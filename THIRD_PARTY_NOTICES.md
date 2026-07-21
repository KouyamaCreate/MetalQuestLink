# Third-party software and trademarks

MetalQuestLink's Apache-2.0 license applies only to project-authored source code and documentation.
Third-party software, SDKs, generated binaries, media, and trademarks remain subject to their own
licenses and terms.

## Build and runtime dependencies

The project integrates with or resolves components from these upstream projects and platforms:

- [Khronos OpenXR SDK](https://github.com/KhronosGroup/OpenXR-SDK), fetched at the revision recorded
  in the root CMake configuration;
- Unity Editor and Unity packages, including XR Plug-in Management, OpenXR, Test Framework, and
  Unity Android modules;
- Meta XR Simulator, Meta XR Core SDK, Meta OpenXR packages, and Meta Quest platform software;
- Apple Metal and VideoToolbox platform frameworks;
- Android platform tools, NDK, MediaCodec, and related platform APIs;
- GitHub Actions used by the repository, including the optional GameCI Unity Builder workflow.

Some dependencies are downloaded from their vendor registries during development and are not
relicensed by this repository. The prebuilt Quest APK and UPM release package may contain
redistributable object code produced by those toolchains. Review the applicable vendor terms and
the notices produced by the build toolchain before redistributing modified binaries.

## Trademarks and affiliation

Meta, Meta Quest, OpenXR, Unity, Apple, Metal, Android, GitHub, OpenAI, and other names are the
property of their respective owners. Their use describes compatibility only. MetalQuestLink is not
affiliated with, endorsed by, or sponsored by those organizations.
