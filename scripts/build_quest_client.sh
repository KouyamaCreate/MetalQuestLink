#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
LOG="${MAQUESTLINK_UNITY_LOG:-$ROOT_DIR/quest-client/Builds/build.log}"

mkdir -p "$(dirname "$LOG")"
"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$ROOT_DIR/quest-client" \
  -buildTarget Android \
  -executeMethod MaQuestLink.QuestClient.Editor.BuildQuestClient.Build \
  -logFile "$LOG"

test -s "$ROOT_DIR/quest-client/Builds/MaQuestLink.apk"
echo "MaQuestLink APK: $ROOT_DIR/quest-client/Builds/MaQuestLink.apk"
