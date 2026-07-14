#!/bin/bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
BUILD_DIR="${MAQUESTLINK_BUILD_DIR:-${ROOT_DIR}/build}"
VIEWER="${BUILD_DIR}/layer/maquestlink_mock_viewer"
RUN_ID="$$"
PORT="${MAQUESTLINK_PORT:-42424}"
VIEWER_LOG="${BUILD_DIR}/phase3-mock-client-${RUN_ID}.log"
PRODUCER_LOG="${BUILD_DIR}/phase3-native-test-${RUN_ID}.log"
VIEWER_PID=""

cleanup() {
  if [[ -n "${VIEWER_PID}" ]] && kill -0 "${VIEWER_PID}" 2>/dev/null; then
    kill "${VIEWER_PID}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

if [[ ! -x "${VIEWER}" ]]; then
  echo "Mock client not built: ${VIEWER}" >&2
  exit 2
fi

"${VIEWER}" --port "${PORT}" --frames 120 --min-fps 30 --send-input \
  >"${VIEWER_LOG}" 2>&1 &
VIEWER_PID="$!"

echo "Checking synthetic OpenXR pose and action injection..."
MAQUESTLINK_PORT="${PORT}" MAQUESTLINK_VERIFY_INPUT=1 MAQUESTLINK_TEST_FRAMES=240 \
  "${ROOT_DIR}/scripts/test_phase1.sh" 2>&1 | tee "${PRODUCER_LOG}"
wait "${VIEWER_PID}"
VIEWER_PID=""

rg -q "MAQUESTLINK_INPUT_E2E_OK views=1 actions=1 space=1" "${PRODUCER_LOG}"
rg -q "MAQUESTLINK_VIDEO_E2E_OK.*input_sent=[1-9][0-9]*" "${VIEWER_LOG}"

echo "Phase 3 input E2E passed"
echo "Mock client log: ${VIEWER_LOG}"
echo "Native test log: ${PRODUCER_LOG}"
