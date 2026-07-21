#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
SIM_APP="${METALQUESTLINK_SIM_APP:-/Applications/MetaXRSimulator.app}"
RUNTIME_JSON="${SIM_APP}/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json"
PROJECT="${ROOT_DIR}/samples/MetaXRMinimal"
BUILD_LOG="${ROOT_DIR}/build/phase5-sample-build.log"
EDIT_LOG="${ROOT_DIR}/build/phase5-editmode.log"
PLAY_LOG="${ROOT_DIR}/build/phase5-playmode.log"
EDIT_RESULTS="${ROOT_DIR}/build/phase5-editmode-results.xml"
PLAY_RESULTS="${ROOT_DIR}/build/phase5-playmode-results.xml"
META_CORE_LOCAL="${METALQUESTLINK_META_CORE_LOCAL:-/private/tmp/meta-xr-core-203-full/package}"
MANIFEST="${PROJECT}/Packages/manifest.json"
MANIFEST_BACKUP="${ROOT_DIR}/build/phase5-manifest.json"
LOCK="${PROJECT}/Packages/packages-lock.json"
LOCK_BACKUP="${ROOT_DIR}/build/phase5-packages-lock.json"

cleanup() {
  if [[ -f "${MANIFEST_BACKUP}" ]]; then
    cp "${MANIFEST_BACKUP}" "${MANIFEST}"
  fi
  if [[ -f "${LOCK_BACKUP}" ]]; then
    cp "${LOCK_BACKUP}" "${LOCK}"
  fi
  # UPM can flush project state immediately after Unity exits; restore after that flush as well.
  sleep 2
  if [[ -f "${MANIFEST_BACKUP}" ]]; then
    cp "${MANIFEST_BACKUP}" "${MANIFEST}"
  fi
  if [[ -f "${LOCK_BACKUP}" ]]; then
    cp "${LOCK_BACKUP}" "${LOCK}"
  fi
}
trap cleanup EXIT

if [[ ! -x "${UNITY}" ]]; then
  echo "Unity 6000.3.6f1 not found: ${UNITY}" >&2
  exit 2
fi
if [[ ! -f "${RUNTIME_JSON}" ]]; then
  echo "Meta XR Simulator runtime manifest not found: ${RUNTIME_JSON}" >&2
  exit 2
fi
if [[ ! -f "${ROOT_DIR}/build/layer/libmetalquestlink_openxr_layer.so" ]]; then
  echo "Native layer not built. Run: cmake -B build && cmake --build build" >&2
  exit 2
fi

# The official Core SDK tarball is large. Use an already extracted copy for deterministic local E2E.
if [[ -d "${META_CORE_LOCAL}" ]]; then
  cp "${MANIFEST}" "${MANIFEST_BACKUP}"
  if [[ -f "${LOCK}" ]]; then
    cp "${LOCK}" "${LOCK_BACKUP}"
  fi
  sed -i '' \
    "s|\"com.meta.xr.sdk.core\": \"203.0.0\"|\"com.meta.xr.sdk.core\": \"file:${META_CORE_LOCAL}\"|" \
    "${MANIFEST}"
fi

"${UNITY}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${PROJECT}" \
  -executeMethod MetalQuestLink.Sample.Editor.SampleBuilder.ConfigureAndBuild \
  -logFile "${BUILD_LOG}"

"${UNITY}" \
  -batchmode \
  -nographics \
  -projectPath "${PROJECT}" \
  -runTests \
  -testPlatform EditMode \
  -testFilter MetalQuestLink.Editor.Tests \
  -testResults "${EDIT_RESULTS}" \
  -logFile "${EDIT_LOG}"

open -a "${SIM_APP}" >/dev/null 2>&1

XR_RUNTIME_JSON="${RUNTIME_JSON}" \
METALQUESTLINK_ENABLE_API_LAYER=1 \
  "${UNITY}" \
  -batchmode \
  -projectPath "${PROJECT}" \
  -runTests \
  -testPlatform PlayMode \
  -testFilter MetalQuestLink.Sample.Tests.SamplePlayModeTests \
  -testResults "${PLAY_RESULTS}" \
  -logFile "${PLAY_LOG}"

rg -q 'result="Passed"' "${EDIT_RESULTS}"
rg -q 'result="Passed"' "${PLAY_RESULTS}"
rg -q 'METALQUESTLINK_SAMPLE_PLAY_VERIFIED layer=loaded status=(connected|waiting_for_connection)' "${PLAY_LOG}"
rg -q 'loaded instance for' "${PROJECT}/Library/MetalQuestLink/layer.log"

echo "Phase 5 Unity Editor integration passed"
echo "Build log: ${BUILD_LOG}"
echo "EditMode results: ${EDIT_RESULTS}"
echo "PlayMode results: ${PLAY_RESULTS}"
