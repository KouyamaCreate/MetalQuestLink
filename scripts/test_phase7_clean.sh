#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
SIM_APP="${METALQUESTLINK_SIM_APP:-/Applications/MetaXRSimulator.app}"
RUNTIME_JSON="$SIM_APP/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json"
META_CORE_LOCAL="${METALQUESTLINK_META_CORE_LOCAL:-/private/tmp/meta-xr-core-203-full/package}"
version="$(tr -d '[:space:]' < "$ROOT_DIR/editor-package/VERSION")"
tarball="${METALQUESTLINK_TARBALL:-$ROOT_DIR/dist/com.metalquestlink.editor-${version}.tgz}"

[[ -x "$UNITY" ]] || { echo "Unityが見つかりません: $UNITY" >&2; exit 2; }
[[ -f "$RUNTIME_JSON" ]] || { echo "Meta XR Simulatorが見つかりません: $RUNTIME_JSON" >&2; exit 2; }
[[ -s "$tarball" ]] || { echo "UPM tarballがありません: $tarball" >&2; exit 2; }

outside="$(mktemp -d "${TMPDIR:-/tmp}/metalquestlink-clean-unity.XXXXXX")"
project="$outside/MetaXRMinimal"
cleanup() {
  status=$?
  if [[ $status -ne 0 ]]; then
    for log in "$outside"/*.log; do
      [[ -f "$log" ]] || continue
      echo "---- $(basename "$log") ----" >&2
      tail -80 "$log" >&2
    done
  fi
  rm -rf "$outside"
  return $status
}
trap cleanup EXIT

mkdir -p "$project"
rsync -a \
  --exclude Library --exclude Logs --exclude Temp --exclude UserSettings --exclude TestResults.xml \
  "$ROOT_DIR/samples/MetaXRMinimal/" "$project/"
rm -f "$project/Packages/packages-lock.json"
sed -i '' \
  "s|\"com.metalquestlink.editor\": \"file:[^\"]*\"|\"com.metalquestlink.editor\": \"file:$tarball\"|" \
  "$project/Packages/manifest.json"
if [[ -d "$META_CORE_LOCAL" ]]; then
  sed -i '' \
    "s|\"com.meta.xr.sdk.core\": \"203.0.0\"|\"com.meta.xr.sdk.core\": \"file:$META_CORE_LOCAL\"|" \
    "$project/Packages/manifest.json"
fi

if ! "$UNITY" \
    -batchmode -nographics -quit \
    -projectPath "$project" \
    -executeMethod MetalQuestLink.Sample.Editor.SampleBuilder.ConfigureAndBuild \
    -logFile "$outside/build.log"; then
  echo "Unity sample setup failed" >&2
  exit 1
fi

if ! "$UNITY" \
    -batchmode -nographics \
    -projectPath "$project" \
    -runTests -testPlatform EditMode \
    -testFilter MetalQuestLink.Editor.Tests \
    -testResults "$outside/editmode-results.xml" \
    -logFile "$outside/editmode.log"; then
  echo "Unity EditMode tests failed" >&2
  exit 1
fi

open -a "$SIM_APP" >/dev/null 2>&1
if ! XR_RUNTIME_JSON="$RUNTIME_JSON" \
    METALQUESTLINK_ENABLE_API_LAYER=1 \
    "$UNITY" \
    -batchmode \
    -projectPath "$project" \
    -runTests -testPlatform PlayMode \
    -testFilter MetalQuestLink.Sample.Tests.SamplePlayModeTests \
    -testResults "$outside/playmode-results.xml" \
    -logFile "$outside/playmode.log"; then
  echo "Unity PlayMode tests failed" >&2
  exit 1
fi

grep -q 'result="Passed"' "$outside/editmode-results.xml"
grep -q 'result="Passed"' "$outside/playmode-results.xml"
grep -q 'METALQUESTLINK_SAMPLE_PLAY_VERIFIED layer=loaded status=waiting_for_connection' "$outside/playmode.log"
grep -q 'loaded instance for' "$project/Library/MetalQuestLink/layer.log"

package_json="$(find "$project/Library/PackageCache" -path '*com.metalquestlink.editor*/package.json' -print -quit)"
[[ -n "$package_json" ]] || { echo "PackageCacheにMetalQuestLink packageがありません" >&2; exit 1; }
package_root="$(dirname "$package_json")"
"$ROOT_DIR/scripts/doctor.sh" --package-root "$package_root" --register

echo "Phase 7 clean UPM tarball Unity/Simulator E2E passed"
echo "Temporary logs were verified under: $outside"
