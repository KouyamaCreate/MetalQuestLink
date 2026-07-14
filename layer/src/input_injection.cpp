#include "input_injection.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iterator>
#include <map>
#include <mutex>
#include <string>
#include <tuple>
#include <utility>
#include <vector>

#include "maquestlink/protocol.hpp"
#include "transport.hpp"

namespace {

namespace protocol = maquestlink::protocol;

enum class Side { None, Left, Right };
enum class Component {
  None,
  Primary,
  Secondary,
  ThumbstickClick,
  Menu,
  PrimaryTouch,
  SecondaryTouch,
  ThumbstickTouch,
  TriggerTouch,
  Trigger,
  Grip,
  Thumbstick,
  Pose,
  Haptic,
};
enum class SpaceSource { Local, Head, LeftController, RightController };

struct InstanceData {
  PFN_xrGetInstanceProcAddr gipa{};
  XrPath left_path{XR_NULL_PATH};
  XrPath right_path{XR_NULL_PATH};
};

struct ActionSetData {
  XrInstance instance{XR_NULL_HANDLE};
};

struct Binding {
  Side side{Side::None};
  Component component{Component::None};
};

struct ActionData {
  XrInstance instance{XR_NULL_HANDLE};
  XrActionSet action_set{XR_NULL_HANDLE};
  XrActionType type{XR_ACTION_TYPE_BOOLEAN_INPUT};
  std::vector<XrPath> subaction_paths;
  std::vector<Binding> bindings;
};

struct SpaceData {
  XrInstance instance{XR_NULL_HANDLE};
  XrSession session{XR_NULL_HANDLE};
  SpaceSource source{SpaceSource::Local};
  XrPosef offset{.orientation = {.x = 0, .y = 0, .z = 0, .w = 1},
                 .position = {.x = 0, .y = 0, .z = 0}};
};

struct HandTrackerData {
  XrInstance instance{XR_NULL_HANDLE};
  XrSession session{XR_NULL_HANDLE};
  protocol::HandSide side{protocol::HandSide::Left};
};

std::mutex g_mutex;
std::map<XrInstance, InstanceData> g_instances;
std::map<XrSession, XrInstance> g_sessions;
std::map<XrActionSet, ActionSetData> g_action_sets;
std::map<XrAction, ActionData> g_actions;
std::map<XrSpace, SpaceData> g_spaces;
std::map<XrHandTrackerEXT, HandTrackerData> g_hand_trackers;
std::atomic<std::uint64_t> g_next_haptic_sequence{1};
std::atomic<std::uintptr_t> g_next_hand_tracker{1};
using StateKey = std::tuple<XrSession, XrAction, XrPath>;
std::map<StateKey, XrBool32> g_boolean_states;
std::map<StateKey, float> g_float_states;
std::map<StateKey, std::array<float, 2>> g_vector_states;

template <typename Function>
[[nodiscard]] Function next_function(XrInstance instance, const char* name) {
  PFN_xrGetInstanceProcAddr gipa{};
  {
    std::scoped_lock lock(g_mutex);
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

[[nodiscard]] XrInstance instance_for_session(XrSession session) {
  std::scoped_lock lock(g_mutex);
  const auto found = g_sessions.find(session);
  return found == g_sessions.end() ? XR_NULL_HANDLE : found->second;
}

[[nodiscard]] Side side_for_path(XrInstance instance, XrPath path) {
  if (path == XR_NULL_PATH) {
    return Side::None;
  }
  std::scoped_lock lock(g_mutex);
  const auto found = g_instances.find(instance);
  if (found == g_instances.end()) {
    return Side::None;
  }
  if (path == found->second.left_path) {
    return Side::Left;
  }
  if (path == found->second.right_path) {
    return Side::Right;
  }
  return Side::None;
}

[[nodiscard]] std::string path_string(XrInstance instance, XrPath path) {
  const auto function = next_function<PFN_xrPathToString>(instance, "xrPathToString");
  if (function == nullptr) {
    return {};
  }
  std::uint32_t count{};
  if (XR_FAILED(function(instance, path, 0, &count, nullptr)) || count == 0) {
    return {};
  }
  std::string result(count, '\0');
  if (XR_FAILED(function(instance, path, count, &count, result.data()))) {
    return {};
  }
  result.resize(std::strlen(result.c_str()));
  return result;
}

[[nodiscard]] Side side_from_binding_path(const std::string& path) {
  if (path.starts_with("/user/hand/left/")) {
    return Side::Left;
  }
  if (path.starts_with("/user/hand/right/")) {
    return Side::Right;
  }
  return Side::None;
}

[[nodiscard]] Component component_from_binding_path(const std::string& path) {
  if (path.ends_with("/input/x/click") || path.ends_with("/input/a/click")) {
    return Component::Primary;
  }
  if (path.ends_with("/input/y/click") || path.ends_with("/input/b/click")) {
    return Component::Secondary;
  }
  if (path.ends_with("/input/thumbstick/click")) {
    return Component::ThumbstickClick;
  }
  if (path.ends_with("/input/menu/click")) {
    return Component::Menu;
  }
  if (path.ends_with("/input/system/click")) {
    return Component::Menu;
  }
  if (path.ends_with("/input/x/touch") || path.ends_with("/input/a/touch")) {
    return Component::PrimaryTouch;
  }
  if (path.ends_with("/input/y/touch") || path.ends_with("/input/b/touch")) {
    return Component::SecondaryTouch;
  }
  if (path.ends_with("/input/thumbstick/touch")) {
    return Component::ThumbstickTouch;
  }
  if (path.ends_with("/input/trigger/touch")) {
    return Component::TriggerTouch;
  }
  if (path.ends_with("/input/trigger/value")) {
    return Component::Trigger;
  }
  if (path.ends_with("/input/squeeze/value")) {
    return Component::Grip;
  }
  if (path.ends_with("/input/thumbstick")) {
    return Component::Thumbstick;
  }
  if (path.ends_with("/input/grip/pose") || path.ends_with("/input/aim/pose")) {
    return Component::Pose;
  }
  if (path.ends_with("/output/haptic")) {
    return Component::Haptic;
  }
  return Component::None;
}

[[nodiscard]] XrQuaternionf multiply(XrQuaternionf left, XrQuaternionf right) {
  return {
      .x = left.w * right.x + left.x * right.w + left.y * right.z - left.z * right.y,
      .y = left.w * right.y - left.x * right.z + left.y * right.w + left.z * right.x,
      .z = left.w * right.z + left.x * right.y - left.y * right.x + left.z * right.w,
      .w = left.w * right.w - left.x * right.x - left.y * right.y - left.z * right.z,
  };
}

[[nodiscard]] XrVector3f rotate(XrQuaternionf rotation, XrVector3f value) {
  const XrQuaternionf vector{.x = value.x, .y = value.y, .z = value.z, .w = 0};
  const XrQuaternionf inverse{.x = -rotation.x, .y = -rotation.y, .z = -rotation.z,
                              .w = rotation.w};
  const XrQuaternionf result = multiply(multiply(rotation, vector), inverse);
  return {.x = result.x, .y = result.y, .z = result.z};
}

[[nodiscard]] XrPosef compose(XrPosef left, XrPosef right) {
  const XrVector3f translated = rotate(left.orientation, right.position);
  return {
      .orientation = multiply(left.orientation, right.orientation),
      .position = {.x = left.position.x + translated.x,
                   .y = left.position.y + translated.y,
                   .z = left.position.z + translated.z},
  };
}

[[nodiscard]] XrPosef inverse(XrPosef pose) {
  XrPosef result;
  result.orientation = {.x = -pose.orientation.x,
                        .y = -pose.orientation.y,
                        .z = -pose.orientation.z,
                        .w = pose.orientation.w};
  const XrVector3f negative{.x = -pose.position.x, .y = -pose.position.y,
                            .z = -pose.position.z};
  result.position = rotate(result.orientation, negative);
  return result;
}

[[nodiscard]] XrPosef xr_pose(const protocol::Pose& pose) {
  return {
      .orientation = {.x = pose.orientation[0],
                      .y = pose.orientation[1],
                      .z = pose.orientation[2],
                      .w = pose.orientation[3]},
      .position = {.x = pose.position[0], .y = pose.position[1], .z = pose.position[2]},
  };
}

[[nodiscard]] XrSpaceLocationFlags location_flags(const protocol::Pose& pose) {
  XrSpaceLocationFlags flags{};
  if ((pose.flags & protocol::PositionValid) != 0) flags |= XR_SPACE_LOCATION_POSITION_VALID_BIT;
  if ((pose.flags & protocol::OrientationValid) != 0) flags |= XR_SPACE_LOCATION_ORIENTATION_VALID_BIT;
  if ((pose.flags & protocol::PositionTracked) != 0) flags |= XR_SPACE_LOCATION_POSITION_TRACKED_BIT;
  if ((pose.flags & protocol::OrientationTracked) != 0) flags |= XR_SPACE_LOCATION_ORIENTATION_TRACKED_BIT;
  return flags;
}

[[nodiscard]] const protocol::ControllerState& controller_for_side(
    const protocol::PoseInput& input, Side side) {
  return side == Side::Right ? input.right : input.left;
}

[[nodiscard]] const protocol::Pose* pose_for_source(const protocol::PoseInput& input,
                                                     SpaceSource source) {
  switch (source) {
    case SpaceSource::Local:
      return nullptr;
    case SpaceSource::Head:
      return &input.head;
    case SpaceSource::LeftController:
      return &input.left.pose;
    case SpaceSource::RightController:
      return &input.right.pose;
  }
  return nullptr;
}

[[nodiscard]] XrPosef local_pose(const protocol::PoseInput& input, const SpaceData& space) {
  const protocol::Pose* source = pose_for_source(input, space.source);
  const XrPosef origin = source == nullptr
                             ? XrPosef{.orientation = {.x = 0, .y = 0, .z = 0, .w = 1},
                                       .position = {.x = 0, .y = 0, .z = 0}}
                             : xr_pose(*source);
  return compose(origin, space.offset);
}

[[nodiscard]] std::vector<Binding> bindings_for_action(XrAction action, XrPath subaction_path) {
  std::scoped_lock lock(g_mutex);
  const auto found = g_actions.find(action);
  if (found == g_actions.end()) {
    return {};
  }
  Side requested = Side::None;
  const auto instance = g_instances.find(found->second.instance);
  if (instance != g_instances.end()) {
    if (subaction_path == instance->second.left_path) requested = Side::Left;
    if (subaction_path == instance->second.right_path) requested = Side::Right;
  }
  std::vector<Binding> result;
  std::copy_if(found->second.bindings.begin(), found->second.bindings.end(),
               std::back_inserter(result), [requested](const Binding& binding) {
                 return requested == Side::None || binding.side == requested;
               });
  return result;
}

[[nodiscard]] bool button_value(const protocol::ControllerState& controller,
                                Component component) {
  std::uint64_t mask{};
  switch (component) {
    case Component::Primary: mask = protocol::PrimaryButton; break;
    case Component::Secondary: mask = protocol::SecondaryButton; break;
    case Component::ThumbstickClick: mask = protocol::ThumbstickButton; break;
    case Component::Menu: mask = protocol::MenuButton; break;
    case Component::PrimaryTouch: mask = protocol::PrimaryTouch; break;
    case Component::SecondaryTouch: mask = protocol::SecondaryTouch; break;
    case Component::ThumbstickTouch: mask = protocol::ThumbstickTouch; break;
    case Component::TriggerTouch: mask = protocol::TriggerTouch; break;
    case Component::Trigger: return controller.trigger > 0.5F;
    case Component::Grip: return controller.grip > 0.5F;
    default: return false;
  }
  return (controller.buttons & mask) != 0;
}

[[nodiscard]] bool injected_boolean(const protocol::PoseInput& input,
                                    const std::vector<Binding>& bindings) {
  return std::any_of(bindings.begin(), bindings.end(), [&input](const Binding& binding) {
    return button_value(controller_for_side(input, binding.side), binding.component);
  });
}

[[nodiscard]] float injected_float(const protocol::PoseInput& input,
                                   const std::vector<Binding>& bindings) {
  for (const Binding& binding : bindings) {
    const auto& controller = controller_for_side(input, binding.side);
    if (binding.component == Component::Trigger) return controller.trigger;
    if (binding.component == Component::Grip) return controller.grip;
    if (button_value(controller, binding.component)) return 1.0F;
  }
  return 0.0F;
}

[[nodiscard]] std::uint64_t monotonic_now_ns() {
  return static_cast<std::uint64_t>(
      std::chrono::duration_cast<std::chrono::nanoseconds>(
          std::chrono::steady_clock::now().time_since_epoch())
          .count());
}

[[nodiscard]] std::vector<Side> haptic_sides(XrInstance instance,
                                              const XrHapticActionInfo* info) {
  if (info == nullptr) return {};
  const Side requested = side_for_path(instance, info->subactionPath);
  if (requested != Side::None) return {requested};
  std::vector<Side> result;
  for (const auto& binding : bindings_for_action(info->action, XR_NULL_PATH)) {
    if (binding.component == Component::Haptic && binding.side != Side::None &&
        std::find(result.begin(), result.end(), binding.side) == result.end()) {
      result.push_back(binding.side);
    }
  }
  return result;
}

void send_haptic(const std::vector<Side>& sides, protocol::HapticAction action,
                 float amplitude, float frequency_hz, std::uint64_t duration_ns) {
  for (const Side side : sides) {
    transport_send(protocol::Message{
        .sequence = g_next_haptic_sequence.fetch_add(1),
        .payload = protocol::HapticCommand{
            .timestamp_ns = monotonic_now_ns(),
            .side = side == Side::Right ? protocol::HandSide::Right
                                       : protocol::HandSide::Left,
            .action = action,
            .amplitude = amplitude,
            .frequency_hz = frequency_hz,
            .duration_ns = duration_ns,
        },
    });
  }
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_action_set(XrInstance instance,
                                                       const XrActionSetCreateInfo* info,
                                                       XrActionSet* action_set) {
  const auto next = next_function<PFN_xrCreateActionSet>(instance, "xrCreateActionSet");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(instance, info, action_set);
  if (XR_SUCCEEDED(result)) {
    std::scoped_lock lock(g_mutex);
    g_action_sets[*action_set] = ActionSetData{instance};
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_destroy_action_set(XrActionSet action_set) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_mutex);
    const auto found = g_action_sets.find(action_set);
    if (found == g_action_sets.end()) return XR_ERROR_HANDLE_INVALID;
    instance = found->second.instance;
  }
  const auto next = next_function<PFN_xrDestroyActionSet>(instance, "xrDestroyActionSet");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(action_set);
  if (XR_SUCCEEDED(result)) {
    std::scoped_lock lock(g_mutex);
    for (auto action = g_actions.begin(); action != g_actions.end();) {
      action = action->second.action_set == action_set ? g_actions.erase(action) : std::next(action);
    }
    g_action_sets.erase(action_set);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_action(XrActionSet action_set,
                                                   const XrActionCreateInfo* info,
                                                   XrAction* action) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_mutex);
    const auto found = g_action_sets.find(action_set);
    if (found == g_action_sets.end()) return XR_ERROR_HANDLE_INVALID;
    instance = found->second.instance;
  }
  const auto next = next_function<PFN_xrCreateAction>(instance, "xrCreateAction");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(action_set, info, action);
  if (XR_SUCCEEDED(result)) {
    ActionData data{.instance = instance, .action_set = action_set, .type = info->actionType};
    if (info->countSubactionPaths > 0 && info->subactionPaths != nullptr) {
      data.subaction_paths.assign(info->subactionPaths,
                                  info->subactionPaths + info->countSubactionPaths);
    }
    std::scoped_lock lock(g_mutex);
    g_actions[*action] = std::move(data);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_destroy_action(XrAction action) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_mutex);
    const auto found = g_actions.find(action);
    if (found == g_actions.end()) return XR_ERROR_HANDLE_INVALID;
    instance = found->second.instance;
  }
  const auto next = next_function<PFN_xrDestroyAction>(instance, "xrDestroyAction");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(action);
  if (XR_SUCCEEDED(result)) {
    std::scoped_lock lock(g_mutex);
    g_actions.erase(action);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_suggest_bindings(
    XrInstance instance, const XrInteractionProfileSuggestedBinding* info) {
  const auto next = next_function<PFN_xrSuggestInteractionProfileBindings>(
      instance, "xrSuggestInteractionProfileBindings");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(instance, info);
  if (XR_FAILED(result) || info == nullptr) {
    return result;
  }
  for (std::uint32_t index = 0; index < info->countSuggestedBindings; ++index) {
    const auto& suggested = info->suggestedBindings[index];
    const std::string path = path_string(instance, suggested.binding);
    const Binding binding{.side = side_from_binding_path(path),
                          .component = component_from_binding_path(path)};
    if (binding.side == Side::None || binding.component == Component::None) {
      continue;
    }
    std::scoped_lock lock(g_mutex);
    const auto action = g_actions.find(suggested.action);
    if (action != g_actions.end() &&
        std::find_if(action->second.bindings.begin(), action->second.bindings.end(),
                     [&binding](const Binding& current) {
                       return current.side == binding.side && current.component == binding.component;
                     }) == action->second.bindings.end()) {
      action->second.bindings.push_back(binding);
    }
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_reference_space(
    XrSession session, const XrReferenceSpaceCreateInfo* info, XrSpace* space) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrCreateReferenceSpace>(instance, "xrCreateReferenceSpace");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, space);
  if (XR_SUCCEEDED(result)) {
    const SpaceSource source = info->referenceSpaceType == XR_REFERENCE_SPACE_TYPE_VIEW
                                   ? SpaceSource::Head
                                   : SpaceSource::Local;
    std::scoped_lock lock(g_mutex);
    g_spaces[*space] = SpaceData{instance, session, source, info->poseInReferenceSpace};
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_action_space(
    XrSession session, const XrActionSpaceCreateInfo* info, XrSpace* space) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrCreateActionSpace>(instance, "xrCreateActionSpace");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, space);
  if (XR_SUCCEEDED(result)) {
    Side side = side_for_path(instance, info->subactionPath);
    if (side == Side::None) {
      const auto bindings = bindings_for_action(info->action, XR_NULL_PATH);
      if (!bindings.empty()) side = bindings.front().side;
    }
    const SpaceSource source = side == Side::Right ? SpaceSource::RightController
                                                    : SpaceSource::LeftController;
    std::scoped_lock lock(g_mutex);
    g_spaces[*space] = SpaceData{instance, session, source, info->poseInActionSpace};
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_destroy_space(XrSpace space) {
  XrInstance instance{};
  {
    std::scoped_lock lock(g_mutex);
    const auto found = g_spaces.find(space);
    if (found == g_spaces.end()) return XR_ERROR_HANDLE_INVALID;
    instance = found->second.instance;
  }
  const auto next = next_function<PFN_xrDestroySpace>(instance, "xrDestroySpace");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(space);
  if (XR_SUCCEEDED(result)) {
    std::scoped_lock lock(g_mutex);
    g_spaces.erase(space);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_locate_space(XrSpace space, XrSpace base_space, XrTime time,
                                                  XrSpaceLocation* location) {
  SpaceData target;
  SpaceData base;
  {
    std::scoped_lock lock(g_mutex);
    const auto target_found = g_spaces.find(space);
    const auto base_found = g_spaces.find(base_space);
    if (target_found == g_spaces.end()) return XR_ERROR_HANDLE_INVALID;
    target = target_found->second;
    if (base_found != g_spaces.end()) base = base_found->second;
  }
  const auto next = next_function<PFN_xrLocateSpace>(target.instance, "xrLocateSpace");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID
                                          : next(space, base_space, time, location);
  const auto input = transport_latest_pose_input();
  if (XR_SUCCEEDED(result) && input.has_value() && location != nullptr) {
    const XrPosef target_local = local_pose(*input, target);
    const XrPosef base_local = local_pose(*input, base);
    location->pose = compose(inverse(base_local), target_local);
    if (const protocol::Pose* pose = pose_for_source(*input, target.source); pose != nullptr) {
      location->locationFlags = location_flags(*pose);
    }
    for (auto* chain = static_cast<XrBaseOutStructure*>(location->next); chain != nullptr;
         chain = chain->next) {
      if (chain->type == XR_TYPE_SPACE_VELOCITY) {
        auto* velocity = reinterpret_cast<XrSpaceVelocity*>(chain);
        velocity->velocityFlags = 0;
        velocity->linearVelocity = {};
        velocity->angularVelocity = {};
      }
    }
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_locate_views(
    XrSession session, const XrViewLocateInfo* info, XrViewState* state,
    std::uint32_t capacity, std::uint32_t* count, XrView* views) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrLocateViews>(instance, "xrLocateViews");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID
                                          : next(session, info, state, capacity, count, views);
  const auto input = transport_latest_pose_input();
  if (XR_SUCCEEDED(result) && input.has_value() && state != nullptr && count != nullptr &&
      views != nullptr && capacity >= 2 && *count >= 2) {
    const XrPosef head = xr_pose(input->head);
    SpaceData base;
    {
      std::scoped_lock lock(g_mutex);
      const auto found = g_spaces.find(info->space);
      if (found != g_spaces.end()) base = found->second;
    }
    const XrPosef inverse_base = inverse(local_pose(*input, base));
    for (std::uint32_t eye = 0; eye < 2; ++eye) {
      const float x = eye == 0 ? -0.032F : 0.032F;
      const XrPosef eye_offset{.orientation = {.x = 0, .y = 0, .z = 0, .w = 1},
                               .position = {.x = x, .y = 0, .z = 0}};
      views[eye].pose = compose(inverse_base, compose(head, eye_offset));
    }
    state->viewStateFlags = 0;
    if ((input->head.flags & protocol::PositionValid) != 0)
      state->viewStateFlags |= XR_VIEW_STATE_POSITION_VALID_BIT;
    if ((input->head.flags & protocol::OrientationValid) != 0)
      state->viewStateFlags |= XR_VIEW_STATE_ORIENTATION_VALID_BIT;
    if ((input->head.flags & protocol::PositionTracked) != 0)
      state->viewStateFlags |= XR_VIEW_STATE_POSITION_TRACKED_BIT;
    if ((input->head.flags & protocol::OrientationTracked) != 0)
      state->viewStateFlags |= XR_VIEW_STATE_ORIENTATION_TRACKED_BIT;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_sync_actions(XrSession session,
                                                  const XrActionsSyncInfo* info) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrSyncActions>(instance, "xrSyncActions");
  return next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info);
}

XRAPI_ATTR XrResult XRAPI_CALL hook_get_boolean(XrSession session,
                                                 const XrActionStateGetInfo* info,
                                                 XrActionStateBoolean* state) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrGetActionStateBoolean>(instance, "xrGetActionStateBoolean");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, state);
  const auto input = transport_latest_pose_input();
  if (XR_SUCCEEDED(result) && input.has_value() && info != nullptr && state != nullptr) {
    state->currentState = injected_boolean(*input, bindings_for_action(info->action, info->subactionPath));
    {
      std::scoped_lock lock(g_mutex);
      const StateKey key{session, info->action, info->subactionPath};
      const auto previous = g_boolean_states.find(key);
      state->changedSinceLastSync = previous == g_boolean_states.end() ||
                                            previous->second != state->currentState;
      g_boolean_states[key] = state->currentState;
    }
    state->lastChangeTime = 0;
    state->isActive = XR_TRUE;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_get_float(XrSession session,
                                               const XrActionStateGetInfo* info,
                                               XrActionStateFloat* state) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrGetActionStateFloat>(instance, "xrGetActionStateFloat");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, state);
  const auto input = transport_latest_pose_input();
  if (XR_SUCCEEDED(result) && input.has_value() && info != nullptr && state != nullptr) {
    state->currentState = injected_float(*input, bindings_for_action(info->action, info->subactionPath));
    {
      std::scoped_lock lock(g_mutex);
      const StateKey key{session, info->action, info->subactionPath};
      const auto previous = g_float_states.find(key);
      state->changedSinceLastSync = previous == g_float_states.end() ||
                                            previous->second != state->currentState;
      g_float_states[key] = state->currentState;
    }
    state->lastChangeTime = 0;
    state->isActive = XR_TRUE;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_get_vector2(XrSession session,
                                                 const XrActionStateGetInfo* info,
                                                 XrActionStateVector2f* state) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrGetActionStateVector2f>(instance, "xrGetActionStateVector2f");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, state);
  const auto input = transport_latest_pose_input();
  if (XR_SUCCEEDED(result) && input.has_value() && info != nullptr && state != nullptr) {
    const auto bindings = bindings_for_action(info->action, info->subactionPath);
    state->currentState = {};
    if (!bindings.empty()) {
      const auto& controller = controller_for_side(*input, bindings.front().side);
      state->currentState = {.x = controller.thumbstick[0], .y = controller.thumbstick[1]};
    }
    {
      std::scoped_lock lock(g_mutex);
      const StateKey key{session, info->action, info->subactionPath};
      const std::array<float, 2> current = {state->currentState.x, state->currentState.y};
      const auto previous = g_vector_states.find(key);
      state->changedSinceLastSync = previous == g_vector_states.end() ||
                                            previous->second != current;
      g_vector_states[key] = current;
    }
    state->lastChangeTime = 0;
    state->isActive = XR_TRUE;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_get_pose(XrSession session,
                                              const XrActionStateGetInfo* info,
                                              XrActionStatePose* state) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrGetActionStatePose>(instance, "xrGetActionStatePose");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info, state);
  if (XR_SUCCEEDED(result) && transport_latest_pose_input().has_value() && state != nullptr) {
    state->isActive = XR_TRUE;
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_apply_haptic(
    XrSession session, const XrHapticActionInfo* info, const XrHapticBaseHeader* feedback) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrApplyHapticFeedback>(instance, "xrApplyHapticFeedback");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID
                                          : next(session, info, feedback);
  if (XR_SUCCEEDED(result) && info != nullptr && feedback != nullptr &&
      feedback->type == XR_TYPE_HAPTIC_VIBRATION) {
    const auto* vibration = reinterpret_cast<const XrHapticVibration*>(feedback);
    send_haptic(haptic_sides(instance, info), protocol::HapticAction::Apply,
                std::clamp(vibration->amplitude, 0.0F, 1.0F), vibration->frequency,
                vibration->duration > 0 ? static_cast<std::uint64_t>(vibration->duration) : 0);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_stop_haptic(XrSession session,
                                                 const XrHapticActionInfo* info) {
  const XrInstance instance = instance_for_session(session);
  const auto next = next_function<PFN_xrStopHapticFeedback>(instance, "xrStopHapticFeedback");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID : next(session, info);
  if (XR_SUCCEEDED(result) && info != nullptr) {
    send_haptic(haptic_sides(instance, info), protocol::HapticAction::Stop, 0, 0, 0);
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_get_system_properties(
    XrInstance instance, XrSystemId system_id, XrSystemProperties* properties) {
  const auto next = next_function<PFN_xrGetSystemProperties>(instance, "xrGetSystemProperties");
  const XrResult result = next == nullptr ? XR_ERROR_HANDLE_INVALID
                                          : next(instance, system_id, properties);
  if (XR_SUCCEEDED(result) && properties != nullptr) {
    for (auto* chain = static_cast<XrBaseOutStructure*>(properties->next); chain != nullptr;
         chain = chain->next) {
      if (chain->type == XR_TYPE_SYSTEM_HAND_TRACKING_PROPERTIES_EXT) {
        auto* hand_properties = reinterpret_cast<XrSystemHandTrackingPropertiesEXT*>(chain);
        hand_properties->supportsHandTracking = XR_TRUE;
      }
    }
  }
  return result;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_create_hand_tracker(
    XrSession session, const XrHandTrackerCreateInfoEXT* info, XrHandTrackerEXT* tracker) {
  if (info == nullptr || tracker == nullptr ||
      (info->hand != XR_HAND_LEFT_EXT && info->hand != XR_HAND_RIGHT_EXT) ||
      info->handJointSet != XR_HAND_JOINT_SET_DEFAULT_EXT) {
    return XR_ERROR_VALIDATION_FAILURE;
  }
  const XrInstance instance = instance_for_session(session);
  if (instance == XR_NULL_HANDLE) return XR_ERROR_HANDLE_INVALID;
  const auto handle = reinterpret_cast<XrHandTrackerEXT>(g_next_hand_tracker.fetch_add(1));
  {
    std::scoped_lock lock(g_mutex);
    g_hand_trackers[handle] = HandTrackerData{
        .instance = instance,
        .session = session,
        .side = info->hand == XR_HAND_RIGHT_EXT ? protocol::HandSide::Right
                                                : protocol::HandSide::Left,
    };
  }
  *tracker = handle;
  return XR_SUCCESS;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_destroy_hand_tracker(XrHandTrackerEXT tracker) {
  std::scoped_lock lock(g_mutex);
  return g_hand_trackers.erase(tracker) == 1 ? XR_SUCCESS : XR_ERROR_HANDLE_INVALID;
}

XRAPI_ATTR XrResult XRAPI_CALL hook_locate_hand_joints(
    XrHandTrackerEXT tracker, const XrHandJointsLocateInfoEXT* info,
    XrHandJointLocationsEXT* locations) {
  if (info == nullptr || locations == nullptr || locations->jointLocations == nullptr ||
      locations->jointCount < XR_HAND_JOINT_COUNT_EXT) {
    return XR_ERROR_VALIDATION_FAILURE;
  }
  HandTrackerData tracker_data;
  SpaceData base;
  {
    std::scoped_lock lock(g_mutex);
    const auto found = g_hand_trackers.find(tracker);
    if (found == g_hand_trackers.end()) return XR_ERROR_HANDLE_INVALID;
    tracker_data = found->second;
    const auto found_space = g_spaces.find(info->baseSpace);
    if (found_space != g_spaces.end()) base = found_space->second;
  }
  const auto hands = transport_latest_hand_tracking();
  const bool active = hands.has_value() &&
      (tracker_data.side == protocol::HandSide::Right ? hands->right_active
                                                       : hands->left_active);
  locations->isActive = active ? XR_TRUE : XR_FALSE;
  if (!active) return XR_SUCCESS;

  const auto& joints = tracker_data.side == protocol::HandSide::Right
                           ? hands->right_joints
                           : hands->left_joints;
  XrPosef inverse_base{.orientation = {.x = 0, .y = 0, .z = 0, .w = 1},
                       .position = {.x = 0, .y = 0, .z = 0}};
  if (const auto pose_input = transport_latest_pose_input(); pose_input.has_value()) {
    inverse_base = inverse(local_pose(*pose_input, base));
  }
  for (std::size_t index = 0; index < protocol::kHandJointCount; ++index) {
    locations->jointLocations[index] = XrHandJointLocationEXT{
        .locationFlags = location_flags(joints[index].pose),
        .pose = compose(inverse_base, xr_pose(joints[index].pose)),
        .radius = joints[index].radius,
    };
  }
  for (auto* chain = static_cast<XrBaseOutStructure*>(locations->next); chain != nullptr;
       chain = chain->next) {
    if (chain->type == XR_TYPE_HAND_JOINT_VELOCITIES_EXT) {
      auto* velocities = reinterpret_cast<XrHandJointVelocitiesEXT*>(chain);
      if (velocities->jointVelocities != nullptr &&
          velocities->jointCount >= XR_HAND_JOINT_COUNT_EXT) {
        for (std::size_t index = 0; index < protocol::kHandJointCount; ++index) {
          velocities->jointVelocities[index] = XrHandJointVelocityEXT{
              .velocityFlags = 0,
              .linearVelocity = {},
              .angularVelocity = {},
          };
        }
      }
    }
  }
  return XR_SUCCESS;
}

}  // namespace

void input_register_instance(XrInstance instance, PFN_xrGetInstanceProcAddr gipa) {
  InstanceData data{.gipa = gipa};
  PFN_xrVoidFunction raw{};
  if (XR_SUCCEEDED(gipa(instance, "xrStringToPath", &raw)) && raw != nullptr) {
    const auto string_to_path = reinterpret_cast<PFN_xrStringToPath>(raw);
    (void)string_to_path(instance, "/user/hand/left", &data.left_path);
    (void)string_to_path(instance, "/user/hand/right", &data.right_path);
  }
  std::scoped_lock lock(g_mutex);
  g_instances[instance] = data;
}

void input_unregister_instance(XrInstance instance) {
  std::scoped_lock lock(g_mutex);
  for (auto space = g_spaces.begin(); space != g_spaces.end();) {
    space = space->second.instance == instance ? g_spaces.erase(space) : std::next(space);
  }
  for (auto action = g_actions.begin(); action != g_actions.end();) {
    action = action->second.instance == instance ? g_actions.erase(action) : std::next(action);
  }
  for (auto set = g_action_sets.begin(); set != g_action_sets.end();) {
    set = set->second.instance == instance ? g_action_sets.erase(set) : std::next(set);
  }
  for (auto session = g_sessions.begin(); session != g_sessions.end();) {
    session = session->second == instance ? g_sessions.erase(session) : std::next(session);
  }
  for (auto tracker = g_hand_trackers.begin(); tracker != g_hand_trackers.end();) {
    tracker = tracker->second.instance == instance ? g_hand_trackers.erase(tracker)
                                                   : std::next(tracker);
  }
  g_instances.erase(instance);
}

void input_register_session(XrSession session, XrInstance instance) {
  std::scoped_lock lock(g_mutex);
  g_sessions[session] = instance;
}

void input_unregister_session(XrSession session) {
  std::scoped_lock lock(g_mutex);
  for (auto space = g_spaces.begin(); space != g_spaces.end();) {
    space = space->second.session == session ? g_spaces.erase(space) : std::next(space);
  }
  g_sessions.erase(session);
  std::erase_if(g_hand_trackers, [session](const auto& item) {
    return item.second.session == session;
  });
  std::erase_if(g_boolean_states, [session](const auto& item) {
    return std::get<0>(item.first) == session;
  });
  std::erase_if(g_float_states, [session](const auto& item) {
    return std::get<0>(item.first) == session;
  });
  std::erase_if(g_vector_states, [session](const auto& item) {
    return std::get<0>(item.first) == session;
  });
}

bool input_get_proc_addr(const char* name, PFN_xrVoidFunction* function) {
  struct Entry { const char* name; PFN_xrVoidFunction function; };
  const Entry entries[] = {
      {"xrCreateActionSet", reinterpret_cast<PFN_xrVoidFunction>(hook_create_action_set)},
      {"xrDestroyActionSet", reinterpret_cast<PFN_xrVoidFunction>(hook_destroy_action_set)},
      {"xrCreateAction", reinterpret_cast<PFN_xrVoidFunction>(hook_create_action)},
      {"xrDestroyAction", reinterpret_cast<PFN_xrVoidFunction>(hook_destroy_action)},
      {"xrSuggestInteractionProfileBindings", reinterpret_cast<PFN_xrVoidFunction>(hook_suggest_bindings)},
      {"xrCreateReferenceSpace", reinterpret_cast<PFN_xrVoidFunction>(hook_create_reference_space)},
      {"xrCreateActionSpace", reinterpret_cast<PFN_xrVoidFunction>(hook_create_action_space)},
      {"xrDestroySpace", reinterpret_cast<PFN_xrVoidFunction>(hook_destroy_space)},
      {"xrLocateSpace", reinterpret_cast<PFN_xrVoidFunction>(hook_locate_space)},
      {"xrLocateViews", reinterpret_cast<PFN_xrVoidFunction>(hook_locate_views)},
      {"xrSyncActions", reinterpret_cast<PFN_xrVoidFunction>(hook_sync_actions)},
      {"xrGetActionStateBoolean", reinterpret_cast<PFN_xrVoidFunction>(hook_get_boolean)},
      {"xrGetActionStateFloat", reinterpret_cast<PFN_xrVoidFunction>(hook_get_float)},
      {"xrGetActionStateVector2f", reinterpret_cast<PFN_xrVoidFunction>(hook_get_vector2)},
      {"xrGetActionStatePose", reinterpret_cast<PFN_xrVoidFunction>(hook_get_pose)},
      {"xrApplyHapticFeedback", reinterpret_cast<PFN_xrVoidFunction>(hook_apply_haptic)},
      {"xrStopHapticFeedback", reinterpret_cast<PFN_xrVoidFunction>(hook_stop_haptic)},
      {"xrGetSystemProperties", reinterpret_cast<PFN_xrVoidFunction>(hook_get_system_properties)},
      {"xrCreateHandTrackerEXT", reinterpret_cast<PFN_xrVoidFunction>(hook_create_hand_tracker)},
      {"xrDestroyHandTrackerEXT", reinterpret_cast<PFN_xrVoidFunction>(hook_destroy_hand_tracker)},
      {"xrLocateHandJointsEXT", reinterpret_cast<PFN_xrVoidFunction>(hook_locate_hand_joints)},
  };
  for (const auto& entry : entries) {
    if (std::strcmp(name, entry.name) == 0) {
      *function = entry.function;
      return true;
    }
  }
  return false;
}
