# 実装進捗

最終更新: 2026-07-15

| Phase | 状態 | 検証 |
|---|---|---|
| 0 — リポジトリ基盤とプロトコル | 完了 | build成功、ctest 1/1成功、arm64確認 |
| 1 — 技術検証スパイク | 完了 | clean build成功、Simulator Metal E2E 120 frames成功、layer load確認 |
| 2 — 映像パイプライン | 完了 | H.264 decode 120/120・66.8345 fps、未接続60 frames完走 |
| 3 — 入力パイプライン | 完了 | view/space/action合成入力E2E成功、PoseInput 138 samples |
| 4 — Questクライアント | 完了 | EditMode 4/4成功、Unity 6000.3.6f1でarm64 APK build成功 |
| 5 — Unityエディタ統合 | 完了 | EditMode 1/1、Meta XR Simulator PlayMode 1/1、layer load・接続待ち確認 |
| 6 — 再投影・計測・ドキュメント | 完了 | world pose / clock unit 3/3、clock sync E2E、Phase 0〜5回帰成功 |
| 7 — 配布パッケージング | 完了 | 配布4点・checksum・repository外tarball Unity/Simulator E2E・doctor成功 |
| 8 — Quest機能の拡張対応 | 完了 | haptic / hand / passthrough mock E2E、Quest EditMode 9/9、Phase 0〜7回帰、Quest 3実機E2E成功 |

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

## Phase 4

- 成果物:
  - Unity 6000.3.6f1 / Meta XR Core SDK 203.0.0 / Unity Meta OpenXR 2.5.1のQuest client
  - H.264/HEVC Annex Bを受けるMediaCodec Surface decoderとOVROverlay External Surface SBS表示
  - Quest HMD / Touch Plus pose・button・touch・thumbstick・trigger・gripの72 Hz送信
  - localhost（`adb reverse`）優先、Wi-Fi hostフォールバック付き全二重TCP transport
  - adb extrasで有効化する毎秒JSON診断ログと無装着E2E `scripts/e2e_device.sh`
  - Unity batch test/buildスクリプト
- protocol / transport test:
  - `scripts/test_quest_client.sh`
  - 結果: 成功。EditMode 4/4 passed。VideoFrame / PoseInput / Controlのwire round-trip、invalid frame拒否、映像受信とPoseInput送信の全二重loopbackを確認。
- APK build:
  - `scripts/build_quest_client.sh`
  - 結果: 成功。Unity 6000.3.6f1のbatch mode、IL2CPP、ARM64。APK 42 MiB。
  - `aapt dump badging` でpackage `com.maquestlink.questclient`、version `0.1.0`、minSdk 32、targetSdk 36、`UnityPlayerGameActivity`、`arm64-v8a`、Quest headtracking featureを確認。
  - Quest 3 / 3Sのみを対象とし、Quest Pro専用eye-tracking feature/permissionが入らないことを確認。
- device E2E:
  - `scripts/e2e_device.sh`
  - 現在はQuest 3未接続のため、未接続を検出して手順付きでexit 2することを確認。
  - 実機を接続して `scripts/e2e_device.sh` を実行する。
- 詳細な実測結果: `docs/notes.md` の「Phase 4」を参照。

## Phase 5

- 成果物:
  - local UPM package `com.maquestlink.editor`
  - implicit OpenXR layer自動登録、Play開始前の環境設定、`adb reverse`とQuest client自動起動
  - connection / fps / copy・encode latencyを表示する `Window > MaQuestLink`
  - APK install、client起動、layer再登録の操作ボタン
  - `OVRCameraRig`、左右`OVRGrabber`、`OVRGrabbable` cubeを持つ `samples/MetaXRMinimal`
  - Unity EditMode / PlayModeを連続検証する `scripts/test_phase5.sh`
- native build:
  - `cmake --build build -j8`
  - 結果: 成功。layerに1秒周期のatomic JSON status出力を追加した。
- Editor / PlayMode E2E:
  - `scripts/test_phase5.sh`
  - 結果: EditMode 1/1、PlayMode 1/1 passed。
  - Meta XR Simulator 201.0.0でMetal sessionを作成し、Unity 6000.3.6f1 Editorのlayer loadを確認した。
  - Quest未接続状態で `connected=false` のstatus JSONと `status=waiting_for_connection` logを確認した。
- 新規導入手順:
  - Package Managerで `editor-package/` を導入する。
  - `Window > MaQuestLink` で `Install Quest APK` を1回実行する。
  - Meta XR Simulatorを選択してPlayする。layer登録、adb reverse、client起動は自動実行される。
- 詳細な実測結果: `docs/notes.md` の「Phase 5」を参照。

## Phase 6

- 成果物:
  - VideoFrameのstereo render poseを使うworld-fixed `OVROverlay` Quadを既定化
  - Quest Ping / Mac Pongによるmonotonic clock offset・RTT推定
  - Mac capture→Quest receive / MediaCodec Surface releaseの段階別遅延診断
  - 実機E2Eへworld-fixed、clock sync、`capture_to_decode_ms`判定を追加
  - ゼロからPlayまで、構成図、実測、制約、troubleshootingを含む日本語README
- unit / build:
  - `cmake --build build -j8 && ctest --test-dir build --output-on-failure`: 成功、1/1 passed。
  - `scripts/test_quest_client.sh`: 成功、EditMode 7/7 passed。clock換算、OpenXR→Unity world pose、invalid pose fallbackを含む。
  - `scripts/build_quest_client.sh`: 成功、Unity IL2CPP / ARM64 APK生成。
- timestamp / input / video E2E:
  - `scripts/test_phase3.sh`: 成功。Ping/Pong応答、合成入力注入、H.264 120/120 decodeを同時確認。
  - 最終計測は76.2646 fps、平均copy 1.82951 ms、平均encode 15.5753 ms、合計17.40481 ms。
- Phase 0〜5 regression:
  - `scripts/test_phase1.sh`: 成功、120-frame Metal loopとlayer load。
  - `scripts/test_phase2.sh`: 成功、未接続60 frames、接続120/120 decode・30 fps以上。
  - `scripts/test_phase3.sh`: 成功、input / video / clock sync。
  - `scripts/test_quest_client.sh`: 成功、7/7。
  - `scripts/build_quest_client.sh`: 成功。
  - `scripts/test_phase5.sh`: 成功、EditMode 1/1、PlayMode 1/1。
- Quest device E2E:
  - `scripts/e2e_device.sh` は実機未接続を検出しexit 2。実機値は未実測。
- 詳細な実測結果: `docs/notes.md` の「Phase 6」を参照。

## Phase 7

- 成果物:
  - arm64 ad-hoc signed layer、manifest、Quest APKを同梱する自己完結`com.maquestlink.editor`
  - UPM tarball / APK / `SHA256SUMS` / `VERSION`を作る`scripts/build_release.sh`
  - package・登録・Simulator・adb・端末APKを日本語診断する`scripts/doctor.sh`
  - repository外展開smoke testとtarball経由clean Unity/Simulator E2E
  - macOS arm64 native CIとUnity license付き手動Quest APK CI
- release smoke:
  - `scripts/test_phase7.sh`: 成功。
  - `dist/`の4種類を生成し、`shasum -a 256 -c SHA256SUMS`が全3対象で成功。
  - tarballを`/private/tmp`系のrepository外directoryへ展開し、native layer / manifest / APKが存在することを確認。
  - `doctor.sh --register`はerror 0。arm64、ad-hoc署名、package/APK version 0.1.0、manifest登録、Simulator、adbを確認した。Quest未接続とSimulator停止は警告。
- clean Unity / Simulator E2E:
  - `scripts/test_phase7_clean.sh`を用意。tarballのみを参照する一時sampleでEditMode / PlayModeを実行する。
  - repository外の一時Unity projectでEditMode / Meta XR Simulator PlayModeが成功。
  - 配布packageのlayer load、`waiting_for_connection`、doctor error 0を確認した。
- Quest device E2E:
  - Quest未接続。配布APKをinstall後に`scripts/e2e_device.sh`を実行し、無装着stream / input / world-fixed / clock syncを検証する。

## Phase 8

- 成果物:
  - `xrApplyHapticFeedback` / `xrStopHapticFeedback`からQuest Touchへ左右別に伝えるHapticCommand
  - implicit manifestとlayer hookで提供する`XR_EXT_hand_tracking`、左右26関節のHandTrackingInput
  - Unity XR HandsのAndroid Hand Tracking SubsystemとMeta hand project capability
  - alpha/additive/source-alpha検出、Quest Passthrough underlay、固定uniform alpha 0.82の近似合成
  - Phase 8条件を追加したmock / native / Quest EditMode / device E2Eと全回帰script
- native / protocol / E2E:
  - `cmake --build build --parallel && ctest --test-dir build --output-on-failure`: 成功、protocol 1/1。
  - `scripts/test_phase3.sh`: 成功。120/120 H.264 decode、合成pose/action、左右26関節、haptic apply/stop、clock sync、Passthrough flagとalpha 0.82を同じTCP接続で確認。
  - native testは`hands=1 haptics=1`、mockは`haptic_apply=1 haptic_stop=1 passthrough=1 passthrough_alpha=0.82`。
- Quest client:
  - `scripts/test_quest_client.sh`: 成功、EditMode 9/9。HapticCommand / HandTrackingInput wire round-trip、joint mapping、frequency / alpha semanticを確認。
  - `scripts/build_quest_client.sh`: 成功、IL2CPP / ARM64 APK 42 MiB。
  - Android OpenXR assetでMicrosoft Hand Interaction Profileは無効、XR Hands Hand Tracking Subsystemは有効であることを確認。
- release / regression:
  - Phase 8 sourceを含むnative layer、APK、UPM tarball、checksum、VERSIONを再生成した。
  - 実機修正後のAPK SHA-256 `ea8f0ed6420dc0a4144b54e0357acbd430b3a0734e12076151434c1587a8f25a`、UPM SHA-256 `0d4a5284e9732a5247819cefc8df54a163d42e29f0ed9d2dc91a8f82d0a35518`。
  - `scripts/test_phase8.sh`: 成功。Phase 0〜7の全検証、release checksum、doctor error 0、repository外tarball Unity/Simulator E2Eが成功。
  - 最終出力は`Phase 0-8 regression passed (device result: 2)`。device result 2はQuest未接続の保留を表す。
- Quest device E2E:
  - Quest 3 (`eureka`)をUSB接続し、`scripts/e2e_device.sh`が成功。
  - 最終配布APKで`MAQUESTLINK_DEVICE_E2E_OK received_fps=74 decode_fps=76 pose_hz=73 capture_to_decode_ms=140.498283 hands_sent=2113 haptics_received=34 passthrough=1`。
  - MediaCodec Surface release、world-fixed、clock sync、Pose / hand送信、Touch haptic command受信、Passthrough underlay設定を同時に確認した。装着時の見え方と振動体感は別途目視確認が必要。
