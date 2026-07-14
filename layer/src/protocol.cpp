#include "maquestlink/protocol.hpp"

#include <bit>
#include <limits>
#include <string>
#include <type_traits>

namespace maquestlink::protocol {
namespace {

template <typename T, bool = std::is_enum_v<T>>
struct WireRawType {
  using type = T;
};

template <typename T>
struct WireRawType<T, true> {
  using type = std::underlying_type_t<T>;
};

template <typename T>
using WireRawTypeT = typename WireRawType<T>::type;

class Writer {
 public:
  template <typename T>
    requires(std::is_integral_v<T> || std::is_enum_v<T> || std::is_floating_point_v<T>)
  void write(T value) {
    using Raw = WireRawTypeT<T>;
    if constexpr (std::is_floating_point_v<T>) {
      static_assert(sizeof(T) == sizeof(std::uint32_t));
      write(std::bit_cast<std::uint32_t>(value));
    } else {
      using Unsigned = std::make_unsigned_t<Raw>;
      const auto raw = static_cast<Unsigned>(static_cast<Raw>(value));
      for (std::size_t i = 0; i < sizeof(Unsigned); ++i) {
        bytes_.push_back(static_cast<std::byte>((raw >> (i * 8U)) & 0xffU));
      }
    }
  }

  void write_bytes(std::span<const std::byte> bytes) {
    bytes_.insert(bytes_.end(), bytes.begin(), bytes.end());
  }

  [[nodiscard]] const std::vector<std::byte>& bytes() const { return bytes_; }
  [[nodiscard]] std::vector<std::byte> take() { return std::move(bytes_); }

 private:
  std::vector<std::byte> bytes_;
};

class Reader {
 public:
  explicit Reader(std::span<const std::byte> bytes) : bytes_(bytes) {}

  template <typename T>
    requires(std::is_integral_v<T> || std::is_enum_v<T> || std::is_floating_point_v<T>)
  [[nodiscard]] T read() {
    using Raw = WireRawTypeT<T>;
    if constexpr (std::is_floating_point_v<T>) {
      static_assert(sizeof(T) == sizeof(std::uint32_t));
      return std::bit_cast<T>(read<std::uint32_t>());
    } else {
      using Unsigned = std::make_unsigned_t<Raw>;
      require(sizeof(Unsigned));
      Unsigned value{};
      for (std::size_t i = 0; i < sizeof(Unsigned); ++i) {
        value |= static_cast<Unsigned>(std::to_integer<unsigned int>(bytes_[offset_ + i]))
                 << (i * 8U);
      }
      offset_ += sizeof(Unsigned);
      return static_cast<T>(static_cast<Raw>(value));
    }
  }

  [[nodiscard]] std::vector<std::byte> read_bytes(std::size_t size) {
    require(size);
    const auto begin = bytes_.begin() + static_cast<std::ptrdiff_t>(offset_);
    const auto end = begin + static_cast<std::ptrdiff_t>(size);
    offset_ += size;
    return {begin, end};
  }

  [[nodiscard]] std::size_t remaining() const { return bytes_.size() - offset_; }

 private:
  void require(std::size_t size) const {
    if (size > remaining()) {
      throw ProtocolError("message ended before the declared payload was complete");
    }
  }

  std::span<const std::byte> bytes_;
  std::size_t offset_{};
};

void write_pose(Writer& writer, const Pose& pose) {
  for (const float component : pose.position) {
    writer.write(component);
  }
  for (const float component : pose.orientation) {
    writer.write(component);
  }
  writer.write(pose.flags);
}

[[nodiscard]] Pose read_pose(Reader& reader) {
  Pose pose;
  for (float& component : pose.position) {
    component = reader.read<float>();
  }
  for (float& component : pose.orientation) {
    component = reader.read<float>();
  }
  pose.flags = reader.read<std::uint32_t>();
  return pose;
}

void write_controller(Writer& writer, const ControllerState& controller) {
  write_pose(writer, controller.pose);
  writer.write(controller.buttons);
  for (const float value : controller.thumbstick) {
    writer.write(value);
  }
  writer.write(controller.trigger);
  writer.write(controller.grip);
}

[[nodiscard]] ControllerState read_controller(Reader& reader) {
  ControllerState controller;
  controller.pose = read_pose(reader);
  controller.buttons = reader.read<std::uint64_t>();
  for (float& value : controller.thumbstick) {
    value = reader.read<float>();
  }
  controller.trigger = reader.read<float>();
  controller.grip = reader.read<float>();
  return controller;
}

void write_eye_view(Writer& writer, const EyeView& view) {
  write_pose(writer, view.pose);
  writer.write(view.fov.angle_left);
  writer.write(view.fov.angle_right);
  writer.write(view.fov.angle_up);
  writer.write(view.fov.angle_down);
}

void write_hand_joint(Writer& writer, const HandJoint& joint) {
  write_pose(writer, joint.pose);
  writer.write(joint.radius);
}

[[nodiscard]] HandJoint read_hand_joint(Reader& reader) {
  return {.pose = read_pose(reader), .radius = reader.read<float>()};
}

[[nodiscard]] EyeView read_eye_view(Reader& reader) {
  EyeView view;
  view.pose = read_pose(reader);
  view.fov.angle_left = reader.read<float>();
  view.fov.angle_right = reader.read<float>();
  view.fov.angle_up = reader.read<float>();
  view.fov.angle_down = reader.read<float>();
  return view;
}

[[nodiscard]] MessageType type_of(const Payload& payload) {
  return std::visit(
      [](const auto& value) -> MessageType {
        using T = std::decay_t<decltype(value)>;
        if constexpr (std::is_same_v<T, VideoFrame>) {
          return MessageType::VideoFrame;
        } else if constexpr (std::is_same_v<T, PoseInput>) {
          return MessageType::PoseInput;
        } else if constexpr (std::is_same_v<T, ControlMessage>) {
          return MessageType::Control;
        } else if constexpr (std::is_same_v<T, HapticCommand>) {
          return MessageType::HapticCommand;
        } else {
          return MessageType::HandTrackingInput;
        }
      },
      payload);
}

[[nodiscard]] std::vector<std::byte> serialize_payload(const Payload& payload) {
  Writer writer;
  std::visit(
      [&writer](const auto& value) {
        using T = std::decay_t<decltype(value)>;
        if constexpr (std::is_same_v<T, VideoFrame>) {
          writer.write(value.capture_timestamp_ns);
          for (const auto& view : value.render_views) {
            write_eye_view(writer, view);
          }
          writer.write(value.width);
          writer.write(value.height);
          writer.write(value.codec);
          writer.write(value.eye_count);
          writer.write(value.flags);
          if (value.encoded_data.size() > kMaxPayloadSize) {
            throw ProtocolError("video frame is larger than the protocol payload limit");
          }
          writer.write(static_cast<std::uint32_t>(value.encoded_data.size()));
          writer.write_bytes(value.encoded_data);
        } else if constexpr (std::is_same_v<T, PoseInput>) {
          writer.write(value.sample_timestamp_ns);
          write_pose(writer, value.head);
          write_controller(writer, value.left);
          write_controller(writer, value.right);
        } else if constexpr (std::is_same_v<T, ControlMessage>) {
          writer.write(value.kind);
          writer.write(value.flags);
          writer.write(value.timestamp_ns);
          if (value.data.size() > kMaxPayloadSize) {
            throw ProtocolError("control data is larger than the protocol payload limit");
          }
          writer.write(static_cast<std::uint32_t>(value.data.size()));
          writer.write_bytes(value.data);
        } else if constexpr (std::is_same_v<T, HapticCommand>) {
          writer.write(value.timestamp_ns);
          writer.write(value.side);
          writer.write(value.action);
          writer.write(value.reserved);
          writer.write(value.amplitude);
          writer.write(value.frequency_hz);
          writer.write(value.duration_ns);
        } else {
          writer.write(value.sample_timestamp_ns);
          writer.write(static_cast<std::uint8_t>(value.left_active));
          writer.write(static_cast<std::uint8_t>(value.right_active));
          writer.write(static_cast<std::uint16_t>(kHandJointCount));
          for (const auto& joint : value.left_joints) write_hand_joint(writer, joint);
          for (const auto& joint : value.right_joints) write_hand_joint(writer, joint);
        }
      },
      payload);
  return writer.take();
}

void require_no_trailing_bytes(const Reader& reader) {
  if (reader.remaining() != 0) {
    throw ProtocolError("payload contains trailing bytes");
  }
}

[[nodiscard]] Payload deserialize_payload(MessageType type, std::span<const std::byte> bytes) {
  Reader reader(bytes);
  switch (type) {
    case MessageType::VideoFrame: {
      VideoFrame value;
      value.capture_timestamp_ns = reader.read<std::uint64_t>();
      for (auto& view : value.render_views) {
        view = read_eye_view(reader);
      }
      value.width = reader.read<std::uint32_t>();
      value.height = reader.read<std::uint32_t>();
      value.codec = reader.read<VideoCodec>();
      value.eye_count = reader.read<std::uint8_t>();
      value.flags = reader.read<std::uint16_t>();
      const auto data_size = reader.read<std::uint32_t>();
      value.encoded_data = reader.read_bytes(data_size);
      require_no_trailing_bytes(reader);
      return value;
    }
    case MessageType::PoseInput: {
      PoseInput value;
      value.sample_timestamp_ns = reader.read<std::uint64_t>();
      value.head = read_pose(reader);
      value.left = read_controller(reader);
      value.right = read_controller(reader);
      require_no_trailing_bytes(reader);
      return value;
    }
    case MessageType::Control: {
      ControlMessage value;
      value.kind = reader.read<ControlKind>();
      value.flags = reader.read<std::uint16_t>();
      value.timestamp_ns = reader.read<std::uint64_t>();
      const auto data_size = reader.read<std::uint32_t>();
      value.data = reader.read_bytes(data_size);
      require_no_trailing_bytes(reader);
      return value;
    }
    case MessageType::HapticCommand: {
      HapticCommand value;
      value.timestamp_ns = reader.read<std::uint64_t>();
      value.side = reader.read<HandSide>();
      value.action = reader.read<HapticAction>();
      value.reserved = reader.read<std::uint16_t>();
      value.amplitude = reader.read<float>();
      value.frequency_hz = reader.read<float>();
      value.duration_ns = reader.read<std::uint64_t>();
      if ((value.side != HandSide::Left && value.side != HandSide::Right) ||
          (value.action != HapticAction::Apply && value.action != HapticAction::Stop)) {
        throw ProtocolError("invalid haptic command enum value");
      }
      require_no_trailing_bytes(reader);
      return value;
    }
    case MessageType::HandTrackingInput: {
      HandTrackingInput value;
      value.sample_timestamp_ns = reader.read<std::uint64_t>();
      value.left_active = reader.read<std::uint8_t>() != 0;
      value.right_active = reader.read<std::uint8_t>() != 0;
      const auto joint_count = reader.read<std::uint16_t>();
      if (joint_count != kHandJointCount) {
        throw ProtocolError("hand tracking joint count is not 26");
      }
      for (auto& joint : value.left_joints) joint = read_hand_joint(reader);
      for (auto& joint : value.right_joints) joint = read_hand_joint(reader);
      require_no_trailing_bytes(reader);
      return value;
    }
  }
  throw ProtocolError("unknown message type");
}

}  // namespace

MessageHeader parse_header(std::span<const std::byte> bytes) {
  if (bytes.size() < kHeaderSize) {
    throw ProtocolError("message is shorter than the protocol header");
  }
  Reader reader(bytes.first(kHeaderSize));
  MessageHeader header;
  header.magic = reader.read<std::uint32_t>();
  header.version = reader.read<std::uint16_t>();
  header.type = reader.read<MessageType>();
  header.payload_size = reader.read<std::uint32_t>();
  header.sequence = reader.read<std::uint64_t>();
  if (header.magic != kMagic) {
    throw ProtocolError("invalid protocol magic");
  }
  if (header.version != kProtocolVersion) {
    throw ProtocolError("unsupported protocol version");
  }
  if (header.payload_size > kMaxPayloadSize) {
    throw ProtocolError("declared payload exceeds the protocol limit");
  }
  switch (header.type) {
    case MessageType::VideoFrame:
    case MessageType::PoseInput:
    case MessageType::Control:
    case MessageType::HapticCommand:
    case MessageType::HandTrackingInput:
      break;
    default:
      throw ProtocolError("unknown message type");
  }
  return header;
}

std::vector<std::byte> serialize(const Message& message) {
  auto payload = serialize_payload(message.payload);
  if (payload.size() > kMaxPayloadSize ||
      payload.size() > std::numeric_limits<std::uint32_t>::max()) {
    throw ProtocolError("serialized payload exceeds the protocol limit");
  }

  Writer writer;
  writer.write(kMagic);
  writer.write(kProtocolVersion);
  writer.write(type_of(message.payload));
  writer.write(static_cast<std::uint32_t>(payload.size()));
  writer.write(message.sequence);
  writer.write_bytes(payload);
  return writer.take();
}

Message deserialize(std::span<const std::byte> bytes) {
  const auto header = parse_header(bytes);
  const auto total_size = kHeaderSize + static_cast<std::size_t>(header.payload_size);
  if (bytes.size() != total_size) {
    throw ProtocolError(bytes.size() < total_size ? "message is truncated"
                                                   : "message contains trailing bytes");
  }
  return Message{
      .sequence = header.sequence,
      .payload = deserialize_payload(header.type, bytes.subspan(kHeaderSize)),
  };
}

}  // namespace maquestlink::protocol
