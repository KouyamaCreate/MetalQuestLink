#pragma once

#include <optional>

#include "metalquestlink/protocol.hpp"

void transport_start();
void transport_stop();
[[nodiscard]] bool transport_connected();
void transport_send(const metalquestlink::protocol::Message& message);
[[nodiscard]] std::optional<metalquestlink::protocol::PoseInput> transport_latest_pose_input();
[[nodiscard]] std::optional<metalquestlink::protocol::HandTrackingInput>
transport_latest_hand_tracking();
