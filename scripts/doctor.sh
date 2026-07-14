#!/usr/bin/env bash
set -u

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PACKAGE_ROOT="$ROOT_DIR/editor-package"
REGISTER=0
STRICT=0

usage() {
  echo "usage: scripts/doctor.sh [--package-root PATH] [--register] [--strict]"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-root) PACKAGE_ROOT="${2:-}"; shift 2 ;;
    --register) REGISTER=1; shift ;;
    --strict) STRICT=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "不明なoptionです: $1" >&2; usage >&2; exit 2 ;;
  esac
done

errors=0
warnings=0
ok() { echo "[OK] $*"; }
warn() { echo "[警告] $*"; warnings=$((warnings + 1)); }
fail() { echo "[エラー] $*"; errors=$((errors + 1)); }

PACKAGE_ROOT="$(cd "$PACKAGE_ROOT" 2>/dev/null && pwd)" || {
  echo "[エラー] package directoryがありません: $PACKAGE_ROOT" >&2
  exit 1
}
layer="$PACKAGE_ROOT/Native~/macOS/libmaquestlink_openxr_layer.so"
bundled_manifest="$PACKAGE_ROOT/Native~/macOS/XrApiLayer_maquestlink.json"
apk="$PACKAGE_ROOT/QuestClient~/MaQuestLink.apk"
package_json="$PACKAGE_ROOT/package.json"
version_file="$PACKAGE_ROOT/VERSION"

arch="$(uname -m)"
[[ "$arch" == "arm64" ]] && ok "Apple Silicon (arm64)" || fail "Apple Siliconが必要です（現在: $arch）"
os_version="$(sw_vers -productVersion 2>/dev/null || echo unknown)"
os_major="${os_version%%.*}"
if [[ "$os_major" =~ ^[0-9]+$ && "$os_major" -ge 14 ]]; then
  ok "macOS $os_version"
else
  fail "macOS 14以降が必要です（現在: $os_version）"
fi

if [[ -s "$layer" ]]; then
  if file "$layer" | grep -Eq 'Mach-O .* arm64'; then
    ok "arm64 OpenXR layer: $layer"
  else
    fail "OpenXR layerがarm64 Mach-Oではありません: $layer"
  fi
  codesign --verify --strict "$layer" >/dev/null 2>&1 \
    && ok "OpenXR layerのad-hoc署名" \
    || fail "OpenXR layerの署名検証に失敗しました"
  if xattr -p com.apple.quarantine "$layer" >/dev/null 2>&1; then
    warn "OpenXR layerにquarantine属性があります。READMEのGatekeeper手順を実行してください"
  fi
else
  fail "packageにOpenXR layerがありません: $layer"
fi
[[ -s "$bundled_manifest" ]] \
  && ok "同梱layer manifest" \
  || fail "同梱layer manifestがありません: $bundled_manifest"
[[ -s "$apk" ]] && ok "同梱Quest APK: $apk" || fail "同梱Quest APKがありません: $apk"

package_version="$(sed -nE 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' "$package_json" 2>/dev/null | head -1)"
file_version="$(tr -d '[:space:]' < "$version_file" 2>/dev/null || true)"
if [[ -n "$package_version" && "$package_version" == "$file_version" ]]; then
  ok "package version: $package_version"
else
  fail "package.jsonとVERSIONが一致しません ($package_version / $file_version)"
fi

aapt="${AAPT:-}"
if [[ -z "$aapt" ]]; then
  shopt -s nullglob
  aapt_candidates=(
    /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/build-tools/*/aapt
  )
  for candidate in "${aapt_candidates[@]}"; do
    [[ -x "$candidate" ]] && aapt="$candidate"
  done
fi
if [[ -n "$aapt" && -x "$aapt" && -s "$apk" ]]; then
  apk_badging="$($aapt dump badging "$apk" 2>/dev/null | head -1)"
  apk_package="$(printf '%s\n' "$apk_badging" | sed -nE "s/.*package: name='([^']+)'.*/\1/p")"
  apk_version="$(printf '%s\n' "$apk_badging" | sed -nE "s/.*versionName='([^']+)'.*/\1/p")"
  [[ "$apk_package" == "com.maquestlink.questclient" ]] \
    && ok "Quest APK package: $apk_package" \
    || fail "Quest APK package名が不正です: $apk_package"
  [[ "$apk_version" == "$package_version" ]] \
    && ok "同梱Quest APK version: $apk_version" \
    || fail "同梱Quest APK versionが不一致です（APK: $apk_version / package: $package_version）"
else
  warn "aaptが見つからないため、同梱APKのversion検査を省略しました"
fi

manifest_dir="${MAQUESTLINK_MANIFEST_DIR:-$HOME/.local/share/openxr/1/api_layers/implicit.d}"
manifest="$manifest_dir/XrApiLayer_maquestlink.json"
if [[ "$REGISTER" == "1" && -s "$layer" ]]; then
  escaped_layer="${layer//\\/\\\\}"
  escaped_layer="${escaped_layer//\"/\\\"}"
  register_tmp="${TMPDIR:-/tmp}/XrApiLayer_maquestlink.$$.json"
  if mkdir -p "$manifest_dir" 2>/dev/null \
    && sed "s|\"library_path\": \"libmaquestlink_openxr_layer.so\"|\"library_path\": \"$escaped_layer\"|" \
      "$bundled_manifest" > "$register_tmp" \
    && install -m 644 "$register_tmp" "$manifest" 2>/dev/null; then
    ok "layer manifestを登録しました: $manifest"
  else
    fail "layer manifestを登録できませんでした: $manifest"
  fi
  rm -f "$register_tmp"
fi
if [[ -s "$manifest" ]] && grep -Fq "\"library_path\": \"$layer\"" "$manifest"; then
  ok "implicit layer登録先: $manifest"
else
  fail "implicit layerがこのpackageへ登録されていません。Unityでpackageを読み直すか --register を実行してください"
fi

sim_app="${MAQUESTLINK_SIM_APP:-/Applications/MetaXRSimulator.app}"
if [[ -f "$sim_app/Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json" ]]; then
  ok "Meta XR Simulator: $sim_app"
else
  fail "Meta XR Simulatorが見つかりません: $sim_app"
fi
pgrep -f '/MetaXRSimulator.app/' >/dev/null 2>&1 \
  && ok "Meta XR Simulatorは起動中です" \
  || warn "Meta XR Simulatorは停止中です。Play前に起動してください"

adb="${ADB:-}"
if [[ -z "$adb" ]]; then
  shopt -s nullglob
  adb_candidates=(
    /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
    /opt/homebrew/bin/adb
    /usr/local/bin/adb
  )
  for candidate in "${adb_candidates[@]}"; do
    [[ -x "$candidate" ]] && adb="$candidate"
  done
fi
if [[ -n "$adb" && -x "$adb" ]]; then
  ok "adb: $adb"
  device_lines="$($adb devices 2>/dev/null | awk 'NR > 1 && $2 == "device" { print $1 }')"
  device_count="$(printf '%s\n' "$device_lines" | awk 'NF { count++ } END { print count + 0 }')"
  if [[ "$device_count" -eq 1 ]]; then
    ok "Quest/Android device接続: $device_lines"
    installed_version="$($adb shell dumpsys package com.maquestlink.questclient 2>/dev/null | sed -nE 's/.*versionName=([^[:space:]]+).*/\1/p' | head -1)"
    if [[ -z "$installed_version" ]]; then
      warn "QuestにMaQuestLink APKが未installです。Unity windowからinstallしてください"
    elif [[ "$installed_version" == "$package_version" ]]; then
      ok "Quest APK version: $installed_version"
    else
      fail "Quest APK versionが不一致です（端末: $installed_version / package: $package_version）"
    fi
  elif [[ "$device_count" -eq 0 ]]; then
    warn "Questが接続されていません。実機診断は接続後に再実行してください"
  else
    fail "deviceが複数あります。Questを1台だけ接続してください"
  fi
else
  fail "adbが見つかりません。Unity Android Build Supportをinstallしてください"
fi

echo "doctor結果: error=$errors warning=$warnings"
if [[ "$errors" -gt 0 || ("$STRICT" == "1" && "$warnings" -gt 0) ]]; then
  exit 1
fi
exit 0
