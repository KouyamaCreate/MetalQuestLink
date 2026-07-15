#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <VideoToolbox/VideoToolbox.h>

#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include <atomic>
#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <iostream>
#include <iterator>
#include <map>
#include <mutex>
#include <string>
#include <vector>

#include "input_injection.hpp"
#include "maquestlink/protocol.hpp"
#include "streaming.hpp"
#include "transport.hpp"

namespace {

namespace protocol = maquestlink::protocol;

struct InstanceData {
  PFN_xrGetInstanceProcAddr gipa{};
};

struct SessionData {
  XrInstance instance{XR_NULL_HANDLE};
  void* command_queue{};
};

struct SwapchainData {
  XrSession session{XR_NULL_HANDLE};
  XrSwapchainCreateInfo info{.type = XR_TYPE_SWAPCHAIN_CREATE_INFO, .next = nullptr};
  std::vector<void*> textures;
  std::uint32_t last_acquired{};
};

std::mutex g_state_mutex;
std::map<XrInstance, InstanceData> g_instances;
std::map<XrSession, SessionData> g_sessions;
std::map<XrSwapchain, SwapchainData> g_swapchains;

struct FrameMetadata {
  std::uint64_t sequence{};
  std::uint64_t timestamp_ns{};
  std::uint64_t encode_start_ns{};
  std::uint64_t copy_duration_ns{};
  std::array<protocol::EyeView, 2> render_views{};
  std::uint32_t width{};
  std::uint32_t height{};
  bool passthrough{};
};

[[nodiscard]] std::uint64_t monotonic_now_ns() {
  return static_cast<std::uint64_t>(
      std::chrono::duration_cast<std::chrono::nanoseconds>(
          std::chrono::steady_clock::now().time_since_epoch())
          .count());
}

std::atomic<std::uint64_t> g_encoded_frames{};
std::atomic<std::uint64_t> g_dropped_frames{};
std::atomic<std::uint64_t> g_pending_encodes{};
std::atomic<std::uint32_t> g_stream_width{};
std::atomic<std::uint32_t> g_stream_height{};
std::atomic<std::uint64_t> g_total_copy_ns{};
std::atomic<std::uint64_t> g_total_encode_ns{};
std::atomic<bool> g_logged_frame_layout{};
std::atomic<std::uint64_t> g_end_frame_calls{};
std::atomic<std::uint64_t> g_nonempty_end_frames{};
std::atomic<bool> g_transport_was_connected{};
std::mutex g_status_mutex;
std::uint64_t g_last_status_ns{};
std::uint64_t g_last_status_frames{};

[[nodiscard]] bool supported_stream_format(MTLPixelFormat format) {
  switch (format) {
    case MTLPixelFormatBGRA8Unorm:
    case MTLPixelFormatBGRA8Unorm_sRGB:
    case MTLPixelFormatRGBA8Unorm:
    case MTLPixelFormatRGBA8Unorm_sRGB:
    case MTLPixelFormatRGB10A2Unorm:
    case MTLPixelFormatBGR10A2Unorm:
    case MTLPixelFormatBGR10_XR:
    case MTLPixelFormatBGR10_XR_sRGB:
    case MTLPixelFormatRGBA16Float:
      return true;
    default:
      return false;
  }
}

[[nodiscard]] bool requires_compute_copy(MTLPixelFormat format) {
  return format != MTLPixelFormatBGRA8Unorm && format != MTLPixelFormatBGRA8Unorm_sRGB;
}

[[nodiscard]] bool is_srgb_format(MTLPixelFormat format) {
  return format == MTLPixelFormatRGBA8Unorm_sRGB ||
         format == MTLPixelFormatBGR10_XR_sRGB;
}

[[nodiscard]] std::uint32_t environment_uint(const char* name, std::uint32_t fallback,
                                             std::uint32_t minimum, std::uint32_t maximum) {
  const char* value = std::getenv(name);
  if (value == nullptr || *value == '\0') return fallback;
  char* end{};
  const unsigned long parsed = std::strtoul(value, &end, 10);
  if (end == value || *end != '\0' || parsed < minimum || parsed > maximum) return fallback;
  return static_cast<std::uint32_t>(parsed);
}

void log_streaming_line(const std::string& message) {
  std::cerr << "[MaQuestLink streaming] " << message << '\n';
  if (const char* path = std::getenv("MAQUESTLINK_LAYER_LOG"); path != nullptr && path[0] != '\0') {
    std::ofstream stream(path, std::ios::app);
    stream << message << '\n';
  }
}

void write_status(bool force = false) {
  const char* status_path = std::getenv("MAQUESTLINK_STATUS_FILE");
  if (status_path == nullptr || *status_path == '\0') {
    return;
  }

  std::scoped_lock lock(g_status_mutex);
  const std::uint64_t now = monotonic_now_ns();
  if (!force && g_last_status_ns != 0 && now - g_last_status_ns < 1'000'000'000ULL) {
    return;
  }

  const std::uint64_t frames = g_encoded_frames.load();
  const double elapsed_seconds = g_last_status_ns == 0
                                     ? 0.0
                                     : static_cast<double>(now - g_last_status_ns) / 1'000'000'000.0;
  const double fps = elapsed_seconds <= 0.0
                         ? 0.0
                         : static_cast<double>(frames - g_last_status_frames) / elapsed_seconds;
  const double average_copy_ms = frames == 0
                                     ? 0.0
                                     : static_cast<double>(g_total_copy_ns.load()) / frames / 1'000'000.0;
  const double average_encode_ms = frames == 0
                                       ? 0.0
                                       : static_cast<double>(g_total_encode_ns.load()) / frames / 1'000'000.0;

  const std::string path(status_path);
  const std::string temporary_path = path + ".tmp";
  std::ofstream output(temporary_path, std::ios::trunc);
  if (!output) {
    return;
  }
  output << "{\n"
         << "  \"version\": 1,\n"
         << "  \"connected\": " << (transport_connected() ? "true" : "false") << ",\n"
         << "  \"encodedFrames\": " << frames << ",\n"
         << "  \"droppedFrames\": " << g_dropped_frames.load() << ",\n"
         << "  \"streamWidth\": " << g_stream_width.load() << ",\n"
         << "  \"streamHeight\": " << g_stream_height.load() << ",\n"
         << "  \"fps\": " << fps << ",\n"
         << "  \"averageCopyMs\": " << average_copy_ms << ",\n"
         << "  \"averageEncodeMs\": " << average_encode_ms << ",\n"
         << "  \"averagePipelineMs\": " << average_copy_ms + average_encode_ms << "\n"
         << "}\n";
  output.close();
  if (output && std::rename(temporary_path.c_str(), path.c_str()) == 0) {
    g_last_status_ns = now;
    g_last_status_frames = frames;
  } else {
    (void)std::remove(temporary_path.c_str());
  }
}

[[nodiscard]] std::vector<std::byte> annex_b_data(CMSampleBufferRef sample, bool keyframe) {
  std::vector<std::byte> result;
  constexpr std::byte start_code[] = {std::byte{0}, std::byte{0}, std::byte{0}, std::byte{1}};
  CMFormatDescriptionRef format = CMSampleBufferGetFormatDescription(sample);
  if (keyframe && format != nullptr) {
    for (std::size_t index = 0; index < 2; ++index) {
      const std::uint8_t* parameter{};
      std::size_t size{};
      std::size_t count{};
      int header_length{};
      if (CMVideoFormatDescriptionGetH264ParameterSetAtIndex(format, index, &parameter, &size,
                                                             &count, &header_length) == noErr) {
        result.insert(result.end(), std::begin(start_code), std::end(start_code));
        const auto* begin = reinterpret_cast<const std::byte*>(parameter);
        result.insert(result.end(), begin, begin + size);
      }
    }
  }

  CMBlockBufferRef block = CMSampleBufferGetDataBuffer(sample);
  std::size_t total{};
  if (block == nullptr || CMBlockBufferGetDataLength(block) <= 0) {
    return result;
  }
  total = static_cast<std::size_t>(CMBlockBufferGetDataLength(block));
  std::vector<std::uint8_t> avcc(total);
  if (CMBlockBufferCopyDataBytes(block, 0, total, avcc.data()) != kCMBlockBufferNoErr) {
    return {};
  }
  for (std::size_t offset = 0; offset + 4 <= avcc.size();) {
    const std::uint32_t size = (static_cast<std::uint32_t>(avcc[offset]) << 24U) |
                               (static_cast<std::uint32_t>(avcc[offset + 1]) << 16U) |
                               (static_cast<std::uint32_t>(avcc[offset + 2]) << 8U) |
                               static_cast<std::uint32_t>(avcc[offset + 3]);
    offset += 4;
    if (size > avcc.size() - offset) {
      return {};
    }
    result.insert(result.end(), std::begin(start_code), std::end(start_code));
    const auto* begin = reinterpret_cast<const std::byte*>(avcc.data() + offset);
    result.insert(result.end(), begin, begin + size);
    offset += size;
  }
  return result;
}

void compression_callback(void*, void* source_ref, OSStatus status,
                          VTEncodeInfoFlags flags, CMSampleBufferRef sample) {
  std::unique_ptr<FrameMetadata> metadata(static_cast<FrameMetadata*>(source_ref));
  g_pending_encodes.fetch_sub(1);
  if (status != noErr || (flags & kVTEncodeInfo_FrameDropped) != 0 || sample == nullptr ||
      !CMSampleBufferDataIsReady(sample)) {
    g_dropped_frames.fetch_add(1);
    return;
  }
  bool keyframe = true;
  if (CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(sample, false);
      attachments != nullptr && CFArrayGetCount(attachments) > 0) {
    auto dictionary = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(attachments, 0));
    keyframe = !CFDictionaryContainsKey(dictionary, kCMSampleAttachmentKey_NotSync);
  }
  auto bytes = annex_b_data(sample, keyframe);
  if (bytes.empty()) {
    return;
  }
  const std::uint64_t encode_ns = monotonic_now_ns() - metadata->encode_start_ns;
  const std::uint64_t frame_count = g_encoded_frames.fetch_add(1) + 1;
  const std::uint64_t total_copy = g_total_copy_ns.fetch_add(metadata->copy_duration_ns) +
                                   metadata->copy_duration_ns;
  const std::uint64_t total_encode = g_total_encode_ns.fetch_add(encode_ns) + encode_ns;
  if (frame_count % 60 == 0) {
    std::cerr << "MAQUESTLINK_VIDEO_STATS frames=" << frame_count
              << " avg_copy_ms=" << (static_cast<double>(total_copy) / frame_count / 1'000'000.0)
              << " avg_encode_ms=" << (static_cast<double>(total_encode) / frame_count / 1'000'000.0)
              << "\n";
  }
  transport_send(protocol::Message{
      .sequence = metadata->sequence,
      .payload = protocol::VideoFrame{
          .capture_timestamp_ns = metadata->timestamp_ns,
          .render_views = metadata->render_views,
          .width = metadata->width,
          .height = metadata->height,
          .codec = protocol::VideoCodec::H264,
          .eye_count = 2,
          .flags = static_cast<std::uint16_t>(
              (keyframe ? protocol::KeyFrame : 0U) |
              (metadata->passthrough ? protocol::Passthrough : 0U)),
          .encoded_data = std::move(bytes),
      },
  });
}

class VideoEncoder {
 public:
  ~VideoEncoder() {
    if (session_ != nullptr) {
      VTCompressionSessionCompleteFrames(session_, kCMTimeInvalid);
      VTCompressionSessionInvalidate(session_);
      CFRelease(session_);
    }
    if (texture_cache_ != nullptr) {
      CFRelease(texture_cache_);
    }
  }

  void encode(id<MTLCommandQueue> queue, const std::array<id<MTLTexture>, 2>& sources,
              std::uint32_t width,
              std::uint32_t height, const XrCompositionLayerProjectionView* views,
              std::uint64_t capture_timestamp_ns, bool passthrough, bool force_keyframe) {
    std::scoped_lock lock(mutex_);
    const std::uint32_t max_pending =
        environment_uint("MAQUESTLINK_MAX_PENDING_FRAMES", 2, 1, 8);
    if (g_pending_encodes.load() >= max_pending) {
      g_dropped_frames.fetch_add(1);
      return;
    }
    const bool compute_source = std::any_of(sources.begin(), sources.end(), [](id<MTLTexture> source) {
      return requires_compute_copy(source.pixelFormat);
    });
    if (!ensure(queue.device, width * 2, height, compute_source)) {
      return;
    }
    CVPixelBufferRef pixel{};
    NSDictionary* attributes = @{(id)kCVPixelBufferMetalCompatibilityKey : @YES,
                                 (id)kCVPixelBufferIOSurfacePropertiesKey : @{}};
    if (CVPixelBufferCreate(kCFAllocatorDefault, width * 2, height, kCVPixelFormatType_32BGRA,
                            (__bridge CFDictionaryRef)attributes, &pixel) != kCVReturnSuccess) {
      return;
    }
    CVMetalTextureRef cv_texture{};
    if (CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault, texture_cache_, pixel, nullptr,
                                                   MTLPixelFormatBGRA8Unorm, width * 2, height, 0,
                                                   &cv_texture) != kCVReturnSuccess) {
      CFRelease(pixel);
      return;
    }
    id<MTLTexture> destination = CVMetalTextureGetTexture(cv_texture);
    id<MTLCommandBuffer> command = [queue commandBuffer];
    const MTLSize size = MTLSizeMake(width, height, 1);
    if (compute_source) {
      for (std::uint32_t eye = 0; eye < 2; ++eye) {
        id<MTLTexture> source = sources[eye];
        const bool eye_needs_compute = requires_compute_copy(source.pixelFormat);
        const auto& sub_image = views[eye].subImage;
        if (!eye_needs_compute) {
          id<MTLBlitCommandEncoder> blit = [command blitCommandEncoder];
          [blit copyFromTexture:source
                    sourceSlice:sub_image.imageArrayIndex
                    sourceLevel:0
                   sourceOrigin:MTLOriginMake(sub_image.imageRect.offset.x,
                                              sub_image.imageRect.offset.y, 0)
                     sourceSize:size
                      toTexture:destination
               destinationSlice:0
               destinationLevel:0
              destinationOrigin:MTLOriginMake(width * eye, 0, 0)];
          [blit endEncoding];
          continue;
        }
        id<MTLComputePipelineState> pipeline =
            source.textureType == MTLTextureType2DArray ? rgba_array_copy_pipeline_
                                                        : rgba_2d_copy_pipeline_;
        id<MTLComputeCommandEncoder> compute = [command computeCommandEncoder];
        [compute setComputePipelineState:pipeline];
        [compute setTexture:source atIndex:0];
        [compute setTexture:destination atIndex:1];
        const RgbaCopyParameters parameters{
            static_cast<std::uint32_t>(sub_image.imageRect.offset.x),
            static_cast<std::uint32_t>(sub_image.imageRect.offset.y),
            width,
            height,
            width * eye,
            0,
            sub_image.imageArrayIndex,
            is_srgb_format(source.pixelFormat) ? 1U : 0U,
        };
        [compute setBytes:&parameters length:sizeof(parameters) atIndex:0];
        const NSUInteger thread_width = pipeline.threadExecutionWidth;
        const NSUInteger thread_height =
            std::max<NSUInteger>(1, pipeline.maxTotalThreadsPerThreadgroup / thread_width);
        const MTLSize threads = MTLSizeMake(thread_width, thread_height, 1);
        [compute dispatchThreads:size threadsPerThreadgroup:threads];
        [compute endEncoding];
      }
    } else {
      id<MTLBlitCommandEncoder> blit = [command blitCommandEncoder];
      for (std::uint32_t eye = 0; eye < 2; ++eye) {
        [blit copyFromTexture:sources[eye]
                  sourceSlice:views[eye].subImage.imageArrayIndex
                  sourceLevel:0
                 sourceOrigin:MTLOriginMake(views[eye].subImage.imageRect.offset.x,
                                            views[eye].subImage.imageRect.offset.y, 0)
                   sourceSize:size
                    toTexture:destination
             destinationSlice:0
             destinationLevel:0
            destinationOrigin:MTLOriginMake(width * eye, 0, 0)];
      }
      [blit endEncoding];
    }
    [command commit];
    [command waitUntilCompleted];
    const std::uint64_t copy_complete_ns = monotonic_now_ns();

    const std::uint64_t sequence = sequence_++;
    auto metadata = std::make_unique<FrameMetadata>();
    metadata->sequence = sequence;
    metadata->timestamp_ns = capture_timestamp_ns;
    metadata->encode_start_ns = copy_complete_ns;
    metadata->copy_duration_ns = copy_complete_ns - capture_timestamp_ns;
    for (std::size_t eye = 0; eye < metadata->render_views.size(); ++eye) {
      const XrPosef& pose = views[eye].pose;
      const XrFovf& fov = views[eye].fov;
      auto& output = metadata->render_views[eye];
      output.pose.position = {pose.position.x, pose.position.y, pose.position.z};
      output.pose.orientation = {pose.orientation.x, pose.orientation.y, pose.orientation.z,
                                 pose.orientation.w};
      output.pose.flags = protocol::PositionValid | protocol::OrientationValid;
      output.fov = {.angle_left = fov.angleLeft,
                    .angle_right = fov.angleRight,
                    .angle_up = fov.angleUp,
                    .angle_down = fov.angleDown};
    }
    metadata->width = width * 2;
    metadata->height = height;
    metadata->passthrough = passthrough;
    VTEncodeInfoFlags flags{};
    const CMTime pts = CMTimeMake(static_cast<std::int64_t>(sequence), 72);
    NSDictionary* frame_properties = force_keyframe
                                         ? @{(id)kVTEncodeFrameOptionKey_ForceKeyFrame : @YES}
                                         : nil;
    g_pending_encodes.fetch_add(1);
    if (VTCompressionSessionEncodeFrame(session_, pixel, pts, kCMTimeInvalid,
                                        (__bridge CFDictionaryRef)frame_properties,
                                        metadata.get(), &flags) == noErr) {
      (void)metadata.release();
    } else {
      g_pending_encodes.fetch_sub(1);
      g_dropped_frames.fetch_add(1);
    }
    CFRelease(cv_texture);
    CFRelease(pixel);
  }

 private:
  struct RgbaCopyParameters {
    std::uint32_t source_x;
    std::uint32_t source_y;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t destination_x;
    std::uint32_t destination_y;
    std::uint32_t source_slice;
    std::uint32_t encode_srgb;
  };

  bool ensure_rgba_pipelines(id<MTLDevice> device) {
    if (rgba_array_copy_pipeline_ != nil && rgba_2d_copy_pipeline_ != nil &&
        pipeline_device_ == device) {
      return true;
    }
    static NSString* const source = @R"metal(
#include <metal_stdlib>
using namespace metal;

struct CopyParameters {
  uint sourceX;
  uint sourceY;
  uint width;
  uint height;
  uint destinationX;
  uint destinationY;
  uint sourceSlice;
  uint encodeSrgb;
};

float3 linearToSrgb(float3 value) {
  const float3 low = value * 12.92;
  const float3 high = 1.055 * pow(max(value, float3(0.0)), float3(1.0 / 2.4)) - 0.055;
  return select(high, low, value <= 0.0031308);
}

kernel void copyRgbaEyeArray(texture2d_array<float, access::read> source [[texture(0)]],
                        texture2d<float, access::write> destination [[texture(1)]],
                        constant CopyParameters& parameters [[buffer(0)]],
                        uint2 position [[thread_position_in_grid]]) {
  if (position.x >= parameters.width || position.y >= parameters.height) return;
  float4 color = source.read(uint2(parameters.sourceX, parameters.sourceY) + position,
                             parameters.sourceSlice);
  if (parameters.encodeSrgb != 0) color.rgb = linearToSrgb(color.rgb);
  destination.write(color,
                    uint2(parameters.destinationX, parameters.destinationY) + position);
}

kernel void copyRgbaEye2D(texture2d<float, access::read> source [[texture(0)]],
                         texture2d<float, access::write> destination [[texture(1)]],
                         constant CopyParameters& parameters [[buffer(0)]],
                         uint2 position [[thread_position_in_grid]]) {
  if (position.x >= parameters.width || position.y >= parameters.height) return;
  float4 color = source.read(uint2(parameters.sourceX, parameters.sourceY) + position);
  if (parameters.encodeSrgb != 0) color.rgb = linearToSrgb(color.rgb);
  destination.write(color,
                    uint2(parameters.destinationX, parameters.destinationY) + position);
}
)metal";
    NSError* error{};
    id<MTLLibrary> library = [device newLibraryWithSource:source options:nil error:&error];
    id<MTLFunction> array_function = [library newFunctionWithName:@"copyRgbaEyeArray"];
    id<MTLFunction> two_d_function = [library newFunctionWithName:@"copyRgbaEye2D"];
    rgba_array_copy_pipeline_ = array_function == nil
                                    ? nil
                                    : [device newComputePipelineStateWithFunction:array_function
                                                                             error:&error];
    rgba_2d_copy_pipeline_ = two_d_function == nil
                                 ? nil
                                 : [device newComputePipelineStateWithFunction:two_d_function
                                                                          error:&error];
    pipeline_device_ =
        rgba_array_copy_pipeline_ == nil || rgba_2d_copy_pipeline_ == nil ? nil : device;
    if (pipeline_device_ == nil) {
      const char* description = error.localizedDescription.UTF8String;
      log_streaming_line("RGBA conversion pipeline failed: " +
                         std::string(description == nullptr ? "unknown error" : description));
      return false;
    }
    return true;
  }

  bool ensure(id<MTLDevice> device, std::uint32_t width, std::uint32_t height,
              bool requires_rgba_pipeline) {
    if (requires_rgba_pipeline && !ensure_rgba_pipelines(device)) {
      return false;
    }
    if (session_ != nullptr && width_ == width && height_ == height && encoder_device_ == device) {
      return true;
    }
    if (session_ != nullptr) {
      VTCompressionSessionCompleteFrames(session_, kCMTimeInvalid);
      VTCompressionSessionInvalidate(session_);
      CFRelease(session_);
      session_ = nullptr;
      g_pending_encodes.store(0);
    }
    if (texture_cache_ != nullptr) {
      CFRelease(texture_cache_);
      texture_cache_ = nullptr;
    }
    width_ = width;
    height_ = height;
    encoder_device_ = device;
    if (CVMetalTextureCacheCreate(kCFAllocatorDefault, nullptr, device, nullptr,
                                  &texture_cache_) != kCVReturnSuccess ||
        VTCompressionSessionCreate(kCFAllocatorDefault, width, height, kCMVideoCodecType_H264,
                                   nullptr, nullptr, nullptr, compression_callback, nullptr,
                                   &session_) != noErr) {
      return false;
    }
    (void)VTSessionSetProperty(session_, kVTCompressionPropertyKey_RealTime, kCFBooleanTrue);
    (void)VTSessionSetProperty(session_, kVTCompressionPropertyKey_AllowFrameReordering,
                              kCFBooleanFalse);
    (void)VTSessionSetProperty(session_, kVTCompressionPropertyKey_ProfileLevel,
                              kVTProfileLevel_H264_Main_AutoLevel);
    const std::uint32_t configured_mbps =
        environment_uint("MAQUESTLINK_BITRATE_MBPS", 0, 0, 80);
    const double baseline_pixels = 3360.0 * 1760.0;
    const int auto_mbps = std::clamp(
        static_cast<int>(20.0 * static_cast<double>(width) * height / baseline_pixels), 8, 40);
    const int bit_rate =
        static_cast<int>((configured_mbps == 0 ? auto_mbps : configured_mbps) * 1'000'000U);
    CFNumberRef rate = CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &bit_rate);
    (void)VTSessionSetProperty(session_, kVTCompressionPropertyKey_AverageBitRate, rate);
    CFRelease(rate);
    const int key_interval = 60;
    CFNumberRef interval = CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &key_interval);
    (void)VTSessionSetProperty(session_, kVTCompressionPropertyKey_MaxKeyFrameInterval, interval);
    CFRelease(interval);
    const int expected_frame_rate = 72;
    CFNumberRef expected_rate =
        CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &expected_frame_rate);
    (void)VTSessionSetProperty(session_, kVTCompressionPropertyKey_ExpectedFrameRate, expected_rate);
    CFRelease(expected_rate);
    g_stream_width.store(width);
    g_stream_height.store(height);
    log_streaming_line("encoder configured " + std::to_string(width) + "x" +
                       std::to_string(height) + " bitrateMbps=" +
                       std::to_string(bit_rate / 1'000'000) + " maxPending=" +
                       std::to_string(environment_uint(
                           "MAQUESTLINK_MAX_PENDING_FRAMES", 2, 1, 8)));
    return VTCompressionSessionPrepareToEncodeFrames(session_) == noErr;
  }

  std::mutex mutex_;
  VTCompressionSessionRef session_{};
  CVMetalTextureCacheRef texture_cache_{};
  std::uint32_t width_{};
  std::uint32_t height_{};
  std::uint64_t sequence_{};
  id<MTLDevice> encoder_device_{};
  id<MTLDevice> pipeline_device_{};
  id<MTLComputePipelineState> rgba_array_copy_pipeline_{};
  id<MTLComputePipelineState> rgba_2d_copy_pipeline_{};
};

VideoEncoder g_encoder;

template <typename Function>
[[nodiscard]] Function next_function(XrInstance instance, const char* name) {
  PFN_xrGetInstanceProcAddr gipa{};
  {
    std::scoped_lock lock(g_state_mutex);
    const auto found = g_instances.find(instance);
    if (found == g_instances.end()) {
      return nullptr;
    }
    gipa = found->second.gipa;
  }
  PFN_xrVoidFunction function{};
  if (XR_FAILED(gipa(instance, name, &function))) {
    return nullptr;
  }
  return reinterpret_cast<Function>(function);
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_session(XrInstance instance,
                                                   const XrSessionCreateInfo* info,
                                                   XrSession* session) {
  const auto next = next_function<PFN_xrCreateSession>(instance, "xrCreateSession");
  if (next == nullptr) return XR_ERROR_HANDLE_INVALID;
  const XrResult result = next(instance, info, session);
  if (XR_SUCCEEDED(result)) {
    void* queue{};
    for (auto* chain = static_cast<const XrBaseInStructure*>(info->next); chain != nullptr;
         chain = chain->next) {
      log_streaming_line("session next structure type=" +
                         std::to_string(static_cast<std::int64_t>(chain->type)));
      if (chain->type == XR_TYPE_GRAPHICS_BINDING_METAL_KHR) {
        queue = reinterpret_cast<const XrGraphicsBindingMetalKHR*>(chain)->commandQueue;
      }
    }
    std::scoped_lock lock(g_state_mutex);
    g_sessions[*session] = SessionData{instance, queue};
    input_register_session(*session, instance);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_destroy_session(XrSession session) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_state_mutex);
    const auto found = g_sessions.find(session);
    if (found == g_sessions.end()) return XR_ERROR_HANDLE_INVALID;
    instance = found->second.instance;
  }
  const auto next = next_function<PFN_xrDestroySession>(instance, "xrDestroySession");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session);
  if (XR_SUCCEEDED(result)) {
    input_unregister_session(session);
    std::scoped_lock lock(g_state_mutex);
    g_sessions.erase(session);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_swapchain(XrSession session,
                                                     const XrSwapchainCreateInfo* info,
                                                     XrSwapchain* swapchain) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_state_mutex);
    if (!g_sessions.contains(session)) return XR_ERROR_HANDLE_INVALID;
    instance = g_sessions[session].instance;
  }
  const auto next = next_function<PFN_xrCreateSwapchain>(instance, "xrCreateSwapchain");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, swapchain);
  if (XR_SUCCEEDED(result)) {
    std::scoped_lock lock(g_state_mutex);
    g_swapchains[*swapchain] = SwapchainData{session, *info, {}, 0};
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_destroy_swapchain(XrSwapchain swapchain) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_state_mutex);
    if (!g_swapchains.contains(swapchain)) return XR_ERROR_HANDLE_INVALID;
    instance = g_sessions[g_swapchains[swapchain].session].instance;
  }
  const auto next = next_function<PFN_xrDestroySwapchain>(instance, "xrDestroySwapchain");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(swapchain);
  if (XR_SUCCEEDED(result)) {
    std::scoped_lock lock(g_state_mutex);
    g_swapchains.erase(swapchain);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_enumerate_images(XrSwapchain swapchain, std::uint32_t capacity,
                                                     std::uint32_t* count,
                                                     XrSwapchainImageBaseHeader* images) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_state_mutex);
    if (!g_swapchains.contains(swapchain)) return XR_ERROR_HANDLE_INVALID;
    instance = g_sessions[g_swapchains[swapchain].session].instance;
  }
  const auto next = next_function<PFN_xrEnumerateSwapchainImages>(instance, "xrEnumerateSwapchainImages");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(swapchain, capacity, count, images);
  if (XR_SUCCEEDED(result) && capacity > 0 && images != nullptr && count != nullptr) {
    log_streaming_line("swapchain images type=" +
                       std::to_string(static_cast<std::int64_t>(images->type)) +
                       " count=" + std::to_string(*count));
    auto* metal = reinterpret_cast<XrSwapchainImageMetalKHR*>(images);
    std::scoped_lock lock(g_state_mutex);
    auto& textures = g_swapchains[swapchain].textures;
    textures.resize(*count);
    for (std::uint32_t index = 0; index < *count; ++index) textures[index] = metal[index].texture;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_acquire_image(XrSwapchain swapchain,
                                                  const XrSwapchainImageAcquireInfo* info,
                                                  std::uint32_t* index) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_state_mutex);
    if (!g_swapchains.contains(swapchain)) return XR_ERROR_HANDLE_INVALID;
    instance = g_sessions[g_swapchains[swapchain].session].instance;
  }
  const auto next = next_function<PFN_xrAcquireSwapchainImage>(instance, "xrAcquireSwapchainImage");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(swapchain, info, index);
  if (XR_SUCCEEDED(result) && index != nullptr) {
    std::scoped_lock lock(g_state_mutex);
    g_swapchains[swapchain].last_acquired = *index;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_end_frame(XrSession session, const XrFrameEndInfo* info) {
  SessionData session_data;
  XrInstance instance{};
  {
    std::scoped_lock lock(g_state_mutex);
    if (!g_sessions.contains(session)) return XR_ERROR_HANDLE_INVALID;
    session_data = g_sessions[session];
    instance = session_data.instance;
  }
  const bool connected = transport_connected();
  const bool force_keyframe = connected && !g_transport_was_connected.load();
  if (!connected) g_transport_was_connected.store(false);
  if (info != nullptr) {
    const auto calls = g_end_frame_calls.fetch_add(1) + 1;
    if (info->layerCount > 0) g_nonempty_end_frames.fetch_add(1);
    if (calls % 300 == 0 && g_encoded_frames.load() == 0) {
      log_streaming_line("capture pending endFrames=" + std::to_string(calls) +
                         " nonempty=" + std::to_string(g_nonempty_end_frames.load()) +
                         " connected=" + (connected ? "1" : "0") +
                         " commandQueue=" +
                         (session_data.command_queue != nullptr ? "present" : "missing"));
    }
  }
  if (connected && session_data.command_queue != nullptr && info != nullptr) {
    const bool log_layout = info->layerCount > 0 && !g_logged_frame_layout.exchange(true);
    if (log_layout) {
      log_streaming_line("frame layout layerCount=" + std::to_string(info->layerCount) +
                         " commandQueue=present");
    }
    const std::uint64_t capture_timestamp_ns = monotonic_now_ns();
    for (std::uint32_t layer_index = 0; layer_index < info->layerCount; ++layer_index) {
      const auto* base = info->layers[layer_index];
      if (log_layout && base != nullptr) {
        log_streaming_line("frame layer[" + std::to_string(layer_index) + "] type=" +
                           std::to_string(static_cast<std::int64_t>(base->type)));
      }
      if (base == nullptr || base->type != XR_TYPE_COMPOSITION_LAYER_PROJECTION) continue;
      const auto* projection = reinterpret_cast<const XrCompositionLayerProjection*>(base);
      if (log_layout) {
        log_streaming_line("projection viewCount=" + std::to_string(projection->viewCount));
      }
      if (projection->viewCount < 2 || projection->views == nullptr) continue;
      const auto& left = projection->views[0].subImage;
      const auto& right = projection->views[1].subImage;
      if (log_layout) {
        log_streaming_line(
            "projection leftSwapchain=" +
            std::to_string(reinterpret_cast<std::uintptr_t>(left.swapchain)) +
            " rightSwapchain=" +
            std::to_string(reinterpret_cast<std::uintptr_t>(right.swapchain)) +
            " leftArray=" + std::to_string(left.imageArrayIndex) +
            " rightArray=" + std::to_string(right.imageArrayIndex) +
            " leftExtent=" + std::to_string(left.imageRect.extent.width) + "x" +
            std::to_string(left.imageRect.extent.height) + " rightExtent=" +
            std::to_string(right.imageRect.extent.width) + "x" +
            std::to_string(right.imageRect.extent.height));
      }
      if (left.imageRect.extent.width <= 0 ||
          left.imageRect.extent.height <= 0 ||
          right.imageRect.extent.width != left.imageRect.extent.width ||
          right.imageRect.extent.height != left.imageRect.extent.height) {
        continue;
      }
      std::array<SwapchainData, 2> swapchain_data;
      bool has_swapchain_data = true;
      {
        std::scoped_lock lock(g_state_mutex);
        for (std::uint32_t eye = 0; eye < 2; ++eye) {
          const auto found = g_swapchains.find(projection->views[eye].subImage.swapchain);
          if (found == g_swapchains.end()) {
            has_swapchain_data = false;
            break;
          }
          swapchain_data[eye] = found->second;
        }
      }
      if (!has_swapchain_data) continue;
      std::array<id<MTLTexture>, 2> textures{};
      bool valid_textures = true;
      for (std::uint32_t eye = 0; eye < 2; ++eye) {
        const auto& sub_image = projection->views[eye].subImage;
        const auto& data = swapchain_data[eye];
        if (data.last_acquired >= data.textures.size() ||
            sub_image.imageArrayIndex >= data.info.arraySize) {
          valid_textures = false;
          break;
        }
        textures[eye] = (__bridge id<MTLTexture>)data.textures[data.last_acquired];
        const auto texture_type = textures[eye].textureType;
        const auto pixel_format = textures[eye].pixelFormat;
        const bool valid_slice = texture_type == MTLTextureType2D
                                     ? sub_image.imageArrayIndex == 0
                                     : sub_image.imageArrayIndex < textures[eye].arrayLength;
        const bool valid_rect = sub_image.imageRect.offset.x >= 0 &&
                                sub_image.imageRect.offset.y >= 0 &&
                                static_cast<std::uint64_t>(sub_image.imageRect.offset.x) +
                                        static_cast<std::uint64_t>(sub_image.imageRect.extent.width) <=
                                    textures[eye].width &&
                                static_cast<std::uint64_t>(sub_image.imageRect.offset.y) +
                                        static_cast<std::uint64_t>(sub_image.imageRect.extent.height) <=
                                    textures[eye].height;
        if (textures[eye] == nil ||
            (texture_type != MTLTextureType2D && texture_type != MTLTextureType2DArray) ||
            !supported_stream_format(pixel_format) || !valid_slice || !valid_rect) {
          valid_textures = false;
          break;
        }
      }
      if (!valid_textures) continue;
      id<MTLCommandQueue> queue = (__bridge id<MTLCommandQueue>)session_data.command_queue;
      if (log_layout) {
        for (std::uint32_t eye = 0; eye < 2; ++eye) {
          log_streaming_line("projection texture[" + std::to_string(eye) + "] format=" +
                             std::to_string(static_cast<std::uint64_t>(textures[eye].pixelFormat)) +
                             " type=" +
                             std::to_string(static_cast<std::uint64_t>(textures[eye].textureType)) +
                             " arrayLength=" + std::to_string(textures[eye].arrayLength));
        }
        log_streaming_line(std::string("projection swapchainMode=") +
                           (left.swapchain == right.swapchain ? "single-array" : "per-eye"));
      }
      g_transport_was_connected.store(true);
      const bool passthrough =
          info->environmentBlendMode == XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND ||
          info->environmentBlendMode == XR_ENVIRONMENT_BLEND_MODE_ADDITIVE ||
          (projection->layerFlags & XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT) != 0;
      g_encoder.encode(queue, textures,
                       static_cast<std::uint32_t>(left.imageRect.extent.width),
                       static_cast<std::uint32_t>(left.imageRect.extent.height),
                       projection->views, capture_timestamp_ns, passthrough, force_keyframe);
      break;
    }
  }
  write_status();
  const auto next = next_function<PFN_xrEndFrame>(instance, "xrEndFrame");
  return next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info);
}

}  // namespace

void streaming_register_instance(XrInstance instance, PFN_xrGetInstanceProcAddr gipa) {
  {
    std::scoped_lock lock(g_state_mutex);
    g_instances[instance] = InstanceData{gipa};
  }
  transport_start();
  write_status(true);
}

void streaming_unregister_instance(XrInstance instance) {
  {
    std::scoped_lock lock(g_state_mutex);
    for (auto swapchain = g_swapchains.begin(); swapchain != g_swapchains.end();) {
      const auto session = g_sessions.find(swapchain->second.session);
      if (session != g_sessions.end() && session->second.instance == instance) {
        swapchain = g_swapchains.erase(swapchain);
      } else {
        ++swapchain;
      }
    }
    for (auto session = g_sessions.begin(); session != g_sessions.end();) {
      if (session->second.instance == instance) {
        input_unregister_session(session->first);
        session = g_sessions.erase(session);
      } else {
        ++session;
      }
    }
    g_instances.erase(instance);
  }
  transport_stop();
  write_status(true);
}

bool streaming_get_proc_addr(const char* name, PFN_xrVoidFunction* function) {
  struct Entry { const char* name; PFN_xrVoidFunction function; };
  const Entry entries[] = {
      {"xrCreateSession", reinterpret_cast<PFN_xrVoidFunction>(hook_create_session)},
      {"xrDestroySession", reinterpret_cast<PFN_xrVoidFunction>(hook_destroy_session)},
      {"xrCreateSwapchain", reinterpret_cast<PFN_xrVoidFunction>(hook_create_swapchain)},
      {"xrDestroySwapchain", reinterpret_cast<PFN_xrVoidFunction>(hook_destroy_swapchain)},
      {"xrEnumerateSwapchainImages", reinterpret_cast<PFN_xrVoidFunction>(hook_enumerate_images)},
      {"xrAcquireSwapchainImage", reinterpret_cast<PFN_xrVoidFunction>(hook_acquire_image)},
      {"xrEndFrame", reinterpret_cast<PFN_xrVoidFunction>(hook_end_frame)},
  };
  for (const auto& entry : entries) {
    if (std::strcmp(name, entry.name) == 0) {
      *function = entry.function;
      return true;
    }
  }
  return false;
}
