#!/bin/bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
BUILD_DIR="${MAQUESTLINK_BUILD_DIR:-${ROOT_DIR}/build}"
SIM_APP="${MAQUESTLINK_SIM_APP:-/Applications/MetaXRSimulator.app}"
SIM_RUNTIME_DIR="${SIM_APP}/Contents/Resources/MetaXRSimulator"
RUNTIME_JSON="${SIM_RUNTIME_DIR}/meta_openxr_simulator.json"
CLIENT="${BUILD_DIR}/layer/maquestlink_native_test"
RUN_ID="$$"
LAYER_LOG="${BUILD_DIR}/maquestlink-layer-${RUN_ID}.log"
CLIENT_LOG="${BUILD_DIR}/phase1-native-test-${RUN_ID}.log"

if [[ ! -f "${RUNTIME_JSON}" ]]; then
  echo "Meta XR Simulator runtime manifest not found: ${RUNTIME_JSON}" >&2
  exit 2
fi
if [[ ! -x "${CLIENT}" ]]; then
  echo "Native test client not built: ${CLIENT}" >&2
  echo "Run: cmake -B build && cmake --build build" >&2
  exit 2
fi

open -a "${SIM_APP}" >/dev/null 2>&1

if ! XR_RUNTIME_JSON="${RUNTIME_JSON}" \
  XDG_DATA_HOME="${BUILD_DIR}" \
  MAQUESTLINK_ENABLE_API_LAYER=1 \
  MAQUESTLINK_LAYER_LOG="${LAYER_LOG}" \
  XR_LOADER_DEBUG=info \
    "${CLIENT}" --frames "${MAQUESTLINK_TEST_FRAMES:-120}" >"${CLIENT_LOG}" 2>&1; then
  cat "${CLIENT_LOG}"
  exit 1
fi
cat "${CLIENT_LOG}"

rg -q "loaded instance for MaQuestLinkNativeTest" "${LAYER_LOG}"
rg -q "MAQUESTLINK_FRAME_LOOP_OK" "${CLIENT_LOG}"

echo "Phase 1 native E2E passed"
echo "Layer log: ${LAYER_LOG}"
echo "Client log: ${CLIENT_LOG}"
