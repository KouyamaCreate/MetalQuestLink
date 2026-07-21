# OpenAI Build Week submission worksheet

締切: 2026-07-22 00:00 UTC（日本時間 2026-07-22 09:00）

この文面は事実確認用の草稿。Devpostへ最終提出する前に、作者本人の言葉へ整え、実際のURL・動画・
Session IDを埋める。

## Project

- Name: MetalQuestLink
- Track: Developer Tools
- Tagline: Stream Unity Editor Play Mode to Quest through Metal—without rebuilding Android every iteration.
- Built with: Codex, GPT-5.6, Unity, OpenXR, Meta Quest, VideoToolbox, MediaCodec, C++, C#
- Draft URL: https://devpost.com/software/maquestlink
- Repository: https://github.com/KouyamaCreate/MetalQuestLink
- Project state: Published on Devpost; not yet submitted to OpenAI Build Week
- Slug note: 表示名は`MetalQuestLink`へ更新済み。Devpost connectorでは既存slugを変更できないため、
  URLだけ旧slugの`maquestlink`が残る。必要ならDevpost UIで手動変更する。

## Description draft

### Inspiration

Quest developers repeatedly wait for Android builds just to validate small XR scene and interaction
changes. MetalQuestLink moves that feedback loop into Unity Editor Play Mode on an Apple Silicon Mac.

### What it does

MetalQuestLink intercepts Unity's OpenXR frames through an implicit API layer, encodes stereo images
with VideoToolbox, and streams them to a prebuilt Quest client. The headset submits the received
video as a stereo projection layer and sends HMD, Touch controller, and hand-tracking input back to
the Editor. It also forwards haptics and offers a documented passthrough preview approximation.

### How we built it

The macOS side is a C++/Objective-C++ OpenXR API layer using Metal and VideoToolbox. A binary
protocol carries video metadata, poses, controller state, 26 joints per hand, clock sync, and haptic
commands. The Quest client uses Unity, MediaCodec, and a native OpenXR projection extension. A UPM
Editor package bundles the native layer and APK, configures Standalone OpenXR, runs preflight checks,
and exposes a one-click setup flow.

### How Codex and GPT-5.6 were used

Codex with GPT-5.6 was used throughout the repository work: understanding the existing native and
Unity integration, tracing version-coupled assumptions, implementing the compatibility matrix and
quick setup flow, writing regression tests, and keeping setup, specification, and submission docs
aligned. Key architectural decisions and measurements remain documented and human-verified in the
repository.

### Challenges

The two devices use different monotonic clock epochs, OpenXR projects vary in loader order and
swapchain layout, and the low-latency path must avoid accumulating encoder backlog. The project
addresses these with ping/pong clock estimation, non-destructive loader ordering, support for array
and per-eye swapchains, and bounded pending frames with observable drops.

### Accomplishments

- Prebuilt tarball-to-Play installation; judges do not rebuild the native layer or Quest APK.
- First-person stereo projection rather than a finite in-world video quad.
- Bidirectional HMD, controller, hand, and haptic protocol.
- Self-contained release, checksums, doctor, clean-room tarball test, and Unity version matrix.
- Quest 3 E2E measurements up to 74 receive fps, 76 decode fps, and 73 Hz pose samples.

### What's next

Expand the verified Unity matrix, add CI-backed package tests, improve notarized distribution,
measure long-running optical latency and quality, and grow support through community issues and pull
requests.

## Required fields still needing owner input

- [ ] Confirm eligibility: “Above legal age of majority in country of residence” (Japan is included in the eligible country list)
- [x] Public repository URL: https://github.com/KouyamaCreate/MetalQuestLink
- [ ] Confirm Apache-2.0 is the intended public license
- [ ] Public YouTube demo URL under 3 minutes
- [ ] `/feedback` Session ID for the main build task
- [ ] Confirm submitter type and country of residence
- [x] Project name decided by the owner: `MetalQuestLink`
- [ ] Final first-person edit of the description
