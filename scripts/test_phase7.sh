#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
version="$(tr -d '[:space:]' < "$ROOT_DIR/editor-package/VERSION")"
DIST_DIR="${MAQUESTLINK_DIST_DIR:-$ROOT_DIR/dist}"

MAQUESTLINK_SKIP_NATIVE_BUILD=1 \
MAQUESTLINK_SKIP_APK_BUILD=1 \
  "$ROOT_DIR/scripts/build_release.sh"

tarball="$DIST_DIR/com.maquestlink.editor-${version}.tgz"
apk="$DIST_DIR/MaQuestLink-${version}.apk"
[[ -s "$tarball" && -s "$apk" && -s "$DIST_DIR/SHA256SUMS" && -s "$DIST_DIR/VERSION" ]]
(
  cd "$DIST_DIR"
  shasum -a 256 -c SHA256SUMS
)

outside="$(mktemp -d "${TMPDIR:-/tmp}/maquestlink-clean-package.XXXXXX")"
trap 'rm -rf "$outside"' EXIT
COPYFILE_DISABLE=1 tar -xzf "$tarball" -C "$outside"
[[ -s "$outside/package/Native~/macOS/libmaquestlink_openxr_layer.so" ]]
[[ -s "$outside/package/Native~/macOS/XrApiLayer_maquestlink.json" ]]
[[ -s "$outside/package/QuestClient~/MaQuestLink.apk" ]]
if grep -REn '/MaQuestLink/(build/layer|quest-client/Builds)' \
  "$outside/package" --include='*.cs' --include='*.json' --include='*.md'; then
  echo "配布packageにrepository内build成果物への依存があります" >&2
  exit 1
fi

MAQUESTLINK_MANIFEST_DIR="$outside/manifest" \
  "$ROOT_DIR/scripts/doctor.sh" --package-root "$outside/package" --register

echo "Phase 7 release package smoke test passed"
