#include <jni.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>

#include <android/log.h>

#include <array>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <vector>

namespace {

constexpr const char* kLogTag = "MaQuestLinkProjection";

struct StreamView {
  XrPosef pose{};
  XrFovf fov{};
};

struct ProjectionState {
  XrInstance instance{XR_NULL_HANDLE};
  XrSession session{XR_NULL_HANDLE};
  XrSpace app_space{XR_NULL_HANDLE};
  XrSwapchain swapchain{XR_NULL_HANDLE};
  jobject surface{};
  std::uint32_t width{};
  std::uint32_t height{};
  std::array<StreamView, 2> views{};
  bool frame_ready{};
  bool passthrough{};
};

std::mutex g_mutex;
ProjectionState g_state;
PFN_xrGetInstanceProcAddr g_next_gipa{};
PFN_xrEndFrame g_next_end_frame{};
PFN_xrDestroySwapchain g_destroy_swapchain{};

void log_line(const char* message) {
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "%s", message);
}

XrResult XRAPI_PTR hook_end_frame(XrSession session, const XrFrameEndInfo* info) {
  PFN_xrEndFrame next{};
  ProjectionState state;
  {
    std::scoped_lock lock(g_mutex);
    next = g_next_end_frame;
    state = g_state;
  }
  if (next == nullptr) return XR_ERROR_FUNCTION_UNSUPPORTED;
  if (info == nullptr || session != state.session || state.swapchain == XR_NULL_HANDLE ||
      state.app_space == XR_NULL_HANDLE || !state.frame_ready || state.width < 2 ||
      state.height < 2) {
    return next(session, info);
  }

  const std::int32_t eye_width = static_cast<std::int32_t>(state.width / 2);
  const std::int32_t height = static_cast<std::int32_t>(state.height);
  std::array<XrCompositionLayerProjectionView, 2> views{};
  for (std::size_t eye = 0; eye < views.size(); ++eye) {
    views[eye] = XrCompositionLayerProjectionView{
        .type = XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW,
        .next = nullptr,
        .pose = state.views[eye].pose,
        .fov = state.views[eye].fov,
        .subImage =
            XrSwapchainSubImage{
                .swapchain = state.swapchain,
                .imageRect =
                    XrRect2Di{
                        .offset = {static_cast<std::int32_t>(eye) * eye_width, 0},
                        .extent = {eye_width, height},
                    },
                .imageArrayIndex = 0,
            },
    };
  }

  const XrCompositionLayerColorScaleBiasKHR color_scale{
      .type = XR_TYPE_COMPOSITION_LAYER_COLOR_SCALE_BIAS_KHR,
      .next = nullptr,
      .colorScale = {1.0F, 1.0F, 1.0F, state.passthrough ? 0.82F : 1.0F},
      .colorBias = {0.0F, 0.0F, 0.0F, 0.0F},
  };
  const XrCompositionLayerProjection projection{
      .type = XR_TYPE_COMPOSITION_LAYER_PROJECTION,
      .next = state.passthrough ? &color_scale : nullptr,
      .layerFlags = state.passthrough ? XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT : 0,
      .space = state.app_space,
      .viewCount = static_cast<std::uint32_t>(views.size()),
      .views = views.data(),
  };

  std::vector<const XrCompositionLayerBaseHeader*> layers;
  layers.reserve(info->layerCount + 1);
  bool replaced_projection = false;
  for (std::uint32_t index = 0; index < info->layerCount; ++index) {
    const auto* layer = info->layers[index];
    if (!replaced_projection && layer != nullptr &&
        layer->type == XR_TYPE_COMPOSITION_LAYER_PROJECTION) {
      layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection));
      replaced_projection = true;
    } else {
      layers.push_back(layer);
    }
  }
  if (!replaced_projection) {
    layers.push_back(reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection));
  }
  XrFrameEndInfo modified = *info;
  modified.layerCount = static_cast<std::uint32_t>(layers.size());
  modified.layers = layers.data();
  return next(session, &modified);
}

XrResult XRAPI_PTR hook_get_instance_proc_addr(XrInstance instance, const char* name,
                                               PFN_xrVoidFunction* function) {
  if (g_next_gipa == nullptr) return XR_ERROR_INITIALIZATION_FAILED;
  const XrResult result = g_next_gipa(instance, name, function);
  if (XR_SUCCEEDED(result) && function != nullptr && name != nullptr &&
      std::strcmp(name, "xrEndFrame") == 0) {
    std::scoped_lock lock(g_mutex);
    if (*function != reinterpret_cast<PFN_xrVoidFunction>(hook_end_frame)) {
      g_next_end_frame = reinterpret_cast<PFN_xrEndFrame>(*function);
    }
    *function = reinterpret_cast<PFN_xrVoidFunction>(hook_end_frame);
  }
  return result;
}

template <typename Function>
Function load_function(XrInstance instance, const char* name) {
  PFN_xrVoidFunction function{};
  if (g_next_gipa == nullptr || XR_FAILED(g_next_gipa(instance, name, &function))) return nullptr;
  return reinterpret_cast<Function>(function);
}

void destroy_surface_swapchain_locked() {
  if (g_state.swapchain != XR_NULL_HANDLE && g_destroy_swapchain != nullptr) {
    (void)g_destroy_swapchain(g_state.swapchain);
  }
  g_state.swapchain = XR_NULL_HANDLE;
  g_state.surface = nullptr;
  g_state.width = 0;
  g_state.height = 0;
  g_state.frame_ready = false;
}

}  // namespace

extern "C" __attribute__((visibility("default"))) PFN_xrGetInstanceProcAddr
maquestlink_projection_hook_get_instance_proc_addr(PFN_xrGetInstanceProcAddr old) {
  std::scoped_lock lock(g_mutex);
  g_next_gipa = old;
  log_line("installed xrGetInstanceProcAddr hook");
  return hook_get_instance_proc_addr;
}

extern "C" __attribute__((visibility("default"))) void maquestlink_projection_set_instance(
    XrInstance instance) {
  std::scoped_lock lock(g_mutex);
  g_state.instance = instance;
  g_destroy_swapchain = load_function<PFN_xrDestroySwapchain>(instance, "xrDestroySwapchain");
  log_line("received OpenXR instance");
}

extern "C" __attribute__((visibility("default"))) void maquestlink_projection_set_session(
    XrSession session) {
  std::scoped_lock lock(g_mutex);
  g_state.session = session;
  log_line("received OpenXR session");
}

extern "C" __attribute__((visibility("default"))) void maquestlink_projection_set_app_space(
    XrSpace space) {
  std::scoped_lock lock(g_mutex);
  g_state.app_space = space;
}

extern "C" __attribute__((visibility("default"))) XrResult
maquestlink_projection_create_surface(std::uint32_t width, std::uint32_t height, jobject* surface) {
  if (surface == nullptr || width < 2 || height < 2 || (width % 2) != 0) {
    return XR_ERROR_VALIDATION_FAILURE;
  }
  std::scoped_lock lock(g_mutex);
  if (g_state.instance == XR_NULL_HANDLE || g_state.session == XR_NULL_HANDLE) {
    return XR_ERROR_SESSION_NOT_READY;
  }
  if (g_state.swapchain != XR_NULL_HANDLE && g_state.width == width && g_state.height == height) {
    *surface = g_state.surface;
    return XR_SUCCESS;
  }
  destroy_surface_swapchain_locked();
  const auto create_surface = load_function<PFN_xrCreateSwapchainAndroidSurfaceKHR>(
      g_state.instance, "xrCreateSwapchainAndroidSurfaceKHR");
  if (create_surface == nullptr) return XR_ERROR_FUNCTION_UNSUPPORTED;
  const XrSwapchainCreateInfo create_info{
      .type = XR_TYPE_SWAPCHAIN_CREATE_INFO,
      .next = nullptr,
      .createFlags = 0,
      .usageFlags = XR_SWAPCHAIN_USAGE_SAMPLED_BIT,
      .format = 0,
      .sampleCount = 0,
      .width = width,
      .height = height,
      .faceCount = 0,
      .arraySize = 0,
      .mipCount = 0,
  };
  XrSwapchain swapchain{XR_NULL_HANDLE};
  jobject created_surface{};
  const XrResult result =
      create_surface(g_state.session, &create_info, &swapchain, &created_surface);
  if (XR_FAILED(result)) return result;
  g_state.swapchain = swapchain;
  g_state.surface = created_surface;
  g_state.width = width;
  g_state.height = height;
  *surface = created_surface;
  log_line("created immersive Android Surface projection swapchain");
  return XR_SUCCESS;
}

extern "C" __attribute__((visibility("default"))) void maquestlink_projection_update_frame(
    const StreamView* views, std::uint32_t view_count, bool passthrough) {
  if (views == nullptr || view_count != 2) return;
  std::scoped_lock lock(g_mutex);
  g_state.views[0] = views[0];
  g_state.views[1] = views[1];
  g_state.passthrough = passthrough;
  g_state.frame_ready = true;
}

extern "C" __attribute__((visibility("default"))) void maquestlink_projection_reset_session() {
  std::scoped_lock lock(g_mutex);
  destroy_surface_swapchain_locked();
  g_state.session = XR_NULL_HANDLE;
  g_state.app_space = XR_NULL_HANDLE;
}
