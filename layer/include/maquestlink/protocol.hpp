#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <stdexcept>
#include <variant>
#include <vector>

namespace maquestlink::protocol {

inline constexpr std::uint32_t kMagic = 0x4b4c514d;  // "MQLK" on the wire.
inline constexpr std::uint16_t kProtocolVersion = 1;
inline constexpr std::size_t kHeaderSize = 20;
inline constexpr std::uint32_t kMaxPayloadSize = 64U * 1024U * 1024U;

enum class MessageType : std::uint16_t {
  VideoFrame = 1,
  PoseInput = 2,
  Control = 3,
  HapticCommand = 4,
  HandTrackingInput = 5,
};

inline constexpr std::size_t kHandJointCount = 26;

enum class VideoCodec : std::uint8_t {
  H264 = 1,
  Hevc = 2,
};

enum VideoFrameFlags : std::uint16_t {
  KeyFrame = 1U << 0U,
  Passthrough = 1U << 1U,
  ChromaKeyTransparency = 1U << 2U,
};

// Protocol v1 maps Passthrough to an underlay plus fixed uniform overlay alpha.
inline constexpr float kPassthroughApproximationAlpha = 0.82F;

enum class HandSide : std::uint8_t {
  Left = 1,
  Right = 2,
};

enum class HapticAction : std::uint8_t {
  Apply = 1,
  Stop = 2,
};

enum class ControlKind : std::uint16_t {
  Hello = 1,
  HelloAck = 2,
  StartStream = 3,
  StopStream = 4,
  Ping = 5,
  Pong = 6,
  Disconnect = 7,
};

enum PoseFlags : std::uint32_t {
  PositionValid = 1U << 0U,
  OrientationValid = 1U << 1U,
  PositionTracked = 1U << 2U,
  OrientationTracked = 1U << 3U,
};

enum ControllerButtons : std::uint64_t {
  PrimaryButton = 1ULL << 0U,
  SecondaryButton = 1ULL << 1U,
  ThumbstickButton = 1ULL << 2U,
  MenuButton = 1ULL << 3U,
  PrimaryTouch = 1ULL << 4U,
  SecondaryTouch = 1ULL << 5U,
  ThumbstickTouch = 1ULL << 6U,
  TriggerTouch = 1ULL << 7U,
};

struct Pose {
  std::array<float, 3> position{};
  std::array<float, 4> orientation{0.0F, 0.0F, 0.0F, 1.0F};
  std::uint32_t flags{};

  bool operator==(const Pose&) const = default;
};

struct ControllerState {
  Pose pose{};
  std::uint64_t buttons{};
  std::array<float, 2> thumbstick{};
  float trigger{};
  float grip{};

  bool operator==(const ControllerState&) const = default;
};

struct Fov {
  float angle_left{};
  float angle_right{};
  float angle_up{};
  float angle_down{};

  bool operator==(const Fov&) const = default;
};

struct EyeView {
  Pose pose{};
  Fov fov{};

  bool operator==(const EyeView&) const = default;
};

struct VideoFrame {
  std::uint64_t capture_timestamp_ns{};
  std::array<EyeView, 2> render_views{};
  std::uint32_t width{};
  std::uint32_t height{};
  VideoCodec codec{VideoCodec::H264};
  std::uint8_t eye_count{2};
  std::uint16_t flags{};
  std::vector<std::byte> encoded_data{};

  bool operator==(const VideoFrame&) const = default;
};

struct PoseInput {
  std::uint64_t sample_timestamp_ns{};
  Pose head{};
  ControllerState left{};
  ControllerState right{};

  bool operator==(const PoseInput&) const = default;
};

struct ControlMessage {
  ControlKind kind{ControlKind::Hello};
  std::uint16_t flags{};
  std::uint64_t timestamp_ns{};
  std::vector<std::byte> data{};

  bool operator==(const ControlMessage&) const = default;
};

struct HapticCommand {
  std::uint64_t timestamp_ns{};
  HandSide side{HandSide::Left};
  HapticAction action{HapticAction::Apply};
  std::uint16_t reserved{};
  float amplitude{};
  float frequency_hz{};
  std::uint64_t duration_ns{};

  bool operator==(const HapticCommand&) const = default;
};

struct HandJoint {
  Pose pose{};
  float radius{};

  bool operator==(const HandJoint&) const = default;
};

struct HandTrackingInput {
  std::uint64_t sample_timestamp_ns{};
  bool left_active{};
  bool right_active{};
  std::array<HandJoint, kHandJointCount> left_joints{};
  std::array<HandJoint, kHandJointCount> right_joints{};

  bool operator==(const HandTrackingInput&) const = default;
};

using Payload =
    std::variant<VideoFrame, PoseInput, ControlMessage, HapticCommand, HandTrackingInput>;

struct Message {
  std::uint64_t sequence{};
  Payload payload{};

  bool operator==(const Message&) const = default;
};

struct MessageHeader {
  std::uint32_t magic{};
  std::uint16_t version{};
  MessageType type{};
  std::uint32_t payload_size{};
  std::uint64_t sequence{};

  bool operator==(const MessageHeader&) const = default;
};

class ProtocolError : public std::runtime_error {
 public:
  using std::runtime_error::runtime_error;
};

[[nodiscard]] MessageHeader parse_header(std::span<const std::byte> bytes);
[[nodiscard]] std::vector<std::byte> serialize(const Message& message);
[[nodiscard]] Message deserialize(std::span<const std::byte> bytes);

}  // namespace maquestlink::protocol
