#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
SIM_APP="${MAQUESTLINK_SIM_APP:-/Applications/MetaXRSimulator.app}"
RUNTIME_JSON="$SIM_APP/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json"
META_CORE_LOCAL="${MAQUESTLINK_META_CORE_LOCAL:-/private/tmp/meta-xr-core-203-full/package}"
version="$(tr -d '[:space:]' < "$ROOT_DIR/editor-package/VERSION")"
tarball="${MAQUESTLINK_TARBALL:-$ROOT_DIR/dist/com.maquestlink.editor-${version}.tgz}"

[[ -x "$UNITY" ]] || { echo "Unityが見つかりません: $UNITY" >&2; exit 2; }
[[ -f "$RUNTIME_JSON" ]] || { echo "Meta XR Simulatorが見つかりません: $RUNTIME_JSON" >&2; exit 2; }
[[ -s "$tarball" ]] || { echo "UPM tarballがありません: $tarball" >&2; exit 2; }

outside="$(mktemp -d "${TMPDIR:-/tmp}/maquestlink-clean-unity.XXXXXX")"
project="$outside/MetaXRMinimal"
trap 'rm -rf "$outside"' EXIT

mkdir -p "$project"
rsync -a \
  --exclude Library --exclude Logs --exclude Temp --exclude UserSettings --exclude TestResults.xml \
  "$ROOT_DIR/samples/MetaXRMinimal/" "$project/"
rm -f "$project/Packages/packages-lock.json"
sed -i '' \
  "s|\"com.maquestlink.editor\": \"file:[^\"]*\"|\"com.maquestlink.editor\": \"file:$tarball\"|" \
  "$project/Packages/manifest.json"
if [[ -d "$META_CORE_LOCAL" ]]; then
  sed -i '' \
    "s|\"com.meta.xr.sdk.core\": \"203.0.0\"|\"com.meta.xr.sdk.core\": \"file:$META_CORE_LOCAL\"|" \
    "$project/Packages/manifest.json"
fi

"$UNITY" \
  -batchmode -nographics -quit \
  -projectPath "$project" \
  -executeMethod MaQuestLink.Sample.Editor.SampleBuilder.ConfigureAndBuild \
  -logFile "$outside/build.log"

"$UNITY" \
  -batchmode -nographics \
  -projectPath "$project" \
  -runTests -testPlatform EditMode \
  -testFilter MaQuestLink.Editor.Tests \
  -testResults "$outside/editmode-results.xml" \
  -logFile "$outside/editmode.log"

open -a "$SIM_APP" >/dev/null 2>&1
XR_RUNTIME_JSON="$RUNTIME_JSON" \
MAQUESTLINK_ENABLE_API_LAYER=1 \
  "$UNITY" \
  -batchmode \
  -projectPath "$project" \
  -runTests -testPlatform PlayMode \
  -testFilter MaQuestLink.Sample.Tests.SamplePlayModeTests \
  -testResults "$outside/playmode-results.xml" \
  -logFile "$outside/playmode.log"

grep -q 'result="Passed"' "$outside/editmode-results.xml"
grep -q 'result="Passed"' "$outside/playmode-results.xml"
grep -q 'MAQUESTLINK_SAMPLE_PLAY_VERIFIED layer=loaded status=waiting_for_connection' "$outside/playmode.log"
grep -q 'loaded instance for' "$project/Library/MaQuestLink/layer.log"

package_json="$(find "$project/Library/PackageCache" -path '*com.maquestlink.editor*/package.json' -print -quit)"
[[ -n "$package_json" ]] || { echo "PackageCacheにMaQuestLink packageがありません" >&2; exit 1; }
package_root="$(dirname "$package_json")"
"$ROOT_DIR/scripts/doctor.sh" --package-root "$package_root" --register

echo "Phase 7 clean UPM tarball Unity/Simulator E2E passed"
echo "Temporary logs were verified under: $outside"
