#pragma once

#include <optional>

#include "maquestlink/protocol.hpp"

void transport_start();
void transport_stop();
[[nodiscard]] bool transport_connected();
void transport_send(const maquestlink::protocol::Message& message);
[[nodiscard]] std::optional<maquestlink::protocol::PoseInput> transport_latest_pose_input();
[[nodiscard]] std::optional<maquestlink::protocol::HandTrackingInput>
transport_latest_hand_tracking();
