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
