# 実装進捗

最終更新: 2026-07-15

| Phase | 状態 | 検証 |
|---|---|---|
| 0 — リポジトリ基盤とプロトコル | 完了 | build成功、ctest 1/1成功、arm64確認 |
| 1 — 技術検証スパイク | 完了 | clean build成功、Simulator Metal E2E 120 frames成功、layer load確認 |
| 2 — 映像パイプライン | 完了 | H.264 decode 120/120・66.8345 fps、未接続60 frames完走 |
| 3 — 入力パイプライン | 完了 | view/space/action合成入力E2E成功、PoseInput 138 samples |
| 4 — Questクライアント | 未着手 | — |
| 5 — Unityエディタ統合 | 未着手 | — |
| 6 — 再投影・計測・ドキュメント | 未着手 | — |
| 7 — 配布パッケージング | 未着手 | — |

## Phase 0

- 成果物: CMakeスキャフォールド、共有バイナリプロトコル、単体テスト
- 検証コマンド: `cmake -B build && cmake --build build`
  - 結果: 成功。`maquestlink_protocol` と `maquestlink_protocol_tests` をビルドした。
- 検証コマンド: `ctest --test-dir build --output-on-failure`
  - 結果: 成功。1/1 tests passed。
- アーキテクチャ確認: `lipo -archs build/layer/maquestlink_protocol_tests`
  - 結果: `arm64`。
- 判明した事実: AppleClang 17.0.0、cmake 4.3.1のローカル環境で受け入れ基準を満たした。

## Phase 1

- 成果物:
  - Meta XR Simulator Standalone v201.0を `/Applications/MetaXRSimulator.app` に導入
  - Khronos OpenXR-SDK 1.1.61固定のloader build
  - Metal session、stereo swapchain、frame loopを実行するnative test client
  - OpenXR loader interface v1対応のpass-through API layerとimplicit manifest
  - `scripts/test_phase1.sh` による再実行可能なE2E
- clean build:
  - `cmake -S . -B /private/tmp/maquestlink-phase1-clean-20260715`
  - `cmake --build /private/tmp/maquestlink-phase1-clean-20260715 --parallel`
  - 結果: 成功。native clientとlayerはいずれもarm64。
- regression: `ctest --test-dir build --output-on-failure`
  - 結果: 成功。1/1 tests passed。
- native E2E: `scripts/test_phase1.sh`
  - 結果: 成功。Meta XR Simulator 201.0.0、`XR_KHR_metal_enable`、Apple M4 Pro Metal deviceを確認。
  - 120フレームのstereo swapchain描画と `xrEndFrame` が完了。
  - loader logで `XR_APILAYER_MAQUESTLINK_streaming` のロードを確認。
  - layer固有ログでnegotiate、instance load、destroyを確認。
- 詳細な実測結果: `docs/notes.md` の「Phase 1」を参照。

## Phase 2

- 成果物:
  - OpenXR session/swapchain追跡と `xrEndFrame` hook
  - Metalによる左右眼side-by-side copyとVideoToolbox H.264 low-latency encoder
  - host monotonic timestamp、左右眼pose/FOV付きVideoFrameのTCP送信
  - VideoToolboxで実デコードする `maquestlink_mock_viewer`
  - 未接続/接続の両経路を検証する `scripts/test_phase2.sh`
- build/regression:
  - `cmake --build build -j8`
  - `ctest --test-dir build --output-on-failure`
  - 結果: 成功。protocol test 1/1 passed。
- video E2E: `scripts/test_phase2.sh`
  - 結果: 成功。mock viewerが3360x1760 H.264を120/120 framesデコード、66.8345 fps。
  - producerは240-frame OpenXR loopを完走。120-frame時点で平均copy 4.02939 ms、平均encode 16.1017 ms。
  - viewer未接続の独立runは60-frame loopを完走し、映像処理を行わないpass-throughを確認。
- 詳細な実測結果: `docs/notes.md` の「Phase 2」を参照。

## Phase 3

- 成果物:
  - lifecycle管理された全二重TCP transportと最新PoseInput store
  - `xrLocateViews` / `xrLocateSpace` のHMD・controller pose注入
  - action set/action/subaction/binding mapping
  - `xrSyncActions` / `xrGetActionStateBoolean` / `Float` / `Vector2f` / `Pose` hook
  - click/touch/analogのprotocol v1 semantic定義
  - 合成入力送信mock clientと `scripts/test_phase3.sh`
- build/regression:
  - `cmake --build build -j8`
  - `ctest --test-dir build --output-on-failure`
  - 結果: 成功。protocol test 1/1 passed。
- input E2E: `scripts/test_phase3.sh`
  - 結果: 成功。合成PoseInput 138 samplesを送信。
  - `xrLocateViews`、`xrLocateSpace`、boolean/float/vector action stateの期待値一致をnative client内で確認。
  - OpenXR 1.1の現行Touch Plus profile `/interaction_profiles/meta/touch_plus_controller` をSimulatorで有効化した。
  - 同時映像は3360x1760 H.264を120/120 frames、76.4866 fpsでdecode。
- Phase 2 regression: `scripts/test_phase2.sh`
  - 結果: 成功。未接続pass-throughと接続映像decodeの両方を再確認。
- 詳細な実測結果: `docs/notes.md` の「Phase 3」を参照。
