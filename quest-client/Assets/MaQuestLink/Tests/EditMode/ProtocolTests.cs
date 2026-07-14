using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MaQuestLink.QuestClient.Tests
{
    public sealed class ProtocolTests
    {
        [Test]
        public void PoseInput_RoundTripsWithCppCompatibleSizeAndHeader()
        {
            var pose = CreatePoseInput();
            var bytes = Protocol.Serialize(new WireMessage(0x0102030405060708ul, pose));

            Assert.That(bytes.Length, Is.EqualTo(Protocol.HeaderSize + 152));
            Assert.That(bytes[0], Is.EqualTo(0x4d));
            Assert.That(bytes[1], Is.EqualTo(0x51));
            Assert.That(bytes[2], Is.EqualTo(0x4c));
            Assert.That(bytes[3], Is.EqualTo(0x4b));
            Assert.That(bytes[4], Is.EqualTo(1));
            Assert.That(bytes[6], Is.EqualTo((byte)MessageType.PoseInput));
            Assert.That(bytes[8], Is.EqualTo(152));

            var decoded = Protocol.Deserialize(bytes);
            var value = decoded.Payload as PoseInput;
            Assert.That(value, Is.Not.Null);
            Assert.That(decoded.Sequence, Is.EqualTo(0x0102030405060708ul));
            Assert.That(value.SampleTimestampNs, Is.EqualTo(123456789ul));
            Assert.That(value.Head.Position.X, Is.EqualTo(1.25f));
            Assert.That(value.Left.Buttons,
                Is.EqualTo(ControllerButtons.PrimaryButton | ControllerButtons.TriggerTouch));
            Assert.That(value.Right.Thumbstick.Y, Is.EqualTo(-0.75f));
            Assert.That(value.Right.Grip, Is.EqualTo(0.875f));
        }

        [Test]
        public void VideoAndControl_RoundTrip()
        {
            var video = new VideoFrame
            {
                CaptureTimestampNs = 42,
                Width = 3360,
                Height = 1760,
                Codec = VideoCodec.H264,
                EyeCount = 2,
                EncodedData = new byte[] { 0, 0, 0, 1, 0x65, 1, 2, 3 },
            };
            video.RenderViews[0].Fov.AngleLeft = -0.8f;
            video.RenderViews[1].Pose.Orientation.W = 1.0f;

            var videoBytes = Protocol.Serialize(new WireMessage(7, video));
            Assert.That(videoBytes.Length, Is.EqualTo(Protocol.HeaderSize + 120 + 8));
            var decodedVideo = (VideoFrame)Protocol.Deserialize(videoBytes).Payload;
            Assert.That(decodedVideo.Width, Is.EqualTo(3360));
            Assert.That(decodedVideo.EncodedData, Is.EqualTo(video.EncodedData));
            Assert.That(decodedVideo.RenderViews[0].Fov.AngleLeft, Is.EqualTo(-0.8f));

            var control = new ControlMessage
            {
                Kind = ControlKind.Ping,
                Flags = 3,
                TimestampNs = 99,
                Data = new byte[] { 9, 8, 7 },
            };
            var decodedControl = (ControlMessage)Protocol.Deserialize(
                Protocol.Serialize(new WireMessage(8, control))).Payload;
            Assert.That(decodedControl.Kind, Is.EqualTo(ControlKind.Ping));
            Assert.That(decodedControl.Data, Is.EqualTo(control.Data));
        }

        [Test]
        public void InvalidMessagesAreRejected()
        {
            var valid = Protocol.Serialize(new WireMessage(1, CreatePoseInput()));
            valid[0] = 0;
            Assert.Throws<ProtocolException>(() => Protocol.Deserialize(valid));
            Assert.Throws<ProtocolException>(() => Protocol.Deserialize(new byte[5]));
        }

        [Test]
        public async Task Transport_ReceivesVideoAndSendsLatestPoseFullDuplex()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var transport = new StreamTransport())
            {
                transport.Start(new[] { "127.0.0.1" }, port);
                var accepted = await WithTimeout(listener.AcceptTcpClientAsync(), 3000);
                using (accepted)
                using (var stream = accepted.GetStream())
                {
                    var video = new VideoFrame
                    {
                        Width = 100,
                        Height = 50,
                        EncodedData = new byte[] { 0, 0, 0, 1, 0x65 },
                    };
                    var videoBytes = Protocol.Serialize(new WireMessage(2, video));
                    await stream.WriteAsync(videoBytes, 0, videoBytes.Length);

                    transport.SubmitLatestPose(CreatePoseInput());
                    var receivedHeader = new byte[Protocol.HeaderSize];
                    await ReadExactlyWithTimeout(stream, receivedHeader, 3000);
                    var header = Protocol.ParseHeader(receivedHeader);
                    Assert.That(header.Type, Is.EqualTo(MessageType.PoseInput));
                    var payload = new byte[header.PayloadSize];
                    await ReadExactlyWithTimeout(stream, payload, 3000);

                    VideoFrame received = null;
                    await WaitUntil(() => transport.TryDequeueLatestVideo(out received), 3000);
                    Assert.That(received.Width, Is.EqualTo(100));
                    Assert.That(transport.ReceivedFrames, Is.EqualTo(1));
                    Assert.That(transport.SentPoses, Is.EqualTo(1));
                }
            }
            listener.Stop();
        }

        private static PoseInput CreatePoseInput()
        {
            return new PoseInput
            {
                SampleTimestampNs = 123456789,
                Head = new PoseState
                {
                    Position = new Vector3f { X = 1.25f, Y = 2.5f, Z = -3.75f },
                    Orientation = new Quaternionf { X = 0.1f, Y = 0.2f, Z = 0.3f, W = 0.9f },
                    Flags = PoseFlags.PositionValid | PoseFlags.OrientationValid,
                },
                Left = new ControllerState
                {
                    Pose = new PoseState { Orientation = new Quaternionf { W = 1 } },
                    Buttons = ControllerButtons.PrimaryButton | ControllerButtons.TriggerTouch,
                    Thumbstick = new Vector2f { X = 0.25f, Y = 0.5f },
                    Trigger = 0.625f,
                    Grip = 0.75f,
                },
                Right = new ControllerState
                {
                    Pose = new PoseState { Orientation = new Quaternionf { W = 1 } },
                    Buttons = ControllerButtons.MenuButton,
                    Thumbstick = new Vector2f { X = -0.5f, Y = -0.75f },
                    Trigger = 0.8125f,
                    Grip = 0.875f,
                },
            };
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int milliseconds)
        {
            if (await Task.WhenAny(task, Task.Delay(milliseconds)) != task)
            {
                throw new TimeoutException();
            }
            return await task;
        }

        private static async Task ReadExactlyWithTimeout(NetworkStream stream, byte[] bytes, int milliseconds)
        {
            using (var cancellation = new CancellationTokenSource(milliseconds))
            {
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, cancellation.Token);
                    if (read == 0)
                    {
                        throw new InvalidOperationException("peer closed the test stream");
                    }
                    offset += read;
                }
            }
        }

        private static async Task WaitUntil(Func<bool> predicate, int milliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!predicate())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException();
                }
                await Task.Delay(10);
            }
        }
    }
}
