#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
ADB="${ADB:-/opt/homebrew/bin/adb}"
APK="${MAQUESTLINK_APK:-$ROOT_DIR/quest-client/Builds/MaQuestLink.apk}"
PORT="${MAQUESTLINK_PORT:-42424}"
PACKAGE="com.maquestlink.questclient"
ACTIVITY="com.unity3d.player.UnityPlayerGameActivity"
RUN_ID="$$"
LOGCAT_LOG="$ROOT_DIR/build/phase4-device-logcat-$RUN_ID.log"
PRODUCER_LOG="$ROOT_DIR/build/phase4-device-producer-$RUN_ID.log"
LOGCAT_PID=""
PRODUCER_PID=""
HAND_VISUALIZATION="${MAQUESTLINK_HAND_VISUALIZATION:-0}"
REQUIRE_ACTIVE_HANDS="${MAQUESTLINK_REQUIRE_ACTIVE_HANDS:-0}"
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
  --ez maquestlink_diagnostic true \
  --ez maquestlink_passthrough true \
  --ez maquestlink_hand_visualization "$HAND_VISUALIZATION_BOOL" \
  --es maquestlink_host 127.0.0.1 \
  --ei maquestlink_port "$PORT"

PRODUCER_ENV=(
  "MAQUESTLINK_PORT=$PORT"
  "MAQUESTLINK_VERIFY_DEVICE_INPUT=1"
  "MAQUESTLINK_TEST_FRAMES=${MAQUESTLINK_DEVICE_FRAMES:-2400}"
)
if [[ "$REQUIRE_ACTIVE_HANDS" == "1" ]]; then
  PRODUCER_ENV+=("MAQUESTLINK_REQUIRE_HANDS=1")
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
  if rg -q 'NullReferenceException|MAQUESTLINK.*FAILED' "$LOGCAT_LOG"; then
    echo "Relevant Quest errors from: $LOGCAT_LOG" >&2
    rg 'NullReferenceException|MAQUESTLINK.*FAILED' "$LOGCAT_LOG" | tail -n 20 >&2
  fi
  exit "$PRODUCER_RESULT"
fi
sleep 2

if ! rg -q 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG"; then
  echo "No structured diagnostics were emitted. See: $LOGCAT_LOG" >&2
  exit 1
fi

MAX_RECEIVE="$(rg 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"received_fps":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_DECODE="$(rg 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"decode_fps":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_POSE="$(rg 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"pose_hz":([0-9]+).*/\1/' | sort -n | tail -1)"
LAST_LATENCY="$(rg 'MAQUESTLINK_DIAGNOSTIC.*"connected":true.*"received_fps":([3-9][0-9]|[1-9][0-9]{2,}).*"clock_synced":true' "$LOGCAT_LOG" | \
  sed -nE 's/.*"capture_to_decode_ms":([0-9]+([.][0-9]+)?).*/\1/p' | tail -1)"
MAX_HANDS="$(rg 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"hands_sent":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_HAPTICS="$(rg 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"haptics_received":([0-9]+).*/\1/' | sort -n | tail -1)"
MAX_VALID_HAND_JOINTS="$(rg 'MAQUESTLINK_DIAGNOSTIC' "$LOGCAT_LOG" | sed -E 's/.*"valid_hand_joints":([0-9]+).*/\1/' | sort -n | tail -1)"

if (( MAX_RECEIVE < 30 || MAX_DECODE < 30 || MAX_POSE < 60 )); then
  echo "Phase 4 device E2E failed: received_fps=$MAX_RECEIVE decode_fps=$MAX_DECODE pose_hz=$MAX_POSE" >&2
  exit 1
fi
if ! rg -q 'MAQUESTLINK_DIAGNOSTIC.*"reprojection":"world_fixed".*"clock_synced":true' "$LOGCAT_LOG" || \
   [[ -z "$LAST_LATENCY" ]]; then
  echo "Phase 6 device E2E failed: world-fixed reprojection or synchronized latency was not reported" >&2
  exit 1
fi
if (( MAX_HANDS < 1 || MAX_HAPTICS < 1 )) || \
   ! rg -q 'MAQUESTLINK_DIAGNOSTIC.*"passthrough":"underlay_uniform_alpha"' "$LOGCAT_LOG"; then
  echo "Phase 8 device E2E failed: hands_sent=$MAX_HANDS haptics_received=$MAX_HAPTICS or passthrough inactive" >&2
  exit 1
fi
if [[ "$REQUIRE_ACTIVE_HANDS" == "1" ]] && (( MAX_VALID_HAND_JOINTS < 20 )); then
  echo "Active hand tracking failed: valid_hand_joints=$MAX_VALID_HAND_JOINTS (show a hand to the headset cameras)" >&2
  exit 1
fi

echo "MAQUESTLINK_DEVICE_E2E_OK received_fps=$MAX_RECEIVE decode_fps=$MAX_DECODE pose_hz=$MAX_POSE capture_to_decode_ms=$LAST_LATENCY hands_sent=$MAX_HANDS active_hand_joints=$MAX_VALID_HAND_JOINTS haptics_received=$MAX_HAPTICS passthrough=1"
echo "Logcat: $LOGCAT_LOG"
echo "Producer: $PRODUCER_LOG"
