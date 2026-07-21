#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
version="$(tr -d '[:space:]' < "$ROOT_DIR/editor-package/VERSION")"
DIST_DIR="${METALQUESTLINK_DIST_DIR:-$ROOT_DIR/dist}"

METALQUESTLINK_SKIP_NATIVE_BUILD=1 \
METALQUESTLINK_SKIP_APK_BUILD=1 \
  "$ROOT_DIR/scripts/build_release.sh"

tarball="$DIST_DIR/com.metalquestlink.editor-${version}.tgz"
apk="$DIST_DIR/MetalQuestLink-${version}.apk"
[[ -s "$tarball" && -s "$apk" && -s "$DIST_DIR/SHA256SUMS" && -s "$DIST_DIR/VERSION" ]]
(
  cd "$DIST_DIR"
  shasum -a 256 -c SHA256SUMS
)

outside="$(mktemp -d "${TMPDIR:-/tmp}/metalquestlink-clean-package.XXXXXX")"
trap 'rm -rf "$outside"' EXIT
COPYFILE_DISABLE=1 tar -xzf "$tarball" -C "$outside"
[[ -s "$outside/package/Native~/macOS/libmetalquestlink_openxr_layer.so" ]]
[[ -s "$outside/package/Native~/macOS/XrApiLayer_metalquestlink.json" ]]
[[ -s "$outside/package/QuestClient~/MetalQuestLink.apk" ]]
if grep -REn '/MetalQuestLink/(build/layer|quest-client/Builds)' \
  "$outside/package" --include='*.cs' --include='*.json' --include='*.md'; then
  echo "配布packageにrepository内build成果物への依存があります" >&2
  exit 1
fi

METALQUESTLINK_MANIFEST_DIR="$outside/manifest" \
  "$ROOT_DIR/scripts/doctor.sh" --package-root "$outside/package" --register

echo "Phase 7 release package smoke test passed"
