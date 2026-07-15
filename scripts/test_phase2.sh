#!/bin/bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
BUILD_DIR="${MAQUESTLINK_BUILD_DIR:-${ROOT_DIR}/build}"
VIEWER="${BUILD_DIR}/layer/maquestlink_mock_viewer"
RUN_ID="$$"
PORT="${MAQUESTLINK_PORT:-42425}"
VIEWER_LOG="${BUILD_DIR}/phase2-viewer-${RUN_ID}.log"
PRODUCER_LOG="${BUILD_DIR}/phase2-producer-${RUN_ID}.log"
PASSTHROUGH_LOG="${BUILD_DIR}/phase2-passthrough-${RUN_ID}.log"
PER_EYE_VIEWER_LOG="${BUILD_DIR}/phase2-per-eye-viewer-${RUN_ID}.log"
PER_EYE_PRODUCER_LOG="${BUILD_DIR}/phase2-per-eye-producer-${RUN_ID}.log"
VIEWER_PID=""

cleanup() {
  if [[ -n "${VIEWER_PID}" ]] && kill -0 "${VIEWER_PID}" 2>/dev/null; then
    kill "${VIEWER_PID}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

if [[ ! -x "${VIEWER}" ]]; then
  echo "Mock viewer not built: ${VIEWER}" >&2
  echo "Run: cmake -B build && cmake --build build" >&2
  exit 2
fi

echo "Checking disconnected pass-through..."
if ! MAQUESTLINK_PORT="${PORT}" MAQUESTLINK_TEST_FRAMES=60 \
  "${ROOT_DIR}/scripts/test_phase1.sh" >"${PASSTHROUGH_LOG}" 2>&1; then
  cat "${PASSTHROUGH_LOG}"
  exit 1
fi
cat "${PASSTHROUGH_LOG}"

"${VIEWER}" --port "${PORT}" --frames 120 --min-fps 30 >"${VIEWER_LOG}" 2>&1 &
VIEWER_PID="$!"

echo "Checking connected H.264 loopback stream..."
if ! MAQUESTLINK_PORT="${PORT}" MAQUESTLINK_TEST_FRAMES=240 \
  "${ROOT_DIR}/scripts/test_phase1.sh" >"${PRODUCER_LOG}" 2>&1; then
  cat "${PRODUCER_LOG}"
  exit 1
fi
cat "${PRODUCER_LOG}"
wait "${VIEWER_PID}"
VIEWER_PID=""

rg -q "MAQUESTLINK_FRAME_LOOP_OK frames=60" "${PASSTHROUGH_LOG}"
rg -q "MAQUESTLINK_VIDEO_STATS" "${PRODUCER_LOG}"
rg -q "MAQUESTLINK_VIDEO_E2E_OK" "${VIEWER_LOG}"

"${VIEWER}" --port "${PORT}" --frames 60 --min-fps 30 >"${PER_EYE_VIEWER_LOG}" 2>&1 &
VIEWER_PID="$!"

echo "Checking separate per-eye 2D swapchains..."
if ! MAQUESTLINK_PORT="${PORT}" MAQUESTLINK_TEST_FRAMES=120 \
  MAQUESTLINK_TEST_PER_EYE_SWAPCHAINS=1 \
  "${ROOT_DIR}/scripts/test_phase1.sh" >"${PER_EYE_PRODUCER_LOG}" 2>&1; then
  cat "${PER_EYE_PRODUCER_LOG}"
  exit 1
fi
cat "${PER_EYE_PRODUCER_LOG}"
wait "${VIEWER_PID}"
VIEWER_PID=""

rg -q "swapchain_mode=per-eye" "${PER_EYE_PRODUCER_LOG}"
rg -q "projection swapchainMode=per-eye" "${PER_EYE_PRODUCER_LOG}"
rg -q "MAQUESTLINK_VIDEO_STATS" "${PER_EYE_PRODUCER_LOG}"
rg -q "MAQUESTLINK_VIDEO_E2E_OK" "${PER_EYE_VIEWER_LOG}"

echo "Phase 2 video E2E passed"
echo "Viewer log: ${VIEWER_LOG}"
echo "Producer log: ${PRODUCER_LOG}"
echo "Pass-through log: ${PASSTHROUGH_LOG}"
echo "Per-eye viewer log: ${PER_EYE_VIEWER_LOG}"
echo "Per-eye producer log: ${PER_EYE_PRODUCER_LOG}"
