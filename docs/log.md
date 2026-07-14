# 変更履歴

## 2026-07-15

### 変更

- Apple Silicon arm64向けCMakeプロジェクトを作成した。
- 映像、pose/input、controlの共有バイナリプロトコルv1と単体テストを追加した。
- Phase単位の進捗記録を初期化した。

### 理由

- Phase 0の受け入れ基準を満たし、後続のMac側・Quest側実装で同じwire formatを使うため。

### 影響

- `CMakeLists.txt`
- `layer/`
- `docs/`

### 残り

- Phase 1以降は `docs/plan.md` と `docs/progress.md` に従って実装する。

### Phase 1 変更

- Standalone Meta XR Simulator v201.0で動くMetal native test clientを追加した。
- OpenXR loader interface v1対応のpass-through API layerとimplicit manifestを追加した。
- OpenXR-SDK 1.1.61をFetchContentで固定した。
- `scripts/test_phase1.sh` でSimulator接続、120フレーム描画、layer loadを自動検証できるようにした。

### Phase 1 理由

- 映像・入力hookへ進む前に、macOS上でMetal OpenXR sessionとAPI layer方式が成立する最大リスクを解消するため。

### Phase 1 影響

- `CMakeLists.txt`
- `layer/CMakeLists.txt`
- `layer/native-test/`
- `layer/src/openxr_layer.*`
- `scripts/test_phase1.sh`
- `README.md`、`docs/`

### Phase 1 残り

- Phase 2で `xrEndFrame` とswapchain imageを追跡し、VideoToolbox/TCP送信を実装する。
- OVRPluginが要求する拡張一覧はUnity側を起動するPhase 1〜3の後続実測で記録する。

### Phase 2 変更

- session/swapchainとMetal textureを追跡して `xrEndFrame` から左右眼映像を取得するhookを追加した。
- VideoToolbox H.264 low-latency encoderとloopback TCP serverを追加した。
- VideoFrameを左右眼それぞれのrender pose/FOVを持つ形式へ拡張した。
- H.264を実デコードしてfpsとmetadataを検証するmacOS mock viewerを追加した。
- `scripts/test_phase2.sh` に未接続pass-throughと接続loopback E2Eを自動化した。

### Phase 2 理由

- Questクライアント実装前に、最大帯域の映像経路とフレームmetadataをヘッドセットなしで実証するため。

### Phase 2 影響

- `layer/CMakeLists.txt`
- `layer/include/maquestlink/protocol.hpp`
- `layer/src/openxr_layer.cpp`、`layer/src/protocol.cpp`、`layer/src/streaming.*`
- `layer/tools/mock_viewer.mm`、`layer/tests/protocol_tests.cpp`
- `scripts/test_phase2.sh`
- `README.md`、`docs/`

### Phase 2 残り

- Phase 3で同じTCP接続の受信側を追加し、Quest由来のpose/inputをOpenXRへ注入する。
- Unity/OVRPlugin固有のswapchain構成はPhase 5で実測する。

### Phase 3 変更

- TCP transportを全二重化し、PoseInput受信、切断処理、instance lifecycleに連動したthread停止を追加した。
- view/reference/action spaceとaction/bindingを追跡するOpenXR input injection hookを追加した。
- protocol v1にclick 4種とcapacitive touch 4種のbutton bit semanticを定義した。
- mock viewerに約90 Hzの合成入力送信を追加し、native testにview/space/action照合を追加した。
- `scripts/test_phase3.sh` で映像と入力を同時検証できるようにした。

### Phase 3 理由

- Questクライアントより先に、実機から届く予定のpose/button/analog値をOpenXR applicationへ差し替えられることをヘッドセットなしで実証するため。

### Phase 3 影響

- `layer/include/maquestlink/protocol.hpp`
- `layer/src/input_injection.*`、`layer/src/transport.*`
- `layer/src/openxr_layer.cpp`、`layer/src/streaming.mm`
- `layer/native-test/hello_xr_metal.mm`、`layer/tools/mock_viewer.mm`
- `scripts/test_phase3.sh`
- `README.md`、`docs/`

### Phase 3 残り

- Phase 4でQuest native/Unity側から実際のHMD/controller入力を60 Hz以上で送る。
- OVRPlugin要求拡張一覧とUnity action bindingはPhase 5のEditor Play modeで実測する。
