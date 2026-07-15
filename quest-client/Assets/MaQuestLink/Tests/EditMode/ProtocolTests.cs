using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

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
        public void HapticAndHandTracking_RoundTrip()
        {
            var haptic = new HapticCommand
            {
                TimestampNs = 10,
                Side = HandSide.Left,
                Action = HapticAction.Apply,
                Amplitude = 0.6f,
                FrequencyHz = 120.0f,
                DurationNs = 20_000_000,
            };
            var decodedHaptic = (HapticCommand)Protocol.Deserialize(
                Protocol.Serialize(new WireMessage(10, haptic))).Payload;
            Assert.That(decodedHaptic.Side, Is.EqualTo(HandSide.Left));
            Assert.That(decodedHaptic.Amplitude, Is.EqualTo(0.6f));
            Assert.That(decodedHaptic.DurationNs, Is.EqualTo(20_000_000ul));

            var hands = CreateHands();
            var bytes = Protocol.Serialize(new WireMessage(11, hands));
            Assert.That(bytes.Length, Is.EqualTo(Protocol.HeaderSize + 1884));
            var decodedHands = (HandTrackingInput)Protocol.Deserialize(bytes).Payload;
            Assert.IsTrue(decodedHands.LeftActive);
            Assert.That(decodedHands.LeftJoints.Length, Is.EqualTo(26));
            Assert.That(decodedHands.LeftJoints[10].Pose.Position.X, Is.EqualTo(-0.24f));
            Assert.That(decodedHands.RightJoints[25].Radius, Is.EqualTo(0.009f));
        }

        [Test]
        public void QuestFeatureMappings_AreStable()
        {
            Assert.That(QuestHaptics.NormalizeFrequency(0), Is.EqualTo(1.0f));
            Assert.That(QuestHaptics.NormalizeFrequency(160), Is.EqualTo(0.5f));
            Assert.That(QuestHaptics.NormalizeFrequency(640), Is.EqualTo(1.0f));
            Assert.That(Protocol.PassthroughApproximationAlpha, Is.EqualTo(0.82f));
            Assert.That(QuestHandSampler.UnityJointIdForOpenXrIndex(0), Is.EqualTo(2));
            Assert.That(QuestHandSampler.UnityJointIdForOpenXrIndex(1), Is.EqualTo(1));
            Assert.That(QuestHandSampler.UnityJointIdForOpenXrIndex(10), Is.EqualTo(11));
        }

        [Test]
        public void ImmersiveProjection_PreservesBothEyePosesAndFovs()
        {
            var frame = new VideoFrame();
            frame.RenderViews[0] = new EyeView
            {
                Pose = new PoseState
                {
                    Position = new Vector3f { X = -0.032f, Y = 1.7f, Z = -0.2f },
                    Orientation = new Quaternionf { X = 0.1f, Y = 0.2f, Z = 0.3f, W = 0.9f },
                    Flags = PoseFlags.PositionValid | PoseFlags.OrientationValid,
                },
                Fov = new FieldOfView
                {
                    AngleLeft = -0.9f, AngleRight = 0.7f, AngleUp = 0.8f, AngleDown = -0.75f,
                },
            };
            frame.RenderViews[1] = new EyeView
            {
                Pose = new PoseState
                {
                    Position = new Vector3f { X = 0.032f, Y = 1.7f, Z = -0.2f },
                    Orientation = new Quaternionf { W = 1.0f },
                    Flags = PoseFlags.PositionValid | PoseFlags.OrientationValid,
                },
                Fov = new FieldOfView
                {
                    AngleLeft = -0.7f, AngleRight = 0.9f, AngleUp = 0.81f, AngleDown = -0.76f,
                },
            };

            Assert.IsTrue(ImmersiveProjectionFeature.TryBuildViews(frame, out var views));
            Assert.That(views, Has.Length.EqualTo(2));
            Assert.That(views[0].PositionX, Is.EqualTo(-0.032f));
            Assert.That(views[0].OrientationW, Is.EqualTo(0.9f));
            Assert.That(views[0].AngleLeft, Is.EqualTo(-0.9f));
            Assert.That(views[1].PositionX, Is.EqualTo(0.032f));
            Assert.That(views[1].AngleRight, Is.EqualTo(0.9f));
        }

        [Test]
        public void HandVisualizer_CountsOnlyActiveValidJoints()
        {
            var hands = CreateHands();
            Assert.That(HandTrackingVisualizer.CountValidJoints(hands), Is.EqualTo(52));

            hands.LeftActive = false;
            Assert.That(HandTrackingVisualizer.CountValidJoints(hands), Is.EqualTo(26));

            hands.RightJoints[10].Pose.Flags = 0;
            Assert.That(HandTrackingVisualizer.CountValidJoints(hands), Is.EqualTo(25));
        }

        [Test]
        public void HandVisualizer_CreatesAndUpdatesBothSkeletons()
        {
            var root = new GameObject("HandVisualizerTest");
            try
            {
                var visualizer = root.AddComponent<HandTrackingVisualizer>();
                visualizer.UpdateHands(CreateHands());
                Assert.IsTrue(visualizer.LeftVisible);
                Assert.IsTrue(visualizer.RightVisible);
                Assert.That(visualizer.VisibleJointCount, Is.EqualTo(52));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
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
                    var hapticBytes = Protocol.Serialize(new WireMessage(3, new HapticCommand
                    {
                        Side = HandSide.Right,
                        Action = HapticAction.Stop,
                    }));
                    await stream.WriteAsync(hapticBytes, 0, hapticBytes.Length);

                    transport.SubmitLatestHands(CreateHands());
                    transport.SubmitLatestPose(CreatePoseInput());
                    MessageHeader header;
                    while (true)
                    {
                        var receivedHeader = new byte[Protocol.HeaderSize];
                        await ReadExactlyWithTimeout(stream, receivedHeader, 3000);
                        header = Protocol.ParseHeader(receivedHeader);
                        var payload = new byte[header.PayloadSize];
                        await ReadExactlyWithTimeout(stream, payload, 3000);
                        var complete = new byte[Protocol.HeaderSize + payload.Length];
                        Buffer.BlockCopy(receivedHeader, 0, complete, 0, receivedHeader.Length);
                        Buffer.BlockCopy(payload, 0, complete, receivedHeader.Length, payload.Length);
                        var message = Protocol.Deserialize(complete);
                        if (message.Payload is ControlMessage ping && ping.Kind == ControlKind.Ping)
                        {
                            var pong = new ControlMessage
                            {
                                Kind = ControlKind.Pong,
                                TimestampNs = ping.TimestampNs + 1_000_000ul,
                                Data = ToLittleEndian(ping.TimestampNs),
                            };
                            var pongBytes = Protocol.Serialize(new WireMessage(99, pong));
                            await stream.WriteAsync(pongBytes, 0, pongBytes.Length);
                            continue;
                        }
                        Assert.That(header.Type, Is.EqualTo(MessageType.PoseInput));
                        break;
                    }

                    VideoFrame received = null;
                    await WaitUntil(() => transport.TryDequeueLatestVideo(out received), 3000);
                    Assert.That(received.Width, Is.EqualTo(100));
                    Assert.That(transport.ReceivedFrames, Is.EqualTo(1));
                    Assert.That(transport.SentPoses, Is.EqualTo(1));
                    await WaitUntil(() => transport.SentHands >= 1, 3000);
                    HapticCommand receivedHaptic = null;
                    await WaitUntil(() => transport.TryDequeueHaptic(out receivedHaptic), 3000);
                    Assert.That(receivedHaptic.Side, Is.EqualTo(HandSide.Right));
                    await WaitUntil(() => transport.HasClockSync, 3000);
                    Assert.That(transport.ClockRoundTripMs, Is.GreaterThanOrEqualTo(0));
                }
            }
            listener.Stop();
        }

        [Test]
        public void ClockSynchronizer_ConvertsHostTimestampsToClientAge()
        {
            var clock = new ClockSynchronizer();
            clock.Update(100, 1110, 120);

            Assert.IsTrue(clock.IsSynchronized);
            Assert.That(clock.HostMinusClientNs, Is.EqualTo(1000));
            Assert.That(clock.RoundTripMs, Is.EqualTo(0.00002).Within(0.000001));
            Assert.That(clock.HostAgeMs(1120, 150), Is.EqualTo(0.00003).Within(0.000001));
        }

        [Test]
        public void WorldFixedPose_UsesStereoRenderPoseInUnityCoordinates()
        {
            var frame = new VideoFrame();
            frame.RenderViews[0].Pose = ValidPose(-0.032f, 1.0f, -2.0f);
            frame.RenderViews[1].Pose = ValidPose(0.032f, 1.0f, -2.0f);

            Assert.IsTrue(ExternalSurfacePresenter.TryGetWorldPose(
                frame, 2.0f, out var position, out var rotation));
            Assert.That(position.x, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(4.0f).Within(0.0001f));
            Assert.That(Quaternion.Angle(rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void WorldFixedPose_RejectsInvalidTrackingPose()
        {
            var frame = new VideoFrame();
            Assert.IsFalse(ExternalSurfacePresenter.TryGetWorldPose(
                frame, 2.0f, out _, out _));
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

        private static HandTrackingInput CreateHands()
        {
            var hands = new HandTrackingInput
            {
                SampleTimestampNs = 987654321,
                LeftActive = true,
                RightActive = true,
            };
            for (var index = 0; index < Protocol.HandJointCount; index++)
            {
                hands.LeftJoints[index] = new HandJointState
                {
                    Pose = ValidPose(-0.25f + index * 0.001f, 1.1f + index * 0.002f, -0.4f),
                    Radius = 0.008f,
                };
                hands.RightJoints[index] = new HandJointState
                {
                    Pose = ValidPose(0.25f + index * 0.001f, 1.1f + index * 0.002f, -0.4f),
                    Radius = 0.009f,
                };
            }
            return hands;
        }

        private static PoseState ValidPose(float x, float y, float z)
        {
            return new PoseState
            {
                Position = new Vector3f { X = x, Y = y, Z = z },
                Orientation = new Quaternionf { W = 1.0f },
                Flags = PoseFlags.PositionValid | PoseFlags.OrientationValid,
            };
        }

        private static byte[] ToLittleEndian(ulong value)
        {
            var bytes = new byte[sizeof(ulong)];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)(value >> (index * 8));
            }
            return bytes;
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
