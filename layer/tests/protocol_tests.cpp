#include "maquestlink/protocol.hpp"

#include <algorithm>
#include <cstdlib>
#include <functional>
#include <iostream>
#include <string_view>

namespace protocol = maquestlink::protocol;

namespace {

int failures = 0;

void expect(bool condition, std::string_view description) {
  if (!condition) {
    std::cerr << "FAIL: " << description << '\n';
    ++failures;
  }
}

void expect_protocol_error(const std::function<void()>& action, std::string_view description) {
  try {
    action();
    expect(false, description);
  } catch (const protocol::ProtocolError&) {
  }
}

protocol::Pose pose(float seed) {
  return {
      .position = {seed, seed + 1.0F, seed + 2.0F},
      .orientation = {seed + 3.0F, seed + 4.0F, seed + 5.0F, seed + 6.0F},
      .flags = protocol::PositionValid | protocol::OrientationValid,
  };
}

protocol::EyeView eye_view(float seed) {
  return {
      .pose = pose(seed),
      .fov = {.angle_left = -seed,
              .angle_right = seed + 0.1F,
              .angle_up = seed + 0.2F,
              .angle_down = -seed - 0.3F},
  };
}

void test_video_round_trip() {
  const protocol::Message expected{
      .sequence = 42,
      .payload = protocol::VideoFrame{
          .capture_timestamp_ns = 123456789,
          .render_views = {eye_view(1.0F), eye_view(2.0F)},
          .width = 3664,
          .height = 1920,
          .codec = protocol::VideoCodec::H264,
          .eye_count = 2,
          .flags = 3,
          .encoded_data = {std::byte{0x00}, std::byte{0x00}, std::byte{0x01}, std::byte{0x65}},
      },
  };
  const auto bytes = protocol::serialize(expected);
  expect(protocol::deserialize(bytes) == expected, "video frame round trip");
  const auto header = protocol::parse_header(bytes);
  expect(header.magic == protocol::kMagic, "video header magic");
  expect(header.version == protocol::kProtocolVersion, "video header version");
  expect(header.type == protocol::MessageType::VideoFrame, "video header type");
  expect(header.sequence == 42, "video header sequence");
  expect(header.payload_size + protocol::kHeaderSize == bytes.size(), "video payload length");
}

void test_pose_input_round_trip() {
  const protocol::ControllerState left{
      .pose = pose(10.0F),
      .buttons = 0xa5,
      .thumbstick = {-0.25F, 0.75F},
      .trigger = 0.5F,
      .grip = 1.0F,
  };
  const protocol::ControllerState right{
      .pose = pose(20.0F),
      .buttons = 0x5a,
      .thumbstick = {1.0F, -1.0F},
      .trigger = 0.25F,
      .grip = 0.75F,
  };
  const protocol::Message expected{
      .sequence = 99,
      .payload = protocol::PoseInput{
          .sample_timestamp_ns = 987654321,
          .head = pose(0.0F),
          .left = left,
          .right = right,
      },
  };
  expect(protocol::deserialize(protocol::serialize(expected)) == expected,
         "pose/input round trip");
}

void test_control_round_trip() {
  const protocol::Message expected{
      .sequence = 7,
      .payload = protocol::ControlMessage{
          .kind = protocol::ControlKind::Hello,
          .flags = 1,
          .timestamp_ns = 456,
          .data = {std::byte{'M'}, std::byte{'a'}, std::byte{'c'}},
      },
  };
  expect(protocol::deserialize(protocol::serialize(expected)) == expected,
         "control round trip");
}

void test_haptic_and_hands_round_trip() {
  const protocol::Message haptic{
      .sequence = 8,
      .payload = protocol::HapticCommand{
          .timestamp_ns = 123,
          .side = protocol::HandSide::Right,
          .action = protocol::HapticAction::Apply,
          .amplitude = 0.75F,
          .frequency_hz = 160.0F,
          .duration_ns = 25'000'000,
      },
  };
  expect(protocol::deserialize(protocol::serialize(haptic)) == haptic,
         "haptic round trip");

  protocol::HandTrackingInput hands{
      .sample_timestamp_ns = 456,
      .left_active = true,
      .right_active = true,
  };
  for (std::size_t index = 0; index < protocol::kHandJointCount; ++index) {
    hands.left_joints[index] = {.pose = pose(static_cast<float>(index)), .radius = 0.008F};
    hands.right_joints[index] = {
        .pose = pose(static_cast<float>(index) + 100.0F), .radius = 0.009F};
  }
  const protocol::Message hand_message{.sequence = 9, .payload = hands};
  expect(protocol::deserialize(protocol::serialize(hand_message)) == hand_message,
         "26-joint hand tracking round trip");
}

void test_wire_is_little_endian() {
  const protocol::Message message{
      .sequence = 0x0102030405060708ULL,
      .payload = protocol::ControlMessage{},
  };
  const auto bytes = protocol::serialize(message);
  expect(bytes[0] == std::byte{0x4d} && bytes[1] == std::byte{0x51} &&
             bytes[2] == std::byte{0x4c} && bytes[3] == std::byte{0x4b},
         "magic is little endian MQLK");
  expect(bytes[12] == std::byte{0x08} && bytes[19] == std::byte{0x01},
         "sequence is little endian");
}

void test_rejects_invalid_messages() {
  const auto valid = protocol::serialize(protocol::Message{
      .sequence = 1,
      .payload = protocol::ControlMessage{
          .kind = protocol::ControlKind::Ping,
          .data = {std::byte{0x01}},
      },
  });

  auto truncated = valid;
  truncated.pop_back();
  expect_protocol_error([&] { (void)protocol::deserialize(truncated); },
                        "reject truncated message");

  auto trailing = valid;
  trailing.push_back(std::byte{0});
  expect_protocol_error([&] { (void)protocol::deserialize(trailing); },
                        "reject trailing message bytes");

  auto bad_magic = valid;
  bad_magic[0] = std::byte{0};
  expect_protocol_error([&] { (void)protocol::deserialize(bad_magic); }, "reject bad magic");

  auto bad_version = valid;
  bad_version[4] = std::byte{0xff};
  expect_protocol_error([&] { (void)protocol::deserialize(bad_version); },
                        "reject unsupported version");

  auto bad_type = valid;
  bad_type[6] = std::byte{0xff};
  bad_type[7] = std::byte{0xff};
  expect_protocol_error([&] { (void)protocol::deserialize(bad_type); },
                        "reject unknown message type");

  auto bad_embedded_size = valid;
  std::fill(bad_embedded_size.begin() + static_cast<std::ptrdiff_t>(32),
            bad_embedded_size.begin() + static_cast<std::ptrdiff_t>(36), std::byte{0xff});
  expect_protocol_error([&] { (void)protocol::deserialize(bad_embedded_size); },
                        "reject invalid embedded data length");
}

}  // namespace

int main() {
  test_video_round_trip();
  test_pose_input_round_trip();
  test_control_round_trip();
  test_haptic_and_hands_round_trip();
  test_wire_is_little_endian();
  test_rejects_invalid_messages();
  if (failures == 0) {
    std::cout << "All protocol tests passed\n";
    return EXIT_SUCCESS;
  }
  std::cerr << failures << " protocol test(s) failed\n";
  return EXIT_FAILURE;
}
