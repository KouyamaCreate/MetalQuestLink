using System;
using System.IO;
using System.Text;

namespace MetalQuestLink.QuestClient
{
    public enum MessageType : ushort
    {
        VideoFrame = 1,
        PoseInput = 2,
        Control = 3,
        HapticCommand = 4,
        HandTrackingInput = 5,
    }

    public enum VideoCodec : byte
    {
        H264 = 1,
        Hevc = 2,
    }

    [Flags]
    public enum VideoFrameFlags : ushort
    {
        KeyFrame = 1 << 0,
        Passthrough = 1 << 1,
        ChromaKeyTransparency = 1 << 2,
    }

    public enum HandSide : byte
    {
        Left = 1,
        Right = 2,
    }

    public enum HapticAction : byte
    {
        Apply = 1,
        Stop = 2,
    }

    public enum ControlKind : ushort
    {
        Hello = 1,
        HelloAck = 2,
        StartStream = 3,
        StopStream = 4,
        Ping = 5,
        Pong = 6,
        Disconnect = 7,
    }

    [Flags]
    public enum PoseFlags : uint
    {
        PositionValid = 1u << 0,
        OrientationValid = 1u << 1,
        PositionTracked = 1u << 2,
        OrientationTracked = 1u << 3,
    }

    [Flags]
    public enum ControllerButtons : ulong
    {
        PrimaryButton = 1ul << 0,
        SecondaryButton = 1ul << 1,
        ThumbstickButton = 1ul << 2,
        MenuButton = 1ul << 3,
        PrimaryTouch = 1ul << 4,
        SecondaryTouch = 1ul << 5,
        ThumbstickTouch = 1ul << 6,
        TriggerTouch = 1ul << 7,
    }

    public struct Vector2f
    {
        public float X;
        public float Y;
    }

    public struct Vector3f
    {
        public float X;
        public float Y;
        public float Z;
    }

    public struct Quaternionf
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    public struct PoseState
    {
        public Vector3f Position;
        public Quaternionf Orientation;
        public PoseFlags Flags;
    }

    public struct ControllerState
    {
        public PoseState Pose;
        public ControllerButtons Buttons;
        public Vector2f Thumbstick;
        public float Trigger;
        public float Grip;
    }

    public struct FieldOfView
    {
        public float AngleLeft;
        public float AngleRight;
        public float AngleUp;
        public float AngleDown;
    }

    public struct EyeView
    {
        public PoseState Pose;
        public FieldOfView Fov;
    }

    public sealed class VideoFrame
    {
        public ulong CaptureTimestampNs;
        public EyeView[] RenderViews = new EyeView[2];
        public uint Width;
        public uint Height;
        public VideoCodec Codec = VideoCodec.H264;
        public byte EyeCount = 2;
        public ushort Flags;
        public byte[] EncodedData = Array.Empty<byte>();
        // Quest-local receive time. This is transport metadata and is not serialized.
        public ulong ReceiveTimestampNs;
    }

    public sealed class PoseInput
    {
        public ulong SampleTimestampNs;
        public PoseState Head;
        public ControllerState Left;
        public ControllerState Right;
    }

    public sealed class ControlMessage
    {
        public ControlKind Kind;
        public ushort Flags;
        public ulong TimestampNs;
        public byte[] Data = Array.Empty<byte>();
    }

    public sealed class HapticCommand
    {
        public ulong TimestampNs;
        public HandSide Side;
        public HapticAction Action;
        public ushort Reserved;
        public float Amplitude;
        public float FrequencyHz;
        public ulong DurationNs;
    }

    public struct HandJointState
    {
        public PoseState Pose;
        public float Radius;
    }

    public sealed class HandTrackingInput
    {
        public ulong SampleTimestampNs;
        public bool LeftActive;
        public bool RightActive;
        public HandJointState[] LeftJoints = new HandJointState[Protocol.HandJointCount];
        public HandJointState[] RightJoints = new HandJointState[Protocol.HandJointCount];
    }

    public sealed class WireMessage
    {
        public ulong Sequence;
        public object Payload;

        public WireMessage(ulong sequence, object payload)
        {
            Sequence = sequence;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }

    public struct MessageHeader
    {
        public uint Magic;
        public ushort Version;
        public MessageType Type;
        public uint PayloadSize;
        public ulong Sequence;
    }

    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    public static class Protocol
    {
        public const uint Magic = 0x4b4c544d;
        public const ushort Version = 1;
        public const int HeaderSize = 20;
        public const float PassthroughApproximationAlpha = 0.82f;
        public const uint MaxPayloadSize = 64u * 1024u * 1024u;
        public const int HandJointCount = 26;

        public static MessageHeader ParseHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize)
            {
                throw new ProtocolException("message is shorter than the protocol header");
            }

            var reader = new WireReader(bytes, 0, HeaderSize);
            var header = new MessageHeader
            {
                Magic = reader.ReadUInt32(),
                Version = reader.ReadUInt16(),
                Type = (MessageType)reader.ReadUInt16(),
                PayloadSize = reader.ReadUInt32(),
                Sequence = reader.ReadUInt64(),
            };
            if (header.Magic != Magic)
            {
                throw new ProtocolException("invalid protocol magic");
            }
            if (header.Version != Version)
            {
                throw new ProtocolException("unsupported protocol version");
            }
            if (header.PayloadSize > MaxPayloadSize)
            {
                throw new ProtocolException("declared payload exceeds the protocol limit");
            }
            if (header.Type != MessageType.VideoFrame && header.Type != MessageType.PoseInput &&
                header.Type != MessageType.Control && header.Type != MessageType.HapticCommand &&
                header.Type != MessageType.HandTrackingInput)
            {
                throw new ProtocolException("unknown message type");
            }
            return header;
        }

        public static byte[] Serialize(WireMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            MessageType type;
            byte[] payload;
            if (message.Payload is VideoFrame video)
            {
                type = MessageType.VideoFrame;
                payload = SerializeVideo(video);
            }
            else if (message.Payload is PoseInput pose)
            {
                type = MessageType.PoseInput;
                payload = SerializePoseInput(pose);
            }
            else if (message.Payload is ControlMessage control)
            {
                type = MessageType.Control;
                payload = SerializeControl(control);
            }
            else if (message.Payload is HapticCommand haptic)
            {
                type = MessageType.HapticCommand;
                payload = SerializeHaptic(haptic);
            }
            else if (message.Payload is HandTrackingInput hands)
            {
                type = MessageType.HandTrackingInput;
                payload = SerializeHands(hands);
            }
            else
            {
                throw new ProtocolException("unsupported message payload type");
            }

            if ((uint)payload.Length > MaxPayloadSize)
            {
                throw new ProtocolException("serialized payload exceeds the protocol limit");
            }

            var writer = new WireWriter(HeaderSize + payload.Length);
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((ushort)type);
            writer.Write((uint)payload.Length);
            writer.Write(message.Sequence);
            writer.Write(payload);
            return writer.ToArray();
        }

        public static WireMessage Deserialize(byte[] bytes)
        {
            var header = ParseHeader(bytes);
            var expectedSize = HeaderSize + checked((int)header.PayloadSize);
            if (bytes.Length != expectedSize)
            {
                throw new ProtocolException(bytes.Length < expectedSize
                    ? "message is truncated"
                    : "message contains trailing bytes");
            }

            var reader = new WireReader(bytes, HeaderSize, (int)header.PayloadSize);
            object payload;
            switch (header.Type)
            {
                case MessageType.VideoFrame:
                    payload = DeserializeVideo(reader);
                    break;
                case MessageType.PoseInput:
                    payload = DeserializePoseInput(reader);
                    break;
                case MessageType.Control:
                    payload = DeserializeControl(reader);
                    break;
                case MessageType.HapticCommand:
                    payload = DeserializeHaptic(reader);
                    break;
                case MessageType.HandTrackingInput:
                    payload = DeserializeHands(reader);
                    break;
                default:
                    throw new ProtocolException("unknown message type");
            }
            reader.RequireComplete();
            return new WireMessage(header.Sequence, payload);
        }

        private static byte[] SerializeVideo(VideoFrame value)
        {
            if (value.RenderViews == null || value.RenderViews.Length != 2)
            {
                throw new ProtocolException("video frame must contain exactly two render views");
            }
            var data = value.EncodedData ?? Array.Empty<byte>();
            var writer = new WireWriter(120 + data.Length);
            writer.Write(value.CaptureTimestampNs);
            WriteEyeView(writer, value.RenderViews[0]);
            WriteEyeView(writer, value.RenderViews[1]);
            writer.Write(value.Width);
            writer.Write(value.Height);
            writer.Write((byte)value.Codec);
            writer.Write(value.EyeCount);
            writer.Write(value.Flags);
            writer.Write((uint)data.Length);
            writer.Write(data);
            return writer.ToArray();
        }

        private static byte[] SerializePoseInput(PoseInput value)
        {
            var writer = new WireWriter(152);
            writer.Write(value.SampleTimestampNs);
            WritePose(writer, value.Head);
            WriteController(writer, value.Left);
            WriteController(writer, value.Right);
            return writer.ToArray();
        }

        private static byte[] SerializeControl(ControlMessage value)
        {
            var data = value.Data ?? Array.Empty<byte>();
            var writer = new WireWriter(16 + data.Length);
            writer.Write((ushort)value.Kind);
            writer.Write(value.Flags);
            writer.Write(value.TimestampNs);
            writer.Write((uint)data.Length);
            writer.Write(data);
            return writer.ToArray();
        }

        private static byte[] SerializeHaptic(HapticCommand value)
        {
            var writer = new WireWriter(28);
            writer.Write(value.TimestampNs);
            writer.Write((byte)value.Side);
            writer.Write((byte)value.Action);
            writer.Write(value.Reserved);
            writer.Write(value.Amplitude);
            writer.Write(value.FrequencyHz);
            writer.Write(value.DurationNs);
            return writer.ToArray();
        }

        private static byte[] SerializeHands(HandTrackingInput value)
        {
            if (value.LeftJoints == null || value.LeftJoints.Length != HandJointCount ||
                value.RightJoints == null || value.RightJoints.Length != HandJointCount)
            {
                throw new ProtocolException("hand tracking must contain 26 joints per hand");
            }
            var writer = new WireWriter(12 + HandJointCount * 2 * 36);
            writer.Write(value.SampleTimestampNs);
            writer.Write((byte)(value.LeftActive ? 1 : 0));
            writer.Write((byte)(value.RightActive ? 1 : 0));
            writer.Write((ushort)HandJointCount);
            foreach (var joint in value.LeftJoints) WriteHandJoint(writer, joint);
            foreach (var joint in value.RightJoints) WriteHandJoint(writer, joint);
            return writer.ToArray();
        }

        private static VideoFrame DeserializeVideo(WireReader reader)
        {
            var value = new VideoFrame { CaptureTimestampNs = reader.ReadUInt64() };
            value.RenderViews[0] = ReadEyeView(reader);
            value.RenderViews[1] = ReadEyeView(reader);
            value.Width = reader.ReadUInt32();
            value.Height = reader.ReadUInt32();
            value.Codec = (VideoCodec)reader.ReadByte();
            value.EyeCount = reader.ReadByte();
            value.Flags = reader.ReadUInt16();
            value.EncodedData = reader.ReadBytes(checked((int)reader.ReadUInt32()));
            return value;
        }

        private static PoseInput DeserializePoseInput(WireReader reader)
        {
            return new PoseInput
            {
                SampleTimestampNs = reader.ReadUInt64(),
                Head = ReadPose(reader),
                Left = ReadController(reader),
                Right = ReadController(reader),
            };
        }

        private static ControlMessage DeserializeControl(WireReader reader)
        {
            var value = new ControlMessage
            {
                Kind = (ControlKind)reader.ReadUInt16(),
                Flags = reader.ReadUInt16(),
                TimestampNs = reader.ReadUInt64(),
            };
            value.Data = reader.ReadBytes(checked((int)reader.ReadUInt32()));
            return value;
        }

        private static HapticCommand DeserializeHaptic(WireReader reader)
        {
            var value = new HapticCommand
            {
                TimestampNs = reader.ReadUInt64(),
                Side = (HandSide)reader.ReadByte(),
                Action = (HapticAction)reader.ReadByte(),
                Reserved = reader.ReadUInt16(),
                Amplitude = reader.ReadSingle(),
                FrequencyHz = reader.ReadSingle(),
                DurationNs = reader.ReadUInt64(),
            };
            if ((value.Side != HandSide.Left && value.Side != HandSide.Right) ||
                (value.Action != HapticAction.Apply && value.Action != HapticAction.Stop))
            {
                throw new ProtocolException("invalid haptic command enum value");
            }
            return value;
        }

        private static HandTrackingInput DeserializeHands(WireReader reader)
        {
            var value = new HandTrackingInput
            {
                SampleTimestampNs = reader.ReadUInt64(),
                LeftActive = reader.ReadByte() != 0,
                RightActive = reader.ReadByte() != 0,
            };
            if (reader.ReadUInt16() != HandJointCount)
            {
                throw new ProtocolException("hand tracking joint count is not 26");
            }
            for (var index = 0; index < HandJointCount; index++) value.LeftJoints[index] = ReadHandJoint(reader);
            for (var index = 0; index < HandJointCount; index++) value.RightJoints[index] = ReadHandJoint(reader);
            return value;
        }

        private static void WritePose(WireWriter writer, PoseState value)
        {
            writer.Write(value.Position.X);
            writer.Write(value.Position.Y);
            writer.Write(value.Position.Z);
            writer.Write(value.Orientation.X);
            writer.Write(value.Orientation.Y);
            writer.Write(value.Orientation.Z);
            writer.Write(value.Orientation.W);
            writer.Write((uint)value.Flags);
        }

        private static PoseState ReadPose(WireReader reader)
        {
            return new PoseState
            {
                Position = new Vector3f { X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle() },
                Orientation = new Quaternionf
                {
                    X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle(), W = reader.ReadSingle(),
                },
                Flags = (PoseFlags)reader.ReadUInt32(),
            };
        }

        private static void WriteHandJoint(WireWriter writer, HandJointState value)
        {
            WritePose(writer, value.Pose);
            writer.Write(value.Radius);
        }

        private static HandJointState ReadHandJoint(WireReader reader)
        {
            return new HandJointState { Pose = ReadPose(reader), Radius = reader.ReadSingle() };
        }

        private static void WriteController(WireWriter writer, ControllerState value)
        {
            WritePose(writer, value.Pose);
            writer.Write((ulong)value.Buttons);
            writer.Write(value.Thumbstick.X);
            writer.Write(value.Thumbstick.Y);
            writer.Write(value.Trigger);
            writer.Write(value.Grip);
        }

        private static ControllerState ReadController(WireReader reader)
        {
            return new ControllerState
            {
                Pose = ReadPose(reader),
                Buttons = (ControllerButtons)reader.ReadUInt64(),
                Thumbstick = new Vector2f { X = reader.ReadSingle(), Y = reader.ReadSingle() },
                Trigger = reader.ReadSingle(),
                Grip = reader.ReadSingle(),
            };
        }

        private static void WriteEyeView(WireWriter writer, EyeView value)
        {
            WritePose(writer, value.Pose);
            writer.Write(value.Fov.AngleLeft);
            writer.Write(value.Fov.AngleRight);
            writer.Write(value.Fov.AngleUp);
            writer.Write(value.Fov.AngleDown);
        }

        private static EyeView ReadEyeView(WireReader reader)
        {
            return new EyeView
            {
                Pose = ReadPose(reader),
                Fov = new FieldOfView
                {
                    AngleLeft = reader.ReadSingle(),
                    AngleRight = reader.ReadSingle(),
                    AngleUp = reader.ReadSingle(),
                    AngleDown = reader.ReadSingle(),
                },
            };
        }

        private sealed class WireWriter
        {
            private readonly MemoryStream stream;
            private readonly BinaryWriter writer;

            public WireWriter(int capacity)
            {
                stream = new MemoryStream(capacity);
                writer = new BinaryWriter(stream, Encoding.UTF8, true);
            }

            public void Write(byte value) => writer.Write(value);
            public void Write(ushort value) => writer.Write(value);
            public void Write(uint value) => writer.Write(value);
            public void Write(ulong value) => writer.Write(value);
            public void Write(float value) => writer.Write(value);
            public void Write(byte[] value) => writer.Write(value);
            public byte[] ToArray() => stream.ToArray();
        }

        private sealed class WireReader
        {
            private readonly MemoryStream stream;
            private readonly BinaryReader reader;
            private readonly long end;

            public WireReader(byte[] bytes, int offset, int length)
            {
                stream = new MemoryStream(bytes, offset, length, false);
                reader = new BinaryReader(stream, Encoding.UTF8, true);
                // MemoryStream(byte[], index, count) exposes Position relative to index.
                end = length;
            }

            public byte ReadByte() { Require(1); return reader.ReadByte(); }
            public ushort ReadUInt16() { Require(2); return reader.ReadUInt16(); }
            public uint ReadUInt32() { Require(4); return reader.ReadUInt32(); }
            public ulong ReadUInt64() { Require(8); return reader.ReadUInt64(); }
            public float ReadSingle() { Require(4); return reader.ReadSingle(); }

            public byte[] ReadBytes(int count)
            {
                Require(count);
                var result = reader.ReadBytes(count);
                if (result.Length != count)
                {
                    throw new ProtocolException("message ended before the declared payload was complete");
                }
                return result;
            }

            public void RequireComplete()
            {
                if (stream.Position != end)
                {
                    throw new ProtocolException("payload contains trailing bytes");
                }
            }

            private void Require(int count)
            {
                if (count < 0 || stream.Position + count > end)
                {
                    throw new ProtocolException("message ended before the declared payload was complete");
                }
            }
        }
    }
}
