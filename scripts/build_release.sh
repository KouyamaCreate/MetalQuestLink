#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PACKAGE_DIR="$ROOT_DIR/editor-package"
DIST_DIR="${METALQUESTLINK_DIST_DIR:-$ROOT_DIR/dist}"
BUILD_DIR="${METALQUESTLINK_BUILD_DIR:-$ROOT_DIR/build}"
PACKAGE_JSON="$PACKAGE_DIR/package.json"

version="$(sed -nE 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' "$PACKAGE_JSON" | head -1)"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]]; then
  echo "package.json のversionがsemverではありません: $version" >&2
  exit 2
fi
if [[ "$(tr -d '[:space:]' < "$PACKAGE_DIR/VERSION")" != "$version" ]]; then
  echo "editor-package/VERSIONとpackage.jsonのversionが一致しません" >&2
  exit 2
fi
if ! grep -q "PlayerSettings\.bundleVersion = \"${version}\"" \
  "$ROOT_DIR/quest-client/Assets/MetalQuestLink/Editor/BuildQuestClient.cs"; then
  echo "Quest APKのbundleVersionがpackage versionと一致しません: $version" >&2
  exit 2
fi

exact_tag="$(git -C "$ROOT_DIR" describe --tags --exact-match HEAD 2>/dev/null || true)"
if [[ -n "$exact_tag" && "$exact_tag" != "v$version" ]]; then
  echo "現在のgit tag ($exact_tag) はpackage version (v$version) と一致しません" >&2
  exit 2
fi

for command in cmake codesign file shasum tar; do
  command -v "$command" >/dev/null || {
    echo "必要なcommandがありません: $command" >&2
    exit 2
  }
done

if [[ "${METALQUESTLINK_SKIP_NATIVE_BUILD:-0}" != "1" ]]; then
  cmake -S "$ROOT_DIR" -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release
  cmake --build "$BUILD_DIR" --config Release -j "${METALQUESTLINK_BUILD_JOBS:-8}"
  ctest --test-dir "$BUILD_DIR" --output-on-failure
fi

if [[ "${METALQUESTLINK_SKIP_APK_BUILD:-0}" != "1" ]]; then
  "$ROOT_DIR/scripts/build_quest_client.sh"
fi

layer_source="$BUILD_DIR/layer/libmetalquestlink_openxr_layer.so"
apk_source="$ROOT_DIR/quest-client/Builds/MetalQuestLink.apk"
[[ -s "$layer_source" ]] || { echo "native layerがありません: $layer_source" >&2; exit 2; }
[[ -s "$apk_source" ]] || { echo "Quest APKがありません: $apk_source" >&2; exit 2; }
file "$layer_source" | grep -Eq 'Mach-O .* arm64' || {
  echo "native layerがarm64 Mach-Oではありません: $layer_source" >&2
  exit 2
}

mkdir -p "$PACKAGE_DIR/Native~/macOS" "$PACKAGE_DIR/QuestClient~" "$DIST_DIR"
install -m 755 "$layer_source" "$PACKAGE_DIR/Native~/macOS/libmetalquestlink_openxr_layer.so"
codesign --force --sign - "$PACKAGE_DIR/Native~/macOS/libmetalquestlink_openxr_layer.so"
codesign --verify --strict "$PACKAGE_DIR/Native~/macOS/libmetalquestlink_openxr_layer.so"
install -m 644 "$apk_source" "$PACKAGE_DIR/QuestClient~/MetalQuestLink.apk"

stage="$(mktemp -d "${TMPDIR:-/tmp}/metalquestlink-release.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/package"
COPYFILE_DISABLE=1 cp -R "$PACKAGE_DIR/." "$stage/package/"
xattr -cr "$stage/package" 2>/dev/null || true

tarball="com.metalquestlink.editor-${version}.tgz"
apk="MetalQuestLink-${version}.apk"
rm -f "$DIST_DIR/$tarball" "$DIST_DIR/$apk" "$DIST_DIR/SHA256SUMS" "$DIST_DIR/VERSION"
COPYFILE_DISABLE=1 tar -czf "$DIST_DIR/$tarball" -C "$stage" package
install -m 644 "$apk_source" "$DIST_DIR/$apk"
printf '%s\n' "$version" > "$DIST_DIR/VERSION"
(
  cd "$DIST_DIR"
  shasum -a 256 "$tarball" "$apk" VERSION > SHA256SUMS
  shasum -a 256 -c SHA256SUMS
)

echo "MetalQuestLink release $version"
echo "  UPM: $DIST_DIR/$tarball"
echo "  APK: $DIST_DIR/$apk"
echo "  checksum: $DIST_DIR/SHA256SUMS"
echo "  version: $DIST_DIR/VERSION"
if [[ -z "$exact_tag" ]]; then
  echo "注意: 公開releaseではcommitに v$version tagを付けてください"
fi
