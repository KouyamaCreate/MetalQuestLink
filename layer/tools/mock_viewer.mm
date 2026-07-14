#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <Foundation/Foundation.h>
#import <VideoToolbox/VideoToolbox.h>

#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <optional>
#include <span>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#include "maquestlink/protocol.hpp"

namespace {

namespace protocol = maquestlink::protocol;

struct Options {
  int frames{120};
  double minimum_fps{30.0};
  int port{42424};
  bool send_input{};
};

[[nodiscard]] Options parse_options(int argc, char** argv) {
  Options options;
  for (int index = 1; index < argc; ++index) {
    if (std::strcmp(argv[index], "--frames") == 0 && index + 1 < argc) {
      options.frames = std::stoi(argv[++index]);
    } else if (std::strcmp(argv[index], "--min-fps") == 0 && index + 1 < argc) {
      options.minimum_fps = std::stod(argv[++index]);
    } else if (std::strcmp(argv[index], "--port") == 0 && index + 1 < argc) {
      options.port = std::stoi(argv[++index]);
    } else if (std::strcmp(argv[index], "--send-input") == 0) {
      options.send_input = true;
    } else {
      throw std::runtime_error(std::string("unknown or incomplete option: ") + argv[index]);
    }
  }
  if (options.frames <= 1 || options.minimum_fps <= 0.0 || options.port <= 0 ||
      options.port > 65535) {
    throw std::runtime_error("invalid --frames, --min-fps, or --port value");
  }
  return options;
}

[[nodiscard]] std::uint64_t monotonic_now_ns() {
  return static_cast<std::uint64_t>(
      std::chrono::duration_cast<std::chrono::nanoseconds>(
          std::chrono::steady_clock::now().time_since_epoch())
          .count());
}

[[nodiscard]] int connect_with_retry(int port) {
  const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(30);
  while (std::chrono::steady_clock::now() < deadline) {
    const int socket_fd = ::socket(AF_INET, SOCK_STREAM, 0);
    if (socket_fd < 0) {
      throw std::runtime_error("socket creation failed");
    }
    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_port = htons(static_cast<std::uint16_t>(port));
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (::connect(socket_fd, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0) {
      int no_sigpipe = 1;
      (void)::setsockopt(socket_fd, SOL_SOCKET, SO_NOSIGPIPE, &no_sigpipe, sizeof(no_sigpipe));
      timeval timeout{.tv_sec = 30, .tv_usec = 0};
      (void)::setsockopt(socket_fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, sizeof(timeout));
      return socket_fd;
    }
    ::close(socket_fd);
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
  }
  throw std::runtime_error("timed out connecting to MaQuestLink layer");
}

[[nodiscard]] protocol::Pose synthetic_pose(float x, float y, float z) {
  return {
      .position = {x, y, z},
      .orientation = {0.0F, 0.0F, 0.0F, 1.0F},
      .flags = protocol::PositionValid | protocol::OrientationValid |
               protocol::PositionTracked | protocol::OrientationTracked,
  };
}

void send_synthetic_input(std::stop_token stop, int socket_fd,
                          std::atomic<std::uint64_t>& sent_count) {
  while (!stop.stop_requested()) {
    const std::uint64_t sequence = sent_count.load();
    const protocol::Message message{
        .sequence = sequence,
        .payload = protocol::PoseInput{
            .sample_timestamp_ns = monotonic_now_ns(),
            .head = synthetic_pose(1.0F, 2.0F, 3.0F),
            .left = {.pose = synthetic_pose(-0.25F, 1.25F, -0.5F),
                     .buttons = protocol::PrimaryButton | protocol::ThumbstickButton,
                     .thumbstick = {0.25F, -0.5F},
                     .trigger = 0.75F,
                     .grip = 0.5F},
            .right = {.pose = synthetic_pose(0.25F, 1.25F, -0.5F),
                      .buttons = protocol::SecondaryButton,
                      .thumbstick = {-0.25F, 0.5F},
                      .trigger = 0.25F,
                      .grip = 1.0F},
        },
    };
    const auto bytes = protocol::serialize(message);
    std::size_t offset{};
    while (offset < bytes.size() && !stop.stop_requested()) {
      const ssize_t sent = ::send(socket_fd, bytes.data() + offset, bytes.size() - offset, 0);
      if (sent <= 0) {
        return;
      }
      offset += static_cast<std::size_t>(sent);
    }
    sent_count.fetch_add(1);
    std::this_thread::sleep_for(std::chrono::milliseconds(11));
  }
}

void receive_exact(int socket_fd, std::span<std::byte> output) {
  std::size_t offset{};
  while (offset < output.size()) {
    const ssize_t received = ::recv(socket_fd, output.data() + offset, output.size() - offset, 0);
    if (received <= 0) {
      throw std::runtime_error("video stream disconnected or receive timed out");
    }
    offset += static_cast<std::size_t>(received);
  }
}

[[nodiscard]] protocol::Message receive_message(int socket_fd) {
  std::vector<std::byte> bytes(protocol::kHeaderSize);
  receive_exact(socket_fd, bytes);
  const protocol::MessageHeader header = protocol::parse_header(bytes);
  bytes.resize(protocol::kHeaderSize + header.payload_size);
  receive_exact(socket_fd, std::span<std::byte>(bytes).subspan(protocol::kHeaderSize));
  return protocol::deserialize(bytes);
}

[[nodiscard]] std::size_t start_code_length(std::span<const std::byte> bytes, std::size_t offset) {
  if (offset + 3 <= bytes.size() && bytes[offset] == std::byte{0} &&
      bytes[offset + 1] == std::byte{0} && bytes[offset + 2] == std::byte{1}) {
    return 3;
  }
  if (offset + 4 <= bytes.size() && bytes[offset] == std::byte{0} &&
      bytes[offset + 1] == std::byte{0} && bytes[offset + 2] == std::byte{0} &&
      bytes[offset + 3] == std::byte{1}) {
    return 4;
  }
  return 0;
}

[[nodiscard]] std::size_t find_start_code(std::span<const std::byte> bytes, std::size_t offset) {
  while (offset < bytes.size() && start_code_length(bytes, offset) == 0) {
    ++offset;
  }
  return offset;
}

[[nodiscard]] std::vector<std::vector<std::uint8_t>> split_annex_b(
    std::span<const std::byte> bytes) {
  std::vector<std::vector<std::uint8_t>> units;
  std::size_t start = find_start_code(bytes, 0);
  while (start < bytes.size()) {
    const std::size_t header = start_code_length(bytes, start);
    const std::size_t payload = start + header;
    const std::size_t next = find_start_code(bytes, payload);
    if (next > payload) {
      const auto* begin = reinterpret_cast<const std::uint8_t*>(bytes.data() + payload);
      units.emplace_back(begin, begin + (next - payload));
    }
    start = next;
  }
  return units;
}

struct DecoderStats {
  std::atomic<std::uint64_t> decoded{};
  std::atomic<std::uint64_t> first_ns{};
  std::atomic<std::uint64_t> last_ns{};
};

void decompression_callback(void* context, void*, OSStatus status, VTDecodeInfoFlags,
                            CVImageBufferRef image, CMTime, CMTime) {
  if (status != noErr || image == nullptr) {
    return;
  }
  auto* stats = static_cast<DecoderStats*>(context);
  const std::uint64_t now = monotonic_now_ns();
  std::uint64_t unset{};
  (void)stats->first_ns.compare_exchange_strong(unset, now);
  stats->last_ns.store(now);
  stats->decoded.fetch_add(1);
}

class H264Decoder {
 public:
  explicit H264Decoder(DecoderStats& stats) : stats_(stats) {}

  ~H264Decoder() {
    if (session_ != nullptr) {
      VTDecompressionSessionInvalidate(session_);
      CFRelease(session_);
    }
    if (format_ != nullptr) {
      CFRelease(format_);
    }
  }

  void decode(const protocol::VideoFrame& frame, std::uint64_t sequence) {
    const auto units = split_annex_b(frame.encoded_data);
    std::vector<std::uint8_t> access_unit;
    for (const auto& unit : units) {
      if (unit.empty()) {
        continue;
      }
      const std::uint8_t type = unit.front() & 0x1fU;
      if (type == 7) {
        sps_ = unit;
      } else if (type == 8) {
        pps_ = unit;
      } else {
        const auto size = static_cast<std::uint32_t>(unit.size());
        access_unit.push_back(static_cast<std::uint8_t>(size >> 24U));
        access_unit.push_back(static_cast<std::uint8_t>(size >> 16U));
        access_unit.push_back(static_cast<std::uint8_t>(size >> 8U));
        access_unit.push_back(static_cast<std::uint8_t>(size));
        access_unit.insert(access_unit.end(), unit.begin(), unit.end());
      }
    }
    ensure_session();
    if (access_unit.empty()) {
      return;
    }

    CMBlockBufferRef block{};
    if (CMBlockBufferCreateWithMemoryBlock(kCFAllocatorDefault, nullptr, access_unit.size(),
                                           kCFAllocatorDefault, nullptr, 0, access_unit.size(), 0,
                                           &block) != kCMBlockBufferNoErr ||
        CMBlockBufferReplaceDataBytes(access_unit.data(), block, 0, access_unit.size()) !=
            kCMBlockBufferNoErr) {
      if (block != nullptr) {
        CFRelease(block);
      }
      throw std::runtime_error("failed to create H.264 block buffer");
    }
    const CMSampleTimingInfo timing{
        .duration = kCMTimeInvalid,
        .presentationTimeStamp = CMTimeMake(static_cast<std::int64_t>(sequence), 90),
        .decodeTimeStamp = kCMTimeInvalid,
    };
    const std::size_t sample_size = access_unit.size();
    CMSampleBufferRef sample{};
    const OSStatus sample_status = CMSampleBufferCreateReady(
        kCFAllocatorDefault, block, format_, 1, 1, &timing, 1, &sample_size, &sample);
    CFRelease(block);
    if (sample_status != noErr) {
      throw std::runtime_error("failed to create H.264 sample buffer");
    }
    VTDecodeInfoFlags info_flags{};
    const OSStatus decode_status = VTDecompressionSessionDecodeFrame(
        session_, sample, kVTDecodeFrame_EnableAsynchronousDecompression, nullptr, &info_flags);
    CFRelease(sample);
    if (decode_status != noErr || VTDecompressionSessionWaitForAsynchronousFrames(session_) != noErr) {
      throw std::runtime_error("VideoToolbox rejected an H.264 frame");
    }
  }

 private:
  void ensure_session() {
    if (session_ != nullptr) {
      return;
    }
    if (sps_.empty() || pps_.empty()) {
      throw std::runtime_error("stream did not provide H.264 SPS/PPS before video data");
    }
    const std::uint8_t* parameter_sets[] = {sps_.data(), pps_.data()};
    const std::size_t sizes[] = {sps_.size(), pps_.size()};
    if (CMVideoFormatDescriptionCreateFromH264ParameterSets(
            kCFAllocatorDefault, 2, parameter_sets, sizes, 4, &format_) != noErr) {
      throw std::runtime_error("invalid H.264 SPS/PPS");
    }
    NSDictionary* attributes = @{
      (id)kCVPixelBufferPixelFormatTypeKey : @(kCVPixelFormatType_32BGRA),
      (id)kCVPixelBufferMetalCompatibilityKey : @YES,
    };
    const VTDecompressionOutputCallbackRecord callback{
        .decompressionOutputCallback = decompression_callback,
        .decompressionOutputRefCon = &stats_,
    };
    if (VTDecompressionSessionCreate(kCFAllocatorDefault, format_, nullptr,
                                     (__bridge CFDictionaryRef)attributes, &callback,
                                     &session_) != noErr) {
      throw std::runtime_error("failed to create H.264 decoder");
    }
  }

  DecoderStats& stats_;
  std::vector<std::uint8_t> sps_;
  std::vector<std::uint8_t> pps_;
  CMVideoFormatDescriptionRef format_{};
  VTDecompressionSessionRef session_{};
};

int run(int argc, char** argv) {
  const Options options = parse_options(argc, argv);
  const int socket_fd = connect_with_retry(options.port);
  struct SocketCloser {
    int fd;
    ~SocketCloser() { ::close(fd); }
  } socket_closer{socket_fd};

  std::atomic<std::uint64_t> sent_input{};
  std::optional<std::jthread> input_thread;
  if (options.send_input) {
    input_thread.emplace([socket_fd, &sent_input](std::stop_token stop) {
      send_synthetic_input(stop, socket_fd, sent_input);
    });
  }

  DecoderStats stats;
  H264Decoder decoder(stats);
  std::uint64_t received{};
  std::uint32_t width{};
  std::uint32_t height{};
  while (stats.decoded.load() < static_cast<std::uint64_t>(options.frames)) {
    const protocol::Message message = receive_message(socket_fd);
    const auto* frame = std::get_if<protocol::VideoFrame>(&message.payload);
    if (frame == nullptr) {
      continue;
    }
    if (frame->codec != protocol::VideoCodec::H264 || frame->encoded_data.empty() ||
        frame->capture_timestamp_ns == 0 || frame->eye_count != 2 ||
        (frame->render_views[0].pose.flags & protocol::OrientationValid) == 0 ||
        (frame->render_views[1].pose.flags & protocol::OrientationValid) == 0) {
      throw std::runtime_error("invalid video frame metadata or codec");
    }
    if (received == 0) {
      width = frame->width;
      height = frame->height;
    } else if (frame->width != width || frame->height != height) {
      throw std::runtime_error("video dimensions changed during validation");
    }
    decoder.decode(*frame, message.sequence);
    ++received;
  }

  const std::uint64_t decoded = stats.decoded.load();
  const double elapsed_seconds =
      static_cast<double>(stats.last_ns.load() - stats.first_ns.load()) / 1'000'000'000.0;
  const double fps = elapsed_seconds > 0.0 ? static_cast<double>(decoded - 1) / elapsed_seconds : 0.0;
  if (input_thread.has_value()) {
    input_thread->request_stop();
    input_thread->join();
  }
  std::cout << "MAQUESTLINK_VIDEO_E2E_OK received=" << received << " decoded=" << decoded
            << " fps=" << fps << " width=" << width << " height=" << height
            << " input_sent=" << sent_input.load() << '\n';
  if (fps < options.minimum_fps) {
    throw std::runtime_error("decoded frame rate is below the required minimum");
  }
  return 0;
}

}  // namespace

int main(int argc, char** argv) {
  @autoreleasepool {
    try {
      return run(argc, argv);
    } catch (const std::exception& error) {
      std::cerr << "maquestlink_mock_viewer: " << error.what() << '\n';
      return 1;
    }
  }
}
