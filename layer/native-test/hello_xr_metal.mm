#import <Foundation/Foundation.h>
#import <Metal/Metal.h>

#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
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

struct InputTest {
  bool enabled{};
  XrPath left_path{XR_NULL_PATH};
  XrPath right_path{XR_NULL_PATH};
  XrActionSet action_set{XR_NULL_HANDLE};
  XrAction pose_action{XR_NULL_HANDLE};
  XrAction primary_action{XR_NULL_HANDLE};
  XrAction trigger_action{XR_NULL_HANDLE};
  XrAction thumbstick_action{XR_NULL_HANDLE};
  XrSpace left_action_space{XR_NULL_HANDLE};
  bool views_verified{};
  bool actions_verified{};
  bool space_verified{};
};

[[nodiscard]] XrPosef identity_pose() {
  return {.orientation = {.x = 0, .y = 0, .z = 0, .w = 1},
          .position = {.x = 0, .y = 0, .z = 0}};
}

[[nodiscard]] XrAction create_action(XrActionSet action_set, XrActionType type,
                                     const char* name, const char* localized_name,
                                     const std::array<XrPath, 2>& subactions) {
  XrActionCreateInfo info{.type = XR_TYPE_ACTION_CREATE_INFO, .next = nullptr};
  std::strncpy(info.actionName, name, XR_MAX_ACTION_NAME_SIZE - 1);
  info.actionType = type;
  info.countSubactionPaths = static_cast<std::uint32_t>(subactions.size());
  info.subactionPaths = subactions.data();
  std::strncpy(info.localizedActionName, localized_name,
               XR_MAX_LOCALIZED_ACTION_NAME_SIZE - 1);
  XrAction action{XR_NULL_HANDLE};
  check(xrCreateAction(action_set, &info, &action), "xrCreateAction");
  return action;
}

[[nodiscard]] InputTest create_input_test(XrInstance instance, bool enabled) {
  InputTest test{.enabled = enabled};
  if (!enabled) {
    return test;
  }
  check(xrStringToPath(instance, "/user/hand/left", &test.left_path),
        "xrStringToPath(left)");
  check(xrStringToPath(instance, "/user/hand/right", &test.right_path),
        "xrStringToPath(right)");
  const std::array<XrPath, 2> subactions = {test.left_path, test.right_path};

  XrActionSetCreateInfo set_info{.type = XR_TYPE_ACTION_SET_CREATE_INFO, .next = nullptr};
  std::strncpy(set_info.actionSetName, "maquestlink_input", XR_MAX_ACTION_SET_NAME_SIZE - 1);
  std::strncpy(set_info.localizedActionSetName, "MaQuestLink Input",
               XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE - 1);
  set_info.priority = 0;
  check(xrCreateActionSet(instance, &set_info, &test.action_set), "xrCreateActionSet");
  test.pose_action = create_action(test.action_set, XR_ACTION_TYPE_POSE_INPUT, "controller_pose",
                                   "Controller Pose", subactions);
  test.primary_action = create_action(test.action_set, XR_ACTION_TYPE_BOOLEAN_INPUT,
                                      "primary_button", "Primary Button", subactions);
  test.trigger_action = create_action(test.action_set, XR_ACTION_TYPE_FLOAT_INPUT, "trigger",
                                      "Trigger", subactions);
  test.thumbstick_action = create_action(test.action_set, XR_ACTION_TYPE_VECTOR2F_INPUT,
                                         "thumbstick", "Thumbstick", subactions);

  auto path = [instance](const char* value) {
    XrPath result{XR_NULL_PATH};
    check(xrStringToPath(instance, value, &result), "xrStringToPath(binding)");
    return result;
  };
  const std::array<XrActionSuggestedBinding, 8> bindings = {{
      {.action = test.pose_action, .binding = path("/user/hand/left/input/grip/pose")},
      {.action = test.pose_action, .binding = path("/user/hand/right/input/grip/pose")},
      {.action = test.primary_action, .binding = path("/user/hand/left/input/x/click")},
      {.action = test.primary_action, .binding = path("/user/hand/right/input/a/click")},
      {.action = test.trigger_action, .binding = path("/user/hand/left/input/trigger/value")},
      {.action = test.trigger_action, .binding = path("/user/hand/right/input/trigger/value")},
      {.action = test.thumbstick_action, .binding = path("/user/hand/left/input/thumbstick")},
      {.action = test.thumbstick_action, .binding = path("/user/hand/right/input/thumbstick")},
  }};
  XrPath profile{XR_NULL_PATH};
  check(xrStringToPath(instance, "/interaction_profiles/meta/touch_plus_controller", &profile),
        "xrStringToPath(touch_plus_controller)");
  const XrInteractionProfileSuggestedBinding suggestion{
      .type = XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING,
      .next = nullptr,
      .interactionProfile = profile,
      .countSuggestedBindings = static_cast<std::uint32_t>(bindings.size()),
      .suggestedBindings = bindings.data(),
  };
  check(xrSuggestInteractionProfileBindings(instance, &suggestion),
        "xrSuggestInteractionProfileBindings(touch_plus_controller)");
  std::cout << "input_profile=/interaction_profiles/meta/touch_plus_controller\n";
  return test;
}

void attach_input_test(XrSession session, InputTest& test) {
  if (!test.enabled) {
    return;
  }
  const XrSessionActionSetsAttachInfo attach{
      .type = XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO,
      .next = nullptr,
      .countActionSets = 1,
      .actionSets = &test.action_set,
  };
  check(xrAttachSessionActionSets(session, &attach), "xrAttachSessionActionSets");
  const XrActionSpaceCreateInfo space_info{
      .type = XR_TYPE_ACTION_SPACE_CREATE_INFO,
      .next = nullptr,
      .action = test.pose_action,
      .subactionPath = test.left_path,
      .poseInActionSpace = identity_pose(),
  };
  check(xrCreateActionSpace(session, &space_info, &test.left_action_space),
        "xrCreateActionSpace(left)");
}

[[nodiscard]] bool near(float actual, float expected) {
  return std::abs(actual - expected) < 0.001F;
}

void verify_input_state(XrSession session, XrSpace local_space, XrTime time,
                        const std::vector<XrView>& views, InputTest& test) {
  if (!test.enabled) {
    return;
  }
  if (views.size() >= 2 && near((views[0].pose.position.x + views[1].pose.position.x) * 0.5F, 1.0F) &&
      near(views[0].pose.position.y, 2.0F) && near(views[0].pose.position.z, 3.0F)) {
    test.views_verified = true;
  }

  const XrActiveActionSet active{.actionSet = test.action_set, .subactionPath = XR_NULL_PATH};
  const XrActionsSyncInfo sync{.type = XR_TYPE_ACTIONS_SYNC_INFO,
                               .next = nullptr,
                               .countActiveActionSets = 1,
                               .activeActionSets = &active};
  if (XR_SUCCEEDED(xrSyncActions(session, &sync))) {
    const XrActionStateGetInfo primary_info{.type = XR_TYPE_ACTION_STATE_GET_INFO,
                                            .next = nullptr,
                                            .action = test.primary_action,
                                            .subactionPath = test.left_path};
    XrActionStateBoolean primary{.type = XR_TYPE_ACTION_STATE_BOOLEAN, .next = nullptr};
    const XrActionStateGetInfo trigger_info{.type = XR_TYPE_ACTION_STATE_GET_INFO,
                                            .next = nullptr,
                                            .action = test.trigger_action,
                                            .subactionPath = test.left_path};
    XrActionStateFloat trigger{.type = XR_TYPE_ACTION_STATE_FLOAT, .next = nullptr};
    const XrActionStateGetInfo stick_info{.type = XR_TYPE_ACTION_STATE_GET_INFO,
                                          .next = nullptr,
                                          .action = test.thumbstick_action,
                                          .subactionPath = test.left_path};
    XrActionStateVector2f stick{.type = XR_TYPE_ACTION_STATE_VECTOR2F, .next = nullptr};
    if (XR_SUCCEEDED(xrGetActionStateBoolean(session, &primary_info, &primary)) &&
        XR_SUCCEEDED(xrGetActionStateFloat(session, &trigger_info, &trigger)) &&
        XR_SUCCEEDED(xrGetActionStateVector2f(session, &stick_info, &stick)) &&
        primary.isActive && primary.currentState && near(trigger.currentState, 0.75F) &&
        near(stick.currentState.x, 0.25F) && near(stick.currentState.y, -0.5F)) {
      test.actions_verified = true;
    }
  }

  XrSpaceLocation location{.type = XR_TYPE_SPACE_LOCATION, .next = nullptr};
  if (XR_SUCCEEDED(xrLocateSpace(test.left_action_space, local_space, time, &location)) &&
      (location.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT) != 0 &&
      near(location.pose.position.x, -0.25F) && near(location.pose.position.y, 1.25F) &&
      near(location.pose.position.z, -0.5F)) {
    test.space_verified = true;
  }
}

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
  const bool verify_input = std::getenv("MAQUESTLINK_VERIFY_INPUT") != nullptr;

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
  instance_info.applicationInfo.apiVersion = verify_input ? XR_MAKE_VERSION(1, 1, 0)
                                                          : XR_MAKE_VERSION(1, 0, 0);
  instance_info.enabledExtensionCount = 1;
  instance_info.enabledExtensionNames = enabled_extensions;

  XrInstance instance{XR_NULL_HANDLE};
  check(xrCreateInstance(&instance_info, &instance), "xrCreateInstance");
  InputTest input_test = create_input_test(instance, verify_input);

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
  attach_input_test(session, input_test);
  if (verify_input) {
    // The Simulator can run unpaced in batch E2E. Give the retrying mock client time to
    // connect before the finite frame loop consumes every frame.
    std::this_thread::sleep_for(std::chrono::milliseconds(500));
  }

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
    verify_input_state(session, local_space, frame_state.predictedDisplayTime, views, input_test);

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

  if (verify_input &&
      (!input_test.views_verified || !input_test.actions_verified || !input_test.space_verified)) {
    throw std::runtime_error("synthetic input was not observed through all OpenXR APIs");
  }
  if (verify_input) {
    std::cout << "MAQUESTLINK_INPUT_E2E_OK views=1 actions=1 space=1\n";
  }
  if (input_test.left_action_space != XR_NULL_HANDLE) {
    check(xrDestroySpace(input_test.left_action_space), "xrDestroySpace(action)");
  }
  check(xrDestroySpace(local_space), "xrDestroySpace");
  check(xrDestroySwapchain(swapchain), "xrDestroySwapchain");
  check(xrDestroySession(session), "xrDestroySession");
  if (input_test.action_set != XR_NULL_HANDLE) {
    check(xrDestroyActionSet(input_test.action_set), "xrDestroyActionSet");
  }
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
