# Changelog

Notable user-facing changes are recorded here. Detailed engineering history remains in
[`docs/log.md`](docs/log.md).

## Unreleased

### Documentation

- Made the root README the canonical English public entry point and moved the complete Japanese
  guide to `README.ja.md`.
- Added public support, third-party notice, repository hygiene, and publishing guidance.

## 0.2.0 - 2026-07-21

### Breaking changes

- Renamed the complete product surface to MetalQuestLink, including C# namespaces, assembly names,
  UPM and Android IDs, OpenXR layer identifiers, environment variables, CMake targets, native
  libraries, APK/tarball names, diagnostics, samples, documentation, and Devpost metadata.
- Changed the wire magic from `MQLK` to `MTLK`; 0.1.x clients and layers cannot be mixed with 0.2.x.
- Existing projects must remove the previous package entry and add
  `com.metalquestlink.editor`; the new Quest APK is installed as a separate Android application.

### Added

- Unity 2022.3 / 6000.2 / 6000.3 package compatibility matrix test.
- One-click **Quick Setup (Project + Quest)** flow.
- English judge quick start and open-source contribution governance.

### Changed

- Editor package baseline changed from Unity 6000.2 to Unity 2022.3 LTS.
- XR package dependencies now express a conservative baseline while preserving newer project
  versions.
- Unverified but baseline-compatible Unity versions warn during preflight instead of being blocked
  solely by version number.

## 0.1.0 - 2026-07-15

- Initial self-contained UPM package and Quest APK.
- OpenXR stereo streaming, HMD/controller/hand input, haptics, passthrough approximation, project
  preflight, diagnostics, and release verification.
