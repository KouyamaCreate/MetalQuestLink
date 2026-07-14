#pragma once

#include <openxr/openxr.h>

void input_register_instance(XrInstance instance, PFN_xrGetInstanceProcAddr gipa);
void input_unregister_instance(XrInstance instance);
void input_register_session(XrSession session, XrInstance instance);
void input_unregister_session(XrSession session);
[[nodiscard]] bool input_get_proc_addr(const char* name, PFN_xrVoidFunction* function);
