#pragma once

#include <openxr/openxr.h>

void streaming_register_instance(XrInstance instance, PFN_xrGetInstanceProcAddr gipa);
void streaming_unregister_instance(XrInstance instance);
[[nodiscard]] bool streaming_get_proc_addr(const char* name, PFN_xrVoidFunction* function);
