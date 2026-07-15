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

### Phase 3 回帰修正

- native E2E成功後もtest scriptが終了しない回帰を修正した。
- 原因はlayerのTCP socketを子孫processのadbが継承し、さらにteeのpipe writerも継承してEOFを妨げていたことだった。
- listener / accepted socketへclose-on-execを設定し、Phase 1〜3 testはnative出力をlog fileへ直接書くように変更した。
- 入力E2Eでは、unpacedなSimulatorでもretry中のmock clientが有限frame loop前に接続できるよう500 msのsettle時間を設けた。
- `scripts/test_phase3.sh`、`scripts/test_phase2.sh`の順で完走し、終了後にTCP 42425を保持するprocessがないことを確認した。

### Phase 4 変更

- Unity 6000.3.6f1 + Meta XR Core SDK 203.0.0のQuest client projectを追加した。
- MediaCodec low-latency decoderとOVROverlay External SurfaceのSBS表示を追加した。
- HMD / Touch Plus pose・button・touch・analog入力の72 Hz送信とlocalhost / Wi-Fi再接続transportを追加した。
- adb extras診断mode、Unity EditMode test、CLI APK build、無装着device E2Eを追加した。

### Phase 4 理由

- Mac側で実証済みの映像・入力経路をQuest実機アプリへ接続し、装着なしで性能を判定できる受信側を用意するため。

### Phase 4 影響

- `quest-client/`
- `scripts/build_quest_client.sh`、`scripts/test_quest_client.sh`、`scripts/e2e_device.sh`
- `.gitignore`
- `README.md`、`docs/`、`session.md`

### Phase 4 残り

- Quest未接続のため、実機MediaCodec / External Surface / input rate / power automationは `scripts/e2e_device.sh` で後日実測する。
- Phase 5でEditor packageとsampleを追加し、Unity Editor Play modeからのend-to-end起動を実証する。

### Phase 5 変更

- local UPM package `com.maquestlink.editor` とconnection/性能表示Editor windowを追加した。
- layer自動登録、Play開始前の環境設定、adb reverse、APK install/startをEditorへ統合した。
- native layerへ接続状態、fps、copy/encode時間のJSON status出力を追加した。
- Meta XR SDKの最小grabbable sceneとUnity EditMode / PlayMode E2Eを追加した。

### Phase 5 理由

- package導入、APK install、Playの3操作以内でMac EditorからQuest接続待ちまで到達させるため。

### Phase 5 影響

- `editor-package/`
- `samples/MetaXRMinimal/`
- `layer/src/streaming.mm`
- `scripts/test_phase5.sh`
- `.gitignore`
- `README.md`、`docs/`、`session.md`

### Phase 5 残り

- Quest未接続のため、Editor windowのconnected/fps/latency実値表示は実機E2Eで確認する。
- Phase 6でworld-fixed再投影とmotion-to-photon計測を実装する。

### Phase 6 変更

- stereo render poseからQuest world座標上のQuad poseを復元し、world-fixed compositor再投影を既定化した。
- Control Ping/PongをMac layerとQuest transportへ実装し、別epochのmonotonic clockを近似同期した。
- capture→receive / MediaCodec Surface release、clock RTTをQuest診断へ追加した。
- mock clientへclock sync E2Eを追加し、Quest unit testを7件へ拡張した。
- device E2Eへ再投影mode・clock同期・capture-to-decode判定を追加した。
- READMEを第三者向けの日本語setup / architecture / troubleshooting documentへ更新した。
- mock E2E portを製品portと分離し、Simulator/adb reverseとのテスト間競合を解消した。

### Phase 6 理由

- network/decode遅延中のhead motionをworld-space compositorで補正し、Mac/Quest間の段階別遅延を共通基準で観測するため。

### Phase 6 影響

- `layer/src/transport.cpp`、`layer/tools/mock_viewer.mm`
- `quest-client/Assets/MaQuestLink/Runtime/`、`Tests/EditMode/`
- `scripts/test_phase1.sh`、`test_phase2.sh`、`test_phase3.sh`、`test_quest_client.sh`、`build_quest_client.sh`、`e2e_device.sh`、`test_phase5.sh`
- `README.md`、`docs/`、`session.md`

### Phase 6 残り

- Quest実機未接続のため、world-fixed表示の目視、MediaCodec Surface、capture-to-receive / decode実値は未確認。
- Phase 7でnative layer / APKをUPM packageへ同梱し、repository build不要の配布物を作る。

### Phase 7 変更

- UPM packageへarm64 native layer、layer manifest、Quest APK、VERSIONを同梱し、repository build fallbackを削除した。
- 1 command release builder、checksum付き配布物smoke test、repository外Unity E2E、doctorを追加した。
- GitHub ActionsへmacOS arm64 native regressionとUnity license前提の手動Quest APK buildを追加した。
- READMEへ他のMac利用者向けtarball / git URL導入、Gatekeeper、公証、release、CI secret手順を追加した。

### Phase 7 理由

- CMake / Xcode / Homebrewを持たない利用者がpackage導入、APK install、Playだけで利用できる配布境界を作るため。

### Phase 7 影響

- `editor-package/Native~/`、`editor-package/QuestClient~/`、`editor-package/VERSION`
- `scripts/build_release.sh`、`doctor.sh`、`test_phase7.sh`、`test_phase7_clean.sh`
- `.github/workflows/`
- `README.md`、`docs/`、`session.md`

### Phase 7 残り

- Quest未接続のため配布APKの実機無装着E2Eは、接続後の実行手順を残す。

### Phase 8 変更

- HapticCommandとHandTrackingInputをC++ / C# protocolへ追加し、全二重transportへ統合した。
- layerへhaptic apply/stop、`XR_EXT_hand_tracking` system/create/locate/destroy hookを追加した。
- Quest clientへXR Hands 26関節sampling、Touch vibration、Meta Passthrough underlayを追加した。
- Macの透過合成要求をVideoFrame flagへ写し、Questで固定uniform alpha 0.82を適用する近似方式を追加した。
- mock/native/Quest/device E2EとPhase 0〜8全回帰scriptを更新した。
- Android OpenXR feature ID衝突を検出し、XR Hands Hand Tracking Subsystemを型で選ぶよう修正した。
- READMEへQuest機能対応状況とパススルー近似の限界を追加した。

### Phase 8 理由

- controller入力以外で価値と実現性の高いQuest機能を既存のAPI layer / TCP構成へ追加し、対応範囲を利用者が判断できるようにするため。

### Phase 8 影響

- `layer/include/maquestlink/protocol.hpp`、`layer/src/`、`layer/native-test/`、`layer/tools/`、`layer/tests/`
- `quest-client/Assets/MaQuestLink/Runtime/`、`Editor/`、`Tests/EditMode/`、Quest / OpenXR project settings
- `editor-package/Native~/`、`editor-package/QuestClient~/`、`dist/`
- `scripts/test_phase3.sh`、`test_phase8.sh`、`e2e_device.sh`
- `README.md`、`docs/`、`session.md`

### Phase 8 残り

- Quest未接続のため、実機のMediaCodec表示、26関節60 Hz、Touch振動、Passthrough underlay / uniform alpha合成は未確認。
- シーンアンカー、空間メッシュ、アイ／フェイストラッキング、画素alphaは明示的にスコープ外。

### Quest 3実機E2E修正

- generated sceneのpresenter参照が空になる生成順序を修正し、参照を明示保存した。runtimeでも欠落参照を再探索し、例外ループを防ぐ。
- native testへ実機入力modeを追加した。合成固定値ではなく、Quest診断のPose / hand rateと接続後のhaptic往復を組み合わせて判定する。
- 実機testでは接続成立前のhaptic dropを避けるためapply / stopを周期再送する。
- device scriptはbackground processをwaitして正常に回収し、producer失敗時にMac log末尾と関連Quest例外を表示する。
- 最終配布APKのQuest 3実機E2Eはreceive 74 fps、decode 76 fps、Pose 73 Hz、capture-to-decode 140.498283 ms、hand message 2,113件、haptic 34件、world-fixed / clock sync / Passthrough有効で成功した。
