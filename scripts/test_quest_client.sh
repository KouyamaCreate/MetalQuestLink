#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
RESULTS="$ROOT_DIR/quest-client/TestResults.xml"
LOG="$ROOT_DIR/quest-client/Builds/test.log"

mkdir -p "$(dirname "$LOG")"
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
