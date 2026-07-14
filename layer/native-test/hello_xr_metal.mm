#import <Foundation/Foundation.h>
#import <Metal/Metal.h>

#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace {

void check(XrResult result, const char* operation) {
  if (XR_FAILED(result)) {
    throw std::runtime_error(std::string(operation) + " failed with XrResult " +
                             std::to_string(result));
  }
}

[[nodiscard]] bool has_extension(const std::vector<XrExtensionProperties>& extensions,
                                 const char* name) {
  return std::any_of(extensions.begin(), extensions.end(), [name](const auto& extension) {
    return std::strcmp(extension.extensionName, name) == 0;
  });
}

[[nodiscard]] int parse_frame_count(int argc, char** argv) {
  int frame_count = 120;
  for (int i = 1; i < argc; ++i) {
    if (std::strcmp(argv[i], "--frames") == 0 && i + 1 < argc) {
      frame_count = std::stoi(argv[++i]);
    }
  }
  if (frame_count <= 0) {
    throw std::runtime_error("--frames must be greater than zero");
  }
  return frame_count;
}

[[nodiscard]] XrEnvironmentBlendMode choose_blend_mode(XrInstance instance, XrSystemId system_id) {
  std::uint32_t count{};
  check(xrEnumerateEnvironmentBlendModes(instance, system_id,
                                         XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0, &count,
                                         nullptr),
        "xrEnumerateEnvironmentBlendModes(count)");
  std::vector<XrEnvironmentBlendMode> modes(count);
  check(xrEnumerateEnvironmentBlendModes(instance, system_id,
                                         XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, count, &count,
                                         modes.data()),
        "xrEnumerateEnvironmentBlendModes(values)");
  const auto opaque = std::find(modes.begin(), modes.end(), XR_ENVIRONMENT_BLEND_MODE_OPAQUE);
  return opaque != modes.end() ? *opaque : modes.at(0);
}

struct SessionState {
  bool running{};
  bool exit_requested{};
};

void poll_events(XrInstance instance, XrSession session, SessionState& state) {
  XrEventDataBuffer event{.type = XR_TYPE_EVENT_DATA_BUFFER, .next = nullptr};
  while (xrPollEvent(instance, &event) == XR_SUCCESS) {
    if (event.type == XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED) {
      const auto* changed = reinterpret_cast<const XrEventDataSessionStateChanged*>(&event);
      std::cout << "session_state=" << changed->state << '\n';
      if (changed->state == XR_SESSION_STATE_READY && !state.running) {
        const XrSessionBeginInfo begin_info{
            .type = XR_TYPE_SESSION_BEGIN_INFO,
            .next = nullptr,
            .primaryViewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
        };
        check(xrBeginSession(session, &begin_info), "xrBeginSession");
        state.running = true;
      } else if (changed->state == XR_SESSION_STATE_STOPPING && state.running) {
        check(xrEndSession(session), "xrEndSession");
        state.running = false;
      } else if (changed->state == XR_SESSION_STATE_EXITING ||
                 changed->state == XR_SESSION_STATE_LOSS_PENDING) {
        state.exit_requested = true;
      }
    }
    event = {.type = XR_TYPE_EVENT_DATA_BUFFER, .next = nullptr};
  }
}

int run(int argc, char** argv) {
  const int requested_frames = parse_frame_count(argc, argv);

  std::uint32_t extension_count{};
  check(xrEnumerateInstanceExtensionProperties(nullptr, 0, &extension_count, nullptr),
        "xrEnumerateInstanceExtensionProperties(count)");
  std::vector<XrExtensionProperties> extensions(
      extension_count,
      XrExtensionProperties{.type = XR_TYPE_EXTENSION_PROPERTIES, .next = nullptr});
  check(xrEnumerateInstanceExtensionProperties(nullptr, extension_count, &extension_count,
                                               extensions.data()),
        "xrEnumerateInstanceExtensionProperties(values)");
  if (!has_extension(extensions, XR_KHR_METAL_ENABLE_EXTENSION_NAME)) {
    throw std::runtime_error("runtime does not expose XR_KHR_metal_enable");
  }
  std::cout << "metal_extension=" << XR_KHR_METAL_ENABLE_EXTENSION_NAME << '\n';

  const char* enabled_extensions[] = {XR_KHR_METAL_ENABLE_EXTENSION_NAME};
  XrInstanceCreateInfo instance_info{.type = XR_TYPE_INSTANCE_CREATE_INFO, .next = nullptr};
  std::strncpy(instance_info.applicationInfo.applicationName, "MaQuestLinkNativeTest",
               XR_MAX_APPLICATION_NAME_SIZE - 1);
  instance_info.applicationInfo.applicationVersion = 1;
  std::strncpy(instance_info.applicationInfo.engineName, "MaQuestLink",
               XR_MAX_ENGINE_NAME_SIZE - 1);
  instance_info.applicationInfo.engineVersion = 1;
  instance_info.applicationInfo.apiVersion = XR_MAKE_VERSION(1, 0, 0);
  instance_info.enabledExtensionCount = 1;
  instance_info.enabledExtensionNames = enabled_extensions;

  XrInstance instance{XR_NULL_HANDLE};
  check(xrCreateInstance(&instance_info, &instance), "xrCreateInstance");

  XrInstanceProperties instance_properties{.type = XR_TYPE_INSTANCE_PROPERTIES, .next = nullptr};
  check(xrGetInstanceProperties(instance, &instance_properties), "xrGetInstanceProperties");
  std::cout << "runtime=" << instance_properties.runtimeName << " version="
            << XR_VERSION_MAJOR(instance_properties.runtimeVersion) << '.'
            << XR_VERSION_MINOR(instance_properties.runtimeVersion) << '.'
            << XR_VERSION_PATCH(instance_properties.runtimeVersion) << '\n';

  const XrSystemGetInfo system_info{
      .type = XR_TYPE_SYSTEM_GET_INFO,
      .next = nullptr,
      .formFactor = XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY,
  };
  XrSystemId system_id{XR_NULL_SYSTEM_ID};
  check(xrGetSystem(instance, &system_info, &system_id), "xrGetSystem");

  PFN_xrGetMetalGraphicsRequirementsKHR get_metal_requirements{};
  check(xrGetInstanceProcAddr(
            instance, "xrGetMetalGraphicsRequirementsKHR",
            reinterpret_cast<PFN_xrVoidFunction*>(&get_metal_requirements)),
        "xrGetInstanceProcAddr(xrGetMetalGraphicsRequirementsKHR)");
  XrGraphicsRequirementsMetalKHR requirements{
      .type = XR_TYPE_GRAPHICS_REQUIREMENTS_METAL_KHR, .next = nullptr, .metalDevice = nullptr};
  check(get_metal_requirements(instance, system_id, &requirements),
        "xrGetMetalGraphicsRequirementsKHR");

  id<MTLDevice> device = (__bridge id<MTLDevice>)requirements.metalDevice;
  if (device == nil) {
    throw std::runtime_error("runtime returned a null Metal device");
  }
  id<MTLCommandQueue> command_queue = [device newCommandQueue];
  if (command_queue == nil) {
    throw std::runtime_error("failed to create Metal command queue");
  }
  std::cout << "metal_device=" << device.name.UTF8String << '\n';

  const XrGraphicsBindingMetalKHR graphics_binding{
      .type = XR_TYPE_GRAPHICS_BINDING_METAL_KHR,
      .next = nullptr,
      .commandQueue = (__bridge void*)command_queue,
  };
  const XrSessionCreateInfo session_info{
      .type = XR_TYPE_SESSION_CREATE_INFO,
      .next = &graphics_binding,
      .createFlags = 0,
      .systemId = system_id,
  };
  XrSession session{XR_NULL_HANDLE};
  check(xrCreateSession(instance, &session_info, &session), "xrCreateSession");

  std::uint32_t view_count{};
  check(xrEnumerateViewConfigurationViews(instance, system_id,
                                          XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO, 0,
                                          &view_count, nullptr),
        "xrEnumerateViewConfigurationViews(count)");
  std::vector<XrViewConfigurationView> view_configs(
      view_count,
      XrViewConfigurationView{.type = XR_TYPE_VIEW_CONFIGURATION_VIEW, .next = nullptr});
  check(xrEnumerateViewConfigurationViews(instance, system_id,
                                          XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
                                          view_count, &view_count, view_configs.data()),
        "xrEnumerateViewConfigurationViews(values)");
  if (view_count == 0) {
    throw std::runtime_error("runtime returned no stereo views");
  }

  std::uint32_t format_count{};
  check(xrEnumerateSwapchainFormats(session, 0, &format_count, nullptr),
        "xrEnumerateSwapchainFormats(count)");
  std::vector<std::int64_t> formats(format_count);
  check(xrEnumerateSwapchainFormats(session, format_count, &format_count, formats.data()),
        "xrEnumerateSwapchainFormats(values)");
  const std::vector<std::int64_t> preferred_formats = {
      static_cast<std::int64_t>(MTLPixelFormatBGRA8Unorm_sRGB),
      static_cast<std::int64_t>(MTLPixelFormatRGBA8Unorm_sRGB),
      static_cast<std::int64_t>(MTLPixelFormatBGRA8Unorm),
      static_cast<std::int64_t>(MTLPixelFormatRGBA8Unorm),
  };
  const auto selected = std::find_first_of(preferred_formats.begin(), preferred_formats.end(),
                                           formats.begin(), formats.end());
  if (selected == preferred_formats.end()) {
    throw std::runtime_error("runtime returned no supported RGBA/BGRA Metal format");
  }

  std::uint32_t width{};
  std::uint32_t height{};
  for (const auto& view : view_configs) {
    width = std::max(width, view.recommendedImageRectWidth);
    height = std::max(height, view.recommendedImageRectHeight);
  }
  const XrSwapchainCreateInfo swapchain_info{
      .type = XR_TYPE_SWAPCHAIN_CREATE_INFO,
      .next = nullptr,
      .createFlags = 0,
      .usageFlags = XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XR_SWAPCHAIN_USAGE_SAMPLED_BIT,
      .format = *selected,
      .sampleCount = 1,
      .width = width,
      .height = height,
      .faceCount = 1,
      .arraySize = view_count,
      .mipCount = 1,
  };
  XrSwapchain swapchain{XR_NULL_HANDLE};
  check(xrCreateSwapchain(session, &swapchain_info, &swapchain), "xrCreateSwapchain");

  std::uint32_t image_count{};
  check(xrEnumerateSwapchainImages(swapchain, 0, &image_count, nullptr),
        "xrEnumerateSwapchainImages(count)");
  std::vector<XrSwapchainImageMetalKHR> images(
      image_count,
      XrSwapchainImageMetalKHR{
          .type = XR_TYPE_SWAPCHAIN_IMAGE_METAL_KHR, .next = nullptr, .texture = nullptr});
  check(xrEnumerateSwapchainImages(
            swapchain, image_count, &image_count,
            reinterpret_cast<XrSwapchainImageBaseHeader*>(images.data())),
        "xrEnumerateSwapchainImages(values)");

  const XrReferenceSpaceCreateInfo space_info{
      .type = XR_TYPE_REFERENCE_SPACE_CREATE_INFO,
      .next = nullptr,
      .referenceSpaceType = XR_REFERENCE_SPACE_TYPE_LOCAL,
      .poseInReferenceSpace = {.orientation = {.x = 0, .y = 0, .z = 0, .w = 1},
                               .position = {.x = 0, .y = 0, .z = 0}},
  };
  XrSpace local_space{XR_NULL_HANDLE};
  check(xrCreateReferenceSpace(session, &space_info, &local_space), "xrCreateReferenceSpace");

  const XrEnvironmentBlendMode blend_mode = choose_blend_mode(instance, system_id);
  SessionState session_state;
  const auto ready_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(30);
  int rendered_frames = 0;
  while (rendered_frames < requested_frames && !session_state.exit_requested) {
    poll_events(instance, session, session_state);
    if (!session_state.running) {
      if (std::chrono::steady_clock::now() >= ready_deadline) {
        throw std::runtime_error("session did not reach READY within 30 seconds");
      }
      std::this_thread::sleep_for(std::chrono::milliseconds(10));
      continue;
    }

    XrFrameState frame_state{.type = XR_TYPE_FRAME_STATE, .next = nullptr};
    const XrFrameWaitInfo wait_info{.type = XR_TYPE_FRAME_WAIT_INFO, .next = nullptr};
    check(xrWaitFrame(session, &wait_info, &frame_state), "xrWaitFrame");
    const XrFrameBeginInfo begin_info{.type = XR_TYPE_FRAME_BEGIN_INFO, .next = nullptr};
    check(xrBeginFrame(session, &begin_info), "xrBeginFrame");

    std::vector<XrView> views(view_count,
                              XrView{.type = XR_TYPE_VIEW, .next = nullptr});
    XrViewState view_state{.type = XR_TYPE_VIEW_STATE, .next = nullptr};
    std::uint32_t located_view_count{};
    const XrViewLocateInfo locate_info{
        .type = XR_TYPE_VIEW_LOCATE_INFO,
        .next = nullptr,
        .viewConfigurationType = XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
        .displayTime = frame_state.predictedDisplayTime,
        .space = local_space,
    };
    check(xrLocateViews(session, &locate_info, &view_state, view_count, &located_view_count,
                        views.data()),
          "xrLocateViews");

    std::vector<XrCompositionLayerProjectionView> projection_views;
    XrCompositionLayerProjection projection{
        .type = XR_TYPE_COMPOSITION_LAYER_PROJECTION, .next = nullptr};
    const XrCompositionLayerBaseHeader* layers[1]{};
    std::uint32_t layer_count = 0;

    if (frame_state.shouldRender && located_view_count == view_count) {
      std::uint32_t image_index{};
      const XrSwapchainImageAcquireInfo acquire_info{
          .type = XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO, .next = nullptr};
      check(xrAcquireSwapchainImage(swapchain, &acquire_info, &image_index),
            "xrAcquireSwapchainImage");
      const XrSwapchainImageWaitInfo image_wait_info{
          .type = XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO,
          .next = nullptr,
          .timeout = XR_INFINITE_DURATION,
      };
      check(xrWaitSwapchainImage(swapchain, &image_wait_info), "xrWaitSwapchainImage");

      id<MTLTexture> texture = (__bridge id<MTLTexture>)images.at(image_index).texture;
      id<MTLCommandBuffer> command_buffer = [command_queue commandBuffer];
      for (std::uint32_t eye = 0; eye < view_count; ++eye) {
        MTLRenderPassDescriptor* descriptor = [MTLRenderPassDescriptor renderPassDescriptor];
        descriptor.colorAttachments[0].texture = texture;
        descriptor.colorAttachments[0].slice = eye;
        descriptor.colorAttachments[0].loadAction = MTLLoadActionClear;
        descriptor.colorAttachments[0].storeAction = MTLStoreActionStore;
        descriptor.colorAttachments[0].clearColor =
            eye == 0 ? MTLClearColorMake(0.15, 0.02, 0.02, 1.0)
                     : MTLClearColorMake(0.02, 0.02, 0.15, 1.0);
        id<MTLRenderCommandEncoder> encoder =
            [command_buffer renderCommandEncoderWithDescriptor:descriptor];
        [encoder endEncoding];
      }
      [command_buffer commit];
      [command_buffer waitUntilCompleted];

      const XrSwapchainImageReleaseInfo release_info{
          .type = XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO, .next = nullptr};
      check(xrReleaseSwapchainImage(swapchain, &release_info), "xrReleaseSwapchainImage");

      projection_views.reserve(view_count);
      for (std::uint32_t eye = 0; eye < view_count; ++eye) {
        projection_views.push_back(XrCompositionLayerProjectionView{
            .type = XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW,
            .next = nullptr,
            .pose = views[eye].pose,
            .fov = views[eye].fov,
            .subImage = {.swapchain = swapchain,
                         .imageRect = {.offset = {.x = 0, .y = 0},
                                       .extent = {.width = static_cast<std::int32_t>(width),
                                                  .height = static_cast<std::int32_t>(height)}},
                         .imageArrayIndex = eye},
        });
      }
      projection.space = local_space;
      projection.viewCount = view_count;
      projection.views = projection_views.data();
      layers[0] = reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection);
      layer_count = 1;
    }

    const XrFrameEndInfo end_info{
        .type = XR_TYPE_FRAME_END_INFO,
        .next = nullptr,
        .displayTime = frame_state.predictedDisplayTime,
        .environmentBlendMode = blend_mode,
        .layerCount = layer_count,
        .layers = layer_count == 0 ? nullptr : layers,
    };
    check(xrEndFrame(session, &end_info), "xrEndFrame");
    ++rendered_frames;
  }

  check(xrDestroySpace(local_space), "xrDestroySpace");
  check(xrDestroySwapchain(swapchain), "xrDestroySwapchain");
  check(xrDestroySession(session), "xrDestroySession");
  check(xrDestroyInstance(instance), "xrDestroyInstance");
  std::cout << "MAQUESTLINK_FRAME_LOOP_OK frames=" << rendered_frames << '\n';
  return rendered_frames == requested_frames ? 0 : 1;
}

}  // namespace

int main(int argc, char** argv) {
  int result = 1;
  @autoreleasepool {
    try {
      result = run(argc, argv);
    } catch (const std::exception& error) {
      std::cerr << "MAQUESTLINK_NATIVE_TEST_FAILED: " << error.what() << '\n';
    }
  }
  std::cout.flush();
  std::cerr.flush();
  // Simulator v201 can block indefinitely in a process-exit gRPC static destructor after
  // xrDestroyInstance. All OpenXR and Metal resources have already been released above.
  std::_Exit(result);
}
