#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
ADB="${ADB:-/opt/homebrew/bin/adb}"
APK="${METALQUESTLINK_APK:-$ROOT_DIR/quest-client/Builds/MetalQuestLink.apk}"
PORT="${METALQUESTLINK_PORT:-42424}"
PACKAGE="com.metalquestlink.questclient"
ACTIVITY="com.unity3d.player.UnityPlayerGameActivity"
RUN_ID="$$"
LOGCAT_LOG="$ROOT_DIR/build/phase4-device-logcat-$RUN_ID.log"
PRODUCER_LOG="$ROOT_DIR/build/phase4-device-producer-$RUN_ID.log"
LOGCAT_PID=""
PRODUCER_PID=""
HAND_VISUALIZATION="${METALQUESTLINK_HAND_VISUALIZATION:-0}"
REQUIRE_ACTIVE_HANDS="${METALQUESTLINK_REQUIRE_ACTIVE_HANDS:-0}"
if [[ "$REQUIRE_ACTIVE_HANDS" == "1" ]]; then
  HAND_VISUALIZATION="1"
fi
HAND_VISUALIZATION_BOOL="false"
if [[ "$HAND_VISUALIZATION" == "1" ]]; then
  HAND_VISUALIZATION_BOOL="true"
fi

cleanup() {
  if [[ -n "$LOGCAT_PID" ]] && kill -0 "$LOGCAT_PID" 2>/dev/null; then
    kill "$LOGCAT_PID" 2>/dev/null || true
    wait "$LOGCAT_PID" 2>/dev/null || true
  fi
  if [[ -n "$PRODUCER_PID" ]] && kill -0 "$PRODUCER_PID" 2>/dev/null; then
    kill "$PRODUCER_PID" 2>/dev/null || true
    wait "$PRODUCER_PID" 2>/dev/null || true
  fi
  "$ADB" shell am force-stop "$PACKAGE" >/dev/null 2>&1 || true
  "$ADB" shell am broadcast -a com.oculus.vrpowermanager.automation_enable >/dev/null 2>&1 || true
  "$ADB" reverse --remove "tcp:$PORT" >/dev/null 2>&1 || true
}
trap cleanup EXIT

if [[ ! -x "$ADB" ]]; then
  echo "adb not found: $ADB" >&2
  exit 2
fi
if [[ ! -s "$APK" ]]; then
  echo "Quest client APK not found: $APK" >&2
  echo "Run: scripts/build_quest_client.sh" >&2
  exit 2
fi

DEVICE_COUNT="$("$ADB" devices | awk 'NR > 1 && $2 == "device" {count++} END {print count + 0}')"
if [[ "$DEVICE_COUNT" -eq 0 ]]; then
  echo "Quest 3 is not connected. Connect it by USB, allow USB debugging, then rerun scripts/e2e_device.sh." >&2
  exit 2
fi
if [[ "$DEVICE_COUNT" -ne 1 ]]; then
  echo "Expected exactly one adb device, found $DEVICE_COUNT" >&2
  exit 2
fi

mkdir -p "$ROOT_DIR/build"
"$ADB" install -r "$APK"
"$ADB" reverse "tcp:$PORT" "tcp:$PORT"
POWER_RESULT="$("$ADB" shell am broadcast -a com.oculus.vrpowermanager.automation_disable)"
echo "$POWER_RESULT"
if [[ "$POWER_RESULT" != *"result="* && "$POWER_RESULT" != *"Broadcast completed"* ]]; then
  echo "Quest power-manager automation command was not acknowledged" >&2
  exit 1
fi

"$ADB" logcat -c
"$ADB" logcat -v brief >"$LOGCAT_LOG" 2>&1 &
LOGCAT_PID="$!"

"$ADB" shell am start -S -n "$PACKAGE/$ACTIVITY" \
  --ez metalquestlink_diagnostic true \
  --ez metalquestlink_passthrough true \
  --ez metalquestlink_hand_visualization "$HAND_VISUALIZATION_BOOL" \
  --es metalquestlink_host 127.0.0.1 \
  --ei metalquestlink_port "$PORT"

# Quest OS can spend tens of seconds initializing OpenXR after a fresh APK install.
# Wait for the client Update loop before starting the finite native producer.
CLIENT_READY=0
for _ in $(seq 1 90); do
  if rg -q 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG"; then
    CLIENT_READY=1
    break
  fi
  sleep 1
done
if [[ "$CLIENT_READY" -ne 1 ]]; then
  echo "Quest client did not finish OpenXR initialization within 90 seconds. Keep the headset awake and worn. See: $LOGCAT_LOG" >&2
  exit 1
fi

PRODUCER_ENV=(
  "METALQUESTLINK_PORT=$PORT"
  "METALQUESTLINK_VERIFY_DEVICE_INPUT=1"
  "METALQUESTLINK_TEST_FRAMES=${METALQUESTLINK_DEVICE_FRAMES:-2400}"
)
if [[ "$REQUIRE_ACTIVE_HANDS" == "1" ]]; then
  PRODUCER_ENV+=("METALQUESTLINK_REQUIRE_HANDS=1")
fi
env "${PRODUCER_ENV[@]}" "$ROOT_DIR/scripts/test_phase1.sh" >"$PRODUCER_LOG" 2>&1 &
PRODUCER_PID="$!"
set +e
wait "$PRODUCER_PID"
PRODUCER_RESULT="$?"
set -e
PRODUCER_PID=""
if [[ "$PRODUCER_RESULT" -ne 0 ]]; then
  echo "Mac producer failed (exit $PRODUCER_RESULT). See: $PRODUCER_LOG" >&2
  tail -n 80 "$PRODUCER_LOG" >&2
  if rg -q 'NullReferenceException|METALQUESTLINK.*FAILED' "$LOGCAT_LOG"; then
    echo "Relevant Quest errors from: $LOGCAT_LOG" >&2
    rg 'NullReferenceException|METALQUESTLINK.*FAILED' "$LOGCAT_LOG" | tail -n 20 >&2
  fi
  exit "$PRODUCER_RESULT"
fi
sleep 2

if ! rg -q 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG"; then
  echo "No structured diagnostics were emitted. See: $LOGCAT_LOG" >&2
  exit 1
fi

MAX_RECEIVE="$(rg 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"received_fps":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_DECODE="$(rg 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"decode_fps":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_POSE="$(rg 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"pose_hz":([0-9]+).*/\1/' | sort -n | tail -1)"
LAST_LATENCY="$(rg 'METALQUESTLINK_DIAGNOSTIC.*"connected":true.*"received_fps":([3-9][0-9]|[1-9][0-9]{2,}).*"clock_synced":true' "$LOGCAT_LOG" | \
  sed -nE 's/.*"capture_to_decode_ms":([0-9]+([.][0-9]+)?).*/\1/p' | tail -1)"
MAX_HANDS="$(rg 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"hands_sent":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_HAPTICS="$(rg 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"haptics_received":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_VALID_HAND_JOINTS="$(rg 'METALQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"valid_hand_joints":([0-9]+).*/\1/' | sort -n | tail -1)"

if (( MAX_RECEIVE < 30 || MAX_DECODE < 30 || MAX_POSE < 60 )); then
  echo "Phase 4 device E2E failed: received_fps=$MAX_RECEIVE decode_fps=$MAX_DECODE pose_hz=$MAX_POSE" >&2
  exit 1
fi
if ! rg -q 'METALQUESTLINK_DIAGNOSTIC.*"reprojection":"immersive_projection".*"clock_synced":true' "$LOGCAT_LOG" || \
   [[ -z "$LAST_LATENCY" ]]; then
  echo "Phase 6 device E2E failed: immersive projection or synchronized latency was not reported" >&2
  exit 1
fi
if ! rg -q 'MetalQuestLinkProjection.*created immersive Android Surface projection swapchain' "$LOGCAT_LOG"; then
  echo "Quest immersive projection surface was not created. See: $LOGCAT_LOG" >&2
  exit 1
fi
if (( MAX_HANDS < 1 || MAX_HAPTICS < 1 )) || \
   ! rg -q 'METALQUESTLINK_DIAGNOSTIC.*"passthrough":"underlay_uniform_alpha"' "$LOGCAT_LOG"; then
  echo "Phase 8 device E2E failed: hands_sent=$MAX_HANDS haptics_received=$MAX_HAPTICS or passthrough inactive" >&2
  exit 1
fi
if [[ "$REQUIRE_ACTIVE_HANDS" == "1" ]] && (( MAX_VALID_HAND_JOINTS < 20 )); then
  echo "Active hand tracking failed: valid_hand_joints=$MAX_VALID_HAND_JOINTS (show a hand to the headset cameras)" >&2
  exit 1
fi

echo "METALQUESTLINK_DEVICE_E2E_OK received_fps=$MAX_RECEIVE decode_fps=$MAX_DECODE pose_hz=$MAX_POSE capture_to_decode_ms=$LAST_LATENCY hands_sent=$MAX_HANDS active_hand_joints=$MAX_VALID_HAND_JOINTS haptics_received=$MAX_HAPTICS passthrough=1"
echo "Logcat: $LOGCAT_LOG"
echo "Producer: $PRODUCER_LOG"
