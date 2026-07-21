#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.6f1/Unity.app/Contents/MacOS/Unity}"
UNITY_APP="${UNITY%/Contents/MacOS/Unity}"
UNITY_ROOT="${UNITY_APP%/Unity.app}"
NDK="$UNITY_ROOT/PlaybackEngines/AndroidPlayer/NDK"
BUILD_DIR="${METALQUESTLINK_PROJECTION_BUILD_DIR:-$ROOT_DIR/build-android-projection}"
OUTPUT_DIR="$ROOT_DIR/quest-client/Assets/Plugins/Android/arm64-v8a"

[[ -f "$NDK/build/cmake/android.toolchain.cmake" ]] || {
  echo "Unity Android NDKがありません: $NDK" >&2
  exit 2
}

OPENXR_SOURCE="${METALQUESTLINK_OPENXR_SOURCE_DIR:-}"
if [[ -z "$OPENXR_SOURCE" && -f "$ROOT_DIR/build/CMakeCache.txt" ]]; then
  OPENXR_SOURCE="$(sed -n 's|^FETCHCONTENT_SOURCE_DIR_OPENXR_SDK:PATH=||p' "$ROOT_DIR/build/CMakeCache.txt" | head -1)"
fi
if [[ -z "$OPENXR_SOURCE" || ! -f "$OPENXR_SOURCE/include/openxr/openxr.h" ]]; then
  OPENXR_SOURCE="$ROOT_DIR/build/_deps/openxr_sdk-src"
fi
if [[ ! -f "$OPENXR_SOURCE/include/openxr/openxr.h" ]]; then
  cmake -S "$ROOT_DIR" -B "$ROOT_DIR/build" -DCMAKE_BUILD_TYPE=Release
  OPENXR_SOURCE="$(sed -n 's|^FETCHCONTENT_SOURCE_DIR_OPENXR_SDK:PATH=||p' "$ROOT_DIR/build/CMakeCache.txt" | head -1)"
  [[ -n "$OPENXR_SOURCE" ]] || OPENXR_SOURCE="$ROOT_DIR/build/_deps/openxr_sdk-src"
fi
[[ -f "$OPENXR_SOURCE/include/openxr/openxr.h" ]] || {
  echo "OpenXR headersがありません: $OPENXR_SOURCE" >&2
  exit 2
}

cmake -S "$ROOT_DIR/quest-client/NativeProjection" -B "$BUILD_DIR" \
  -DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a \
  -DANDROID_PLATFORM=android-32 \
  -DANDROID_STL=c++_shared \
  -DCMAKE_BUILD_TYPE=Release \
  -DOPENXR_INCLUDE_DIR="$OPENXR_SOURCE/include"
cmake --build "$BUILD_DIR" --config Release -j "${METALQUESTLINK_BUILD_JOBS:-8}"
mkdir -p "$OUTPUT_DIR"
install -m 755 "$BUILD_DIR/libmetalquestlink_projection.so" "$OUTPUT_DIR/libmetalquestlink_projection.so"
file "$OUTPUT_DIR/libmetalquestlink_projection.so" | grep -q 'ELF 64-bit LSB shared object, ARM aarch64'
echo "MetalQuestLink immersive projection: $OUTPUT_DIR/libmetalquestlink_projection.so"
