#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
RESULTS="$ROOT_DIR/quest-client/TestResults.xml"
LOG="$ROOT_DIR/quest-client/Builds/test.log"
META_CORE_LOCAL="${METALQUESTLINK_META_CORE_LOCAL:-/private/tmp/meta-xr-core-203-full/package}"
MANIFEST="$ROOT_DIR/quest-client/Packages/manifest.json"
LOCK="$ROOT_DIR/quest-client/Packages/packages-lock.json"
MANIFEST_BACKUP="$ROOT_DIR/quest-client/Builds/test-manifest.json"
LOCK_BACKUP="$ROOT_DIR/quest-client/Builds/test-packages-lock.json"

cleanup() {
  [[ -f "$MANIFEST_BACKUP" ]] && cp "$MANIFEST_BACKUP" "$MANIFEST"
  [[ -f "$LOCK_BACKUP" ]] && cp "$LOCK_BACKUP" "$LOCK"
  sleep 2
  [[ -f "$MANIFEST_BACKUP" ]] && cp "$MANIFEST_BACKUP" "$MANIFEST"
  [[ -f "$LOCK_BACKUP" ]] && cp "$LOCK_BACKUP" "$LOCK"
}
trap cleanup EXIT

mkdir -p "$(dirname "$LOG")"
if [[ -d "$META_CORE_LOCAL" ]]; then
  cp "$MANIFEST" "$MANIFEST_BACKUP"
  cp "$LOCK" "$LOCK_BACKUP"
  sed -i '' \
    "s|\"com.meta.xr.sdk.core\": \"203.0.0\"|\"com.meta.xr.sdk.core\": \"file:$META_CORE_LOCAL\"|" \
    "$MANIFEST"
fi
"$UNITY" \
  -batchmode \
  -nographics \
  -projectPath "$ROOT_DIR/quest-client" \
  -runTests \
  -testPlatform EditMode \
  -testResults "$RESULTS" \
  -logFile "$LOG"

rg 'result="Passed"' "$RESULTS" >/dev/null
echo "Quest client EditMode tests passed: $RESULTS"
