#include <openxr/openxr.h>
#include <openxr/openxr_loader_negotiation.h>

#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iostream>
#include <map>
#include <mutex>
#include <string>

#include "input_injection.hpp"
#include "streaming.hpp"

namespace {

constexpr const char* kLayerName = "XR_APILAYER_METALQUESTLINK_streaming";

struct InstanceDispatch {
  PFN_xrGetInstanceProcAddr get_instance_proc_addr{};
};

std::mutex g_mutex;
std::map<XrInstance, InstanceDispatch> g_instances;

void log_line(const std::string& message) {
  std::scoped_lock lock(g_mutex);
  std::cerr << "[MetalQuestLink layer] " << message << '\n';
  if (const char* path = std::getenv("METALQUESTLINK_LAYER_LOG"); path != nullptr && path[0] != '\0') {
    std::ofstream stream(path, std::ios::app);
    stream << message << '\n';
  }
}

[[nodiscard]] PFN_xrGetInstanceProcAddr next_gipa(XrInstance instance) {
  std::scoped_lock lock(g_mutex);
  const auto it = g_instances.find(instance);
  return it == g_instances.end() ? nullptr : it->second.get_instance_proc_addr;
}

XRAPI_ATTR XrResult XRAPI_CALL layer_create_instance(const XrInstanceCreateInfo*, XrInstance*) {
  return XR_ERROR_FUNCTION_UNSUPPORTED;
}

XRAPI_ATTR XrResult XRAPI_CALL layer_destroy_instance(XrInstance instance) {
  const auto gipa = next_gipa(instance);
  if (gipa == nullptr) {
    return XR_ERROR_HANDLE_INVALID;
  }

  PFN_xrVoidFunction function{};
  XrResult result = gipa(instance, "xrDestroyInstance", &function);
  if (XR_SUCCEEDED(result) && function != nullptr) {
    result = reinterpret_cast<PFN_xrDestroyInstance>(function)(instance);
  }
  if (XR_SUCCEEDED(result)) {
    streaming_unregister_instance(instance);
    input_unregister_instance(instance);
    std::scoped_lock lock(g_mutex);
    g_instances.erase(instance);
  }
  log_line("destroyed instance");
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL layer_get_instance_proc_addr(XrInstance instance, const char* name,
                                                             PFN_xrVoidFunction* function) {
  if (name == nullptr || function == nullptr) {
    return XR_ERROR_VALIDATION_FAILURE;
  }
  if (std::strcmp(name, "xrGetInstanceProcAddr") == 0) {
    *function = reinterpret_cast<PFN_xrVoidFunction>(layer_get_instance_proc_addr);
    return XR_SUCCESS;
  }
  if (std::strcmp(name, "xrCreateInstance") == 0) {
    *function = reinterpret_cast<PFN_xrVoidFunction>(layer_create_instance);
    return XR_SUCCESS;
  }
  if (std::strcmp(name, "xrDestroyInstance") == 0) {
    *function = reinterpret_cast<PFN_xrVoidFunction>(layer_destroy_instance);
    return XR_SUCCESS;
  }

  if (streaming_get_proc_addr(name, function)) {
    return XR_SUCCESS;
  }
  if (input_get_proc_addr(name, function)) {
    return XR_SUCCESS;
  }

  const auto gipa = next_gipa(instance);
  if (gipa == nullptr) {
    *function = nullptr;
    return XR_ERROR_HANDLE_INVALID;
  }
  return gipa(instance, name, function);
}

XRAPI_ATTR XrResult XRAPI_CALL layer_create_api_layer_instance(
    const XrInstanceCreateInfo* info, const XrApiLayerCreateInfo* layer_info, XrInstance* instance) {
  if (info == nullptr || layer_info == nullptr || instance == nullptr || layer_info->nextInfo == nullptr ||
      layer_info->nextInfo->nextCreateApiLayerInstance == nullptr ||
      layer_info->nextInfo->nextGetInstanceProcAddr == nullptr) {
    return XR_ERROR_INITIALIZATION_FAILED;
  }

  XrApiLayerCreateInfo next_layer_info = *layer_info;
  next_layer_info.nextInfo = layer_info->nextInfo->next;
  const XrResult result = layer_info->nextInfo->nextCreateApiLayerInstance(info, &next_layer_info, instance);
  if (XR_FAILED(result)) {
    return result;
  }

  {
    std::scoped_lock lock(g_mutex);
    g_instances.emplace(*instance, InstanceDispatch{layer_info->nextInfo->nextGetInstanceProcAddr});
  }
  streaming_register_instance(*instance, layer_info->nextInfo->nextGetInstanceProcAddr);
  input_register_instance(*instance, layer_info->nextInfo->nextGetInstanceProcAddr);
  log_line(std::string("loaded instance for ") + info->applicationInfo.applicationName);
  return XR_SUCCESS;
}

}  // namespace

extern "C" __attribute__((visibility("default"))) XRAPI_ATTR XrResult XRAPI_CALL
xrNegotiateLoaderApiLayerInterface(const XrNegotiateLoaderInfo* loader_info, const char* layer_name,
                                   XrNegotiateApiLayerRequest* layer_request) {
  if (loader_info == nullptr || layer_name == nullptr || layer_request == nullptr ||
      std::strcmp(layer_name, kLayerName) != 0 ||
      loader_info->structType != XR_LOADER_INTERFACE_STRUCT_LOADER_INFO ||
      loader_info->structVersion != XR_LOADER_INFO_STRUCT_VERSION ||
      loader_info->structSize != sizeof(XrNegotiateLoaderInfo) ||
      layer_request->structType != XR_LOADER_INTERFACE_STRUCT_API_LAYER_REQUEST ||
      layer_request->structVersion != XR_API_LAYER_INFO_STRUCT_VERSION ||
      layer_request->structSize != sizeof(XrNegotiateApiLayerRequest) ||
      loader_info->minInterfaceVersion > XR_CURRENT_LOADER_API_LAYER_VERSION ||
      loader_info->maxInterfaceVersion < XR_CURRENT_LOADER_API_LAYER_VERSION ||
      loader_info->maxApiVersion < XR_MAKE_VERSION(1, 0, 0)) {
    return XR_ERROR_INITIALIZATION_FAILED;
  }

  layer_request->layerInterfaceVersion = XR_CURRENT_LOADER_API_LAYER_VERSION;
  layer_request->layerApiVersion = XR_MAKE_VERSION(1, 0, 0);
  layer_request->getInstanceProcAddr = layer_get_instance_proc_addr;
  layer_request->createApiLayerInstance = layer_create_api_layer_instance;
  log_line("negotiated loader interface");
  return XR_SUCCESS;
}
