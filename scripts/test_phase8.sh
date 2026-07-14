#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j "${MAQUESTLINK_BUILD_JOBS:-8}"
ctest --test-dir build --output-on-failure
scripts/test_phase1.sh
scripts/test_phase2.sh
scripts/test_phase3.sh
scripts/test_quest_client.sh
scripts/build_quest_client.sh
scripts/test_phase5.sh
scripts/build_release.sh
scripts/test_phase7.sh
scripts/test_phase7_clean.sh

set +e
scripts/e2e_device.sh
device_result=$?
set -e
if [[ "$device_result" -ne 0 && "$device_result" -ne 2 ]]; then
  exit "$device_result"
fi
if [[ "$device_result" -eq 2 ]]; then
  echo "Quest未接続のためdevice E2Eのみ保留しました"
fi

echo "Phase 0-8 regression passed (device result: $device_result)"
