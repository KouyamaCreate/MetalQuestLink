# Compatibility policy

## Goal

MetalQuestLink avoids coupling installation to one Unity patch release. Compatibility is divided into
three levels:

| Level | Meaning | Preflight behavior |
|---|---|---|
| Verified | Package compile/EditMode matrix passed for that Unity line | Ready |
| Compatible, unverified | Meets the API/package baseline but is not in the matrix | Warning; Play is allowed if capability checks pass |
| Unsupported | Older than Unity 2022.3 or missing a required capability | Error; Play is blocked |

## Current matrix

| Unity | Package compile/EditMode | Simulator/Quest E2E |
|---|---:|---:|
| 2022.3.44f1 | blocked: local Editor license unavailable | not yet run |
| 6000.2.5f1 | matrix target | passed |
| 6000.3.6f1 | matrix target | passed |

The Editor package requests XR Plug-in Management 4.4.0 and OpenXR 1.8.2 as minimum resolver
baselines. A project with newer compatible versions keeps the newer versions. The separately built
Quest client remains pinned so its release APK is reproducible; consumers use the prebuilt APK and
do not inherit that source-project pin.

## Adding a Unity version

1. Install the editor version through Unity Hub.
2. Add it to `scripts/test_unity_matrix.sh`.
3. Run the matrix and relevant Simulator/Quest tests.
4. Update this table, `README.md`, `README.ja.md`, `docs/spec.md`, and `CHANGELOG.md` with the exact
   level actually verified.
