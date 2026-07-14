using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MaQuestLink.QuestClient
{
    /// <summary>
    /// XR非依存の全二重TCPトランスポート。映像は古いフレームを捨て、入力は常に最新値だけを送る。
    /// </summary>
    public sealed class StreamTransport : IDisposable
    {
        private readonly object videoLock = new object();
        private readonly Queue<VideoFrame> videoQueue = new Queue<VideoFrame>(3);
        private readonly object poseLock = new object();
        private readonly SemaphoreSlim poseReady = new SemaphoreSlim(0, 1);

        private CancellationTokenSource cancellation;
        private Task connectionTask;
        private PoseInput latestPose;
        private string[] hosts = Array.Empty<string>();
        private int port;
        private long sequence;
        private long receivedFrames;
        private long sentPoses;
        private long droppedFrames;
        private int connected;
        private readonly ClockSynchronizer clock = new ClockSynchronizer();
        private ulong lastPingTimestampNs;

        public bool IsConnected => Volatile.Read(ref connected) != 0;
        public long ReceivedFrames => Interlocked.Read(ref receivedFrames);
        public long SentPoses => Interlocked.Read(ref sentPoses);
        public long DroppedFrames => Interlocked.Read(ref droppedFrames);
        public string ConnectedHost { get; private set; } = string.Empty;
        public bool HasClockSync => clock.IsSynchronized;
        public long ClockOffsetNs => clock.HostMinusClientNs;
        public double ClockRoundTripMs => clock.RoundTripMs;

        public double EstimateHostAgeMs(ulong hostTimestampNs, ulong localTimestampNs)
        {
            return clock.HostAgeMs(hostTimestampNs, localTimestampNs);
        }

        public void Start(IEnumerable<string> candidateHosts, int serverPort)
        {
            if (candidateHosts == null)
            {
                throw new ArgumentNullException(nameof(candidateHosts));
            }
            if (serverPort <= 0 || serverPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(serverPort));
            }

            Stop();
            var hostList = new List<string>();
            foreach (var host in candidateHosts)
            {
                if (!string.IsNullOrWhiteSpace(host) && !hostList.Contains(host))
                {
                    hostList.Add(host);
                }
            }
            if (hostList.Count == 0)
            {
                hostList.Add("127.0.0.1");
            }

            hosts = hostList.ToArray();
            port = serverPort;
            cancellation = new CancellationTokenSource();
            connectionTask = Task.Run(() => ConnectionLoopAsync(cancellation.Token));
        }

        public void Stop()
        {
            var source = cancellation;
            cancellation = null;
            if (source != null)
            {
                source.Cancel();
                try
                {
                    connectionTask?.Wait(TimeSpan.FromSeconds(2));
                }
                catch (AggregateException) { }
                source.Dispose();
            }
            connectionTask = null;
            Volatile.Write(ref connected, 0);
            ConnectedHost = string.Empty;
            lock (videoLock)
            {
                videoQueue.Clear();
            }
        }

        public void SubmitLatestPose(PoseInput pose)
        {
            if (pose == null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            lock (poseLock)
            {
                latestPose = pose;
            }
            if (poseReady.CurrentCount == 0)
            {
                poseReady.Release();
            }
        }

        public bool TryDequeueLatestVideo(out VideoFrame frame)
        {
            lock (videoLock)
            {
                if (videoQueue.Count == 0)
                {
                    frame = null;
                    return false;
                }
                while (videoQueue.Count > 1)
                {
                    videoQueue.Dequeue();
                    Interlocked.Increment(ref droppedFrames);
                }
                frame = videoQueue.Dequeue();
                return true;
            }
        }

        public void Dispose()
        {
            Stop();
            poseReady.Dispose();
        }

        private async Task ConnectionLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var host in hosts)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    using (var client = new TcpClient { NoDelay = true })
                    {
                        try
                        {
                            var connect = client.ConnectAsync(host, port);
                            var timeout = Task.Delay(TimeSpan.FromSeconds(2), token);
                            if (await Task.WhenAny(connect, timeout).ConfigureAwait(false) != connect)
                            {
                                client.Close();
                                continue;
                            }
                            await connect.ConfigureAwait(false);
                            ConnectedHost = host;
                            Volatile.Write(ref connected, 1);
                            using (var stream = client.GetStream())
                            {
                                var receive = ReceiveLoopAsync(stream, token);
                                var send = SendLoopAsync(stream, token);
                                await Task.WhenAny(receive, send).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (IOException) { }
                        catch (SocketException) { }
                        catch (ObjectDisposedException) when (token.IsCancellationRequested)
                        {
                            return;
                        }
                        finally
                        {
                            Volatile.Write(ref connected, 0);
                            ConnectedHost = string.Empty;
                        }
                    }
                }

                try
                {
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken token)
        {
            var headerBytes = new byte[Protocol.HeaderSize];
            while (!token.IsCancellationRequested)
            {
                await ReadExactlyAsync(stream, headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
                var header = Protocol.ParseHeader(headerBytes);
                var payload = new byte[header.PayloadSize];
                await ReadExactlyAsync(stream, payload, 0, payload.Length, token).ConfigureAwait(false);
                var complete = new byte[Protocol.HeaderSize + payload.Length];
                Buffer.BlockCopy(headerBytes, 0, complete, 0, headerBytes.Length);
                Buffer.BlockCopy(payload, 0, complete, headerBytes.Length, payload.Length);
                var message = Protocol.Deserialize(complete);
                if (message.Payload is VideoFrame video)
                {
                    video.ReceiveTimestampNs = ClockSynchronizer.NowNs();
                    lock (videoLock)
                    {
                        while (videoQueue.Count >= 3)
                        {
                            videoQueue.Dequeue();
                            Interlocked.Increment(ref droppedFrames);
                        }
                        videoQueue.Enqueue(video);
                    }
                    Interlocked.Increment(ref receivedFrames);
                }
                else if (message.Payload is ControlMessage control &&
                         control.Kind == ControlKind.Pong && control.Data?.Length == sizeof(ulong))
                {
                    clock.Update(ReadUInt64LittleEndian(control.Data), control.TimestampNs,
                        ClockSynchronizer.NowNs());
                }
            }
        }

        private async Task SendLoopAsync(NetworkStream stream, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await poseReady.WaitAsync(token).ConfigureAwait(false);
                PoseInput pose;
                lock (poseLock)
                {
                    pose = latestPose;
                }
                var now = ClockSynchronizer.NowNs();
                if (lastPingTimestampNs == 0 || now - lastPingTimestampNs >= 1_000_000_000ul)
                {
                    lastPingTimestampNs = now;
                    var ping = Protocol.Serialize(new WireMessage(
                        unchecked((ulong)Interlocked.Increment(ref sequence)),
                        new ControlMessage { Kind = ControlKind.Ping, TimestampNs = now }));
                    await stream.WriteAsync(ping, 0, ping.Length, token).ConfigureAwait(false);
                }
                if (pose == null)
                {
                    continue;
                }
                var bytes = Protocol.Serialize(new WireMessage(
                    unchecked((ulong)Interlocked.Increment(ref sequence)), pose));
                await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                Interlocked.Increment(ref sentPoses);
            }
        }

        private static ulong ReadUInt64LittleEndian(byte[] bytes)
        {
            ulong value = 0;
            for (var index = 0; index < sizeof(ulong); index++)
            {
                value |= (ulong)bytes[index] << (index * 8);
            }
            return value;
        }

        private static async Task ReadExactlyAsync(
            NetworkStream stream, byte[] bytes, int offset, int count, CancellationToken token)
        {
            while (count > 0)
            {
                var read = await stream.ReadAsync(bytes, offset, count, token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("MaQuestLink peer closed the stream");
                }
                offset += read;
                count -= read;
            }
        }
    }
}
