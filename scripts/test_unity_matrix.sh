#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
read -r -a VERSIONS <<< "${METALQUESTLINK_UNITY_VERSIONS:-2022.3.44f1 6000.2.5f1 6000.3.6f1}"
MATRIX_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/metalquestlink-unity-matrix.XXXXXX")"
trap 'rm -rf "$MATRIX_ROOT"' EXIT

for version in "${VERSIONS[@]}"; do
  unity="/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity"
  if [[ ! -x "$unity" ]]; then
    echo "SKIP Unity ${version}: editor not installed"
    continue
  fi

  project="$MATRIX_ROOT/$version"
  mkdir -p "$project/Assets" "$project/Packages" "$project/ProjectSettings"
  printf '%s\n' \
    '{' \
    '  "dependencies": {' \
    "    \"com.metalquestlink.editor\": \"file:${ROOT_DIR}/editor-package\"," \
    '    "com.unity.test-framework": "1.1.33"' \
    '  },' \
    '  "testables": ["com.metalquestlink.editor"]' \
    '}' > "$project/Packages/manifest.json"
  printf 'm_EditorVersion: %s\n' "$version" > "$project/ProjectSettings/ProjectVersion.txt"

  log="$MATRIX_ROOT/$version.log"
  results="$MATRIX_ROOT/$version-results.xml"
  if ! "$unity" \
      -batchmode -nographics \
      -projectPath "$project" \
      -runTests -testPlatform EditMode \
      -testFilter MetalQuestLink.Editor.Tests.OpenXRLayerInstallerTests.Unity \
      -testResults "$results" \
      -logFile "$log"; then
    tail -80 "$log"
    echo "FAIL Unity ${version}: Unity exited with an error" >&2
    exit 1
  fi

  if [[ ! -f "$results" ]] || ! grep -q 'result="Passed"' "$results"; then
    tail -80 "$log"
    echo "FAIL Unity ${version}" >&2
    exit 1
  fi
  if grep -q 'error CS[0-9]' "$log"; then
    grep 'error CS[0-9]' "$log"
    exit 1
  fi
  echo "PASS Unity ${version}"
done

echo "Unity compatibility matrix passed"
