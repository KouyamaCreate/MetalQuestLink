# 変更履歴

## 2026-07-22 — Build Weekデモ映像 v5最終監査版

- 映像、CG、カメラ、モーショングラフィクスのタイムラインは固定し、ナレーションを23個のshot-sized cueへ再編集した。各発話を対象カット到着後に開始し、関係のない次カットへ跨がない順序へ変更した。字幕JSONは実音声と同じ配置を使用する。
- `FullDemoV4`と`FullDemoV5`からMOSS効果音の全参照を削除した。最終音声はQwen3-TTS合成ナレーションとACE-Step BGMの2レイヤーだけで、冒頭から末尾まで89秒のBGMを維持する。
- Whisper逆文字起こしで、Unity/Quest hero 5–9秒、pipeline 21–26秒、Quest decode 38–42秒、package 56–63秒、compatibility/stack 69–76秒、OSS 82–86秒へ発話が収まることを確認した。5秒間隔Visionでも字幕と画面内容の対応を確認した。
- 再生成後は89.000秒 / 2670フレーム、-16.01 LUFS-I / -3.94 dBTP。全フレームdecode、black/freeze 0件。字幕領域を除く上部900pxの旧visual lock比較はSSIM 0.998375。SHA-256は`e76b1b927a281c1bfb7dfc621f23a2ebe78c9ae9de2f1cfd6ea2d5c6cad6999c`。
- 冒頭5秒へ実Unity Gameビュー収録を追加し、英語の`ACTUAL UNITY CAPTURE`と`PRESS PLAY. SEE IT MOVE.`で、Quest接続なしでもPlay中のsceneが動く事実を示した。後半84秒は7つのBlender CGと7つのRemotion情報章を交互に維持した。
- Quest 3は監査済み元メッシュの頭頂・左右ストラップを完全復元し、左右各448面のレンズへ正方形寄りの同期映像を別々に割り当てた。Quest側とMac側のUSB-Cは開口へ密着させた。
- MacBook前縁に残っていた重複カバー`CGV5_MacFrontRecess.001`だけをbounds検証後に削除し、実機形状に近い黒い指掛けrecessへ戻した。修正処理はsuffix付き全revisionを除去し、再実行可能にした。
- 影響4区間をEEVEE Next 64 samples / 2560x1440 / 16-bit PNGで再レンダーし、1170枚の欠番、0-byte、旧時刻frameを0にした。39秒CGをLanczosで1080p化し、1秒間隔Visionでも形状、端子、レンズ、カメラ多様性を確認した。
- Remotion V5を2670フレームで再生成し、ACE-Step 1.5 XL BGM、英語ナレーション、shot-aligned JSON字幕を統合した。最終masterを89.000秒、-16.01 LUFS-I / -3.94 dBTPにした。
- 全2670フレームdecode、blackdetect 0件、1秒以上のfreeze 0件、1秒間隔duplicate 0件、3秒間隔Vision、冒頭0.5秒間隔、全15境界の前後frame、3系統の独立監査に合格した。映像監査結果は維持し、最終音声・字幕版のhashは上記へ更新した。

理由: Questのストラップ欠落とMac前縁の不要な突起を完成版から除去し、89秒上限、製品形状、同期表示、カット連続性、音量、OSSクレジットを提出可能な証拠で確定するため。

## 2026-07-22 — Build Weekデモ映像 v4最終版

- 39秒のBlender CGを7固有ショットへ再構成し、84秒の最終タイムラインでCGとRemotionを交互に配置した。各境界は直前の画面内移動ベクトルを引き継ぐ12フレームのdirectional handoffとcustom cubic Bézierで接続した。
- Unity Editor生成画像はそのまま保持し、Game viewportだけへ共有XR動画を合成した。MacとQuestは同じframe-driven sceneを使い、Questだけ左右眼へ小さい視差を与えた。左右レンズUVの鏡像を補正し、Mac・左眼・右眼すべてでシアンcubeが左、紫sphereが右になることをVision確認した。
- Quest USB-Cを前面から見て右側の楕円リセスへ移し、金属舌を内部へ埋め、黒いplug bodyを開口へ密着させた。ケーブルはvisorを横切らず右下へ逃がし、端子アップを本編へ残した。
- レンズアップは1つの連続ショットだけに限定し、続くCGをMac→Quest rack focusとcounter-orbit resolveへ変更した。Macはevaluated boundsで接地した。
- EEVEE Next 64 samples / 2560x1440 / 16-bit PNGを1170枚生成し、Lanczosで1080pへ縮小した。停止境界に残った旧時刻frame 638を検出して単独再レンダーし、欠番・0-byte・旧frame混入を0にした。
- `FullDemoV4`へシンプル字幕、ACE-Step 1.5 XL BGM、MOSS-SoundEffect-V2.0の4 cue、英語ナレーションを統合した。最終masterは84.000秒 / 2520フレーム / 1920x1080 / 30 fps、-16.01 LUFS-I / -3.25 dBTP。全decode、black/freeze、4秒間隔Vision、全14境界、端子原寸、1秒間隔duplicate scanに合格した。

理由: Unity Play ModeからQuest 3へbuildなしで同じ動くsceneが届く因果関係を、製品CGと技術情報の密度を交互に保ちながら、端子・レンズ・カメラ接続の不自然さなしで示すため。

## 2026-07-22 — Build Weekデモ映像 v3再構成

- 108秒版の反復的なCG尺を廃止し、8つの固有CGショット31.5秒、6章のRemotion 50秒、spoken resolve 2.5秒からなる84秒構成へ変更した。
- imagegenでUnity 6編集画面のEdit / Play hover / active Playの3状態を制作し、MacBookの実パネルメッシュへ直接UV割り当てした。旧`UNITY_PLAY_TRIANGLE`と`DATA_ARC`は名前優先で削除し、親子階層に残らないようにした。
- MacBookをworld-space boundsで接地し、トラックパッドをアルミ材へ変更した。Questは左右各448面の物理レンズへ別々の正方形寄り映像を割り当て、USB-C端子をストラップ下の側面へ移した。
- CGは2560x1440 / 64 samples / 16-bit PNGで生成後、Lanczosで1080pへ縮小する。DOFはf/8〜32へ抑え、エッジと被写界深度のジャギーを減らす。
- 後半をfull duplex、backpressure、package、compatibility、native stack、OSSの固有6章へ再設計し、カスタムBézier easingと全フレーム継続する背景運動を実装した。
- 効果音を全削除した。83.52秒の英語ナレーションを映像内容へ同期し、ACE-Step BGMは終盤のpeak / resolveを84秒の結末へ再配置した。
- 最終masterは84.000秒 / 2520フレーム / 1920x1080 / 30 fps。全decode、黒画面、-55 dB・1秒のfreeze、1秒間隔の非連続重複frame、境界Vision検査に合格し、音声を-16.00 LUFS-Iへ正規化した。

理由: 同じ素材の繰り返しによる尺稼ぎをなくし、Mac / Questを必要とする説明だけをCG、技術説明をRemotionへ分離し、提出映像の密度と可読性を上げるため。

## 2026-07-21 — Build Weekデモ映像 v2全面再制作

- CG主体の108秒構成へ刷新し、Blender 72秒、英語Remotion 30秒、end card 6秒へ短縮した。
- MacBookの実メッシュboundsを使う接地補正を追加し、画面へクリーンなUnity Play映像を直接埋め込んだ。
- Questの外付け表示カードを廃止し、元モデルの左右レンズ各448面へ同一VR環境の左右動画材を直接割り当てた。
- Quest眼素材の誤った上下配置を修正し、正方形寄りの左右眼を横並びにして別々の物理レンズへ投影した。
- Quest内部カメラを24〜26mmの超近接・正対構図へ変更し、上向きに広がるストラップを画角外へ送りつつ両眼を水平に見せた。
- Unity編集画面を13秒へ延長し、冒頭3ショットの連続push-inとframe 300のPlayクリックを同期した。
- ケーブルを共通曲線上の芯線と10本編組へ作り直し、両端をMacBookとQuestの実ポート位置へ接続した。
- Blender本番中間素材を8-bitから16-bit PNGへ変更し、最終end cardへCC BY 4.0作者クレジットを追加した。
- Qwen3-TTS Baseの英語男性ナレーションを完全ローカル生成し、Whisper逆文字起こしとラウドネス検証を行った。
- 2160/2160枚の1080p 16-bit PNGを欠損・0-byteなしで書き出し、Remotionと結合した108秒・3240フレームの最終masterを生成した。
- 最終音声を-16.1 LUFS-I / -2.1 dBTPへ正規化し、全編decode、black/freeze scan、3秒間隔と切替境界のVision QAを完了した。

理由: 製品モデル、画面埋め込み、VR両眼表示、カメラワークの見た目を提出品質へ引き上げ、3分制限内でCGを主役にするため。

## 2026-07-21 — Build Weekデモ映像

- Blender MCPでUnity PlayからQuestへの接続フローを示す12秒の3D可視化を制作した。
- Remotionで技術構成、双方向入力、Quest 3実測値、配布互換性、Codex / GPT-5.6の役割を説明する98秒のモーショングラフィクスを追加した。
- ACE-Step 1.5 XL TurboのBGM、MOSS-SoundEffect-v2.0の7種SFX、英語ナレーションを統合し、1:58の1080p動画へ書き出した。
- 冒頭は実機収録と誤認されないよう`3D FLOW VISUALIZATION`と表示する。実機未接続のため、提出要件に応じて先頭12秒をUnity / Quest実写へ差し替える。

理由: 3分以内で製品価値、技術的独自性、実測根拠、OSS配布性を一貫して説明できる提出用成果物を用意するため。

## 2026-07-21 — MetalQuestLink全面改名

- 表示名だけでなく、C# namespace / class / asmdef、UPM ID、Android application ID、OpenXR layer、環境変数、CMake target、native library、APK / tarball、sample path、diagnostic marker、docsを`MetalQuestLink`へ統一した。
- wire magicを`MQLK`から`MTLK`へ変更し、混在を誤接続として拒否する。release versionをbreaking changeとして`0.2.0`へ上げた。
- 配布物名を`com.metalquestlink.editor-0.2.0.tgz`と`MetalQuestLink-0.2.0.apk`へ変更した。

理由: 英語圏でもMetalベースのQuest streaming toolだと名称から理解しやすくし、公開surfaceに旧称を残さないため。

## 2026-07-21 — OpenAI Build Week提出・Unity互換性・OSS運用

- Editor packageのUnity baselineを6000.2から2022.3 LTSへ変更し、XR Management / OpenXR依存を4.4.0 / 1.8.2のresolver baselineへ下げた。
- version番号だけでPlayを拒否せず、verified / compatible-unverified / unsupportedの互換性levelと既存能力checkを組み合わせるpreflightへ変更した。
- `Quick Setup (Project + Quest)`でStandalone OpenXR設定、layer登録、同梱APK installを一括実行できるようにした。
- Unity 2022.3 / 6000.2 / 6000.3の一時project matrix testを追加した。
- 英語judge guide、互換性方針、Devpost worksheet、3分demo構成を追加した。
- Apache-2.0、CONTRIBUTING、Code of Conduct、Security Policy、CHANGELOG、Issue / PR templateを追加し、外部contributor向けdocs更新契約を定義した。
- Unity 6000.2 / 6000.3 matrix、新tarballのrelease smoke、repository外Unity/Simulator clean E2Eを成功させた。2022.3はlocal Editor license未有効で実行前にblockedした。
- Devpostの未提出草稿を`MetalQuestLink`、Developer Tools向けtagline、事実ベースの説明、Built withへ更新した。

理由: 特定Unity patchへの不要な固定を減らし、配布tarballを短時間で評価でき、公開後もIssue / PRと実装docsが同期する運用にするため。

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
- `layer/include/metalquestlink/protocol.hpp`
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

- `layer/include/metalquestlink/protocol.hpp`
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

- local UPM package `com.metalquestlink.editor` とconnection/性能表示Editor windowを追加した。
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
- `quest-client/Assets/MetalQuestLink/Runtime/`、`Tests/EditMode/`
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

- `layer/include/metalquestlink/protocol.hpp`、`layer/src/`、`layer/native-test/`、`layer/tools/`、`layer/tests/`
- `quest-client/Assets/MetalQuestLink/Runtime/`、`Editor/`、`Tests/EditMode/`、Quest / OpenXR project settings
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

### ハンド可視化とPlay即時プレビュー

- Quest clientへ左右26関節のprocedural skeleton表示を追加した。Android playerでprimitive collider classがstripされる問題を避け、MeshFilter / MeshRendererだけで生成する。
- device E2Eへhand visualizationとactive joint必須modeを追加し、Quest 3実機で左右52関節、hand message 2,465件、Passthrough有効を同時確認した。
- Editor packageのPlay hookはAPKをbuildせず、adb reverse後にインストール済みclientをPassthrough / hand visualization extras付きで起動する。
- `Window > MetalQuestLink`へ`Passthrough preview`と`Show tracked hands`を追加した。

理由: コントローラなしでも追跡結果を目視でき、既存Unity projectのPlayからAndroid build待ちなしで実機previewへ入れるようにするため。

### 既存Unity projectのPlay表示修正

- Editor packageの最低Unity versionを6000.2へ広げ、Standalone XR loader、Oculus Touch feature、Meta XR feature setを自動設定する。Android loaderは既存設定を保持する。
- GUI起動のUnityでもMeta XR Simulator runtime JSONを検出して`XR_RUNTIME_JSON`へ設定し、不足時はPlayを明示的に止める。
- Unity 6000.2.5f1 / OpenXR 1.15.1のParaSights実シーンが提出するRGBA8Unorm_sRGBの2D-array swapchainをMetal computeでBGRAへ変換する。
- stream再接続後の最初の映像を強制keyframeにし、SPS/PPSなしの差分frameからdecoderが始まる問題を修正する。
- ParaSights実シーンで3360x1760映像を60/60 frame、同一Play中の再接続でも30/30 frame VideoToolbox decodeした。Quest接続中にMac側`encodedFrames=427`、約18.5 fpsを確認した。

理由: sample demoではなく、利用者の既存Unity sceneをAndroid buildなしのPlay操作でQuestへ即時previewできるようにするため。

### Quest一人称projection表示

- 2 m先の`OVROverlay` Quadを既定表示にする構成を廃止し、`XR_KHR_android_surface_swapchain`でMediaCodec出力SurfaceをOpenXR stereo projection layerへ直接提出するnative featureを追加した。
- `xrEndFrame`で既存projectionを左右眼pose/FOVとside-by-side rectを持つ受信映像projectionへ差し替え、Passthroughなど他のcomposition layerは保持する。
- Android native pluginをQuest APK build前にarm64 cross-buildし、16 KB page alignmentで同梱する。
- Quest EditModeへ左右眼pose/FOV mapping testを追加し、device E2EはOpenXR初期化完了を待ってからproducerを開始してimmersive Surface生成を検証する。

理由: world-fixed Quadでは映像が空間内の板に見え、VRの視野全体を覆う一人称体験にならないため。

### 複数Unity project向けパッケージ互換性

- Play前のproject checkを追加し、Unity version、Standalone OpenXR、Simulator runtime、native layer、port、bitrate、encode待ち上限を検証する。
- Standalone OpenXR設定は既存XR loaderを削除せず、OpenXRを先頭へ移して他のloaderをfallbackとして保持する。Android設定は変更しない。
- adb / APK / runtime JSONはproject root基準の相対pathにも対応し、複数Quest用serial、USB優先時のWi-Fi fallback hostを設定可能にした。
- H.264 bitrateは左右眼解像度に応じて8〜40 Mbpsを自動選択し、1〜80 Mbpsへ上書きできる。非同期encode待ちを既定2 frameに制限し、超過時は遅延を積まずdropする。
- 同一2D-array swapchainと左右別2D / 2D-array swapchainを扱い、BGRA8 / RGBA8に加えて10-bitとRGBA16Floatを8-bit BGRAへ変換する。
- status / Editor windowへstream解像度とdropped frame数を追加した。adb outputは非同期読取へ変更し、timeout前のpipe EOF待ちを防いだ。
- native testへ左右別swapchain modeを追加し、Phase 2回帰で通常arrayと左右別2Dをどちらも実decodeする。

理由: Unity version、XR loader順、stereo rendering mode、render texture format、接続端末、解像度が異なる既存projectへ、個別改造なしでUPM packageを導入できる範囲を広げるため。

### MetalQuestLink全面改名の完了検証

- product name、C# namespace / assembly、UPM / Android ID、OpenXR layer、environment variable、CMake target、native library、APK / tarball、sample、docsを`MetalQuestLink` / `metalquestlink`へ統一した。
- wire magicを`MQLK`から`MTLK`へ変更し、新旧binaryの誤接続を防ぐbreaking release `0.2.0`とした。
- renamed native build / CTest 1/1、Quest EditMode 12/12、Unity 6000.2 / 6000.3 matrix、APK build、release smoke、repository外clean tarball Unity / Simulator E2Eが成功した。
- Devpostの表示名・tagline・説明は更新済み。connectorがslug変更を提供しないため、URLの`maquestlink`だけは手動更新待ち。

### Public OSS repository documentation

- GitHub root `README.md`を英語のcanonical entry pointへ変更し、完全な日本語ガイドを`README.ja.md`へ移した。
- rootからinstall、互換性、architecture、feature status、diagnostics、build/test、limitations、contribution、security、licenseへ到達できるよう整理した。
- `SUPPORT.md`、`THIRD_PARTY_NOTICES.md`、`.gitattributes`、Dependabot設定、Public公開checklistを追加した。
- UPM package READMEを英語化し、Issueのsecurity linkから`OWNER` placeholderを除去した。
- 14GBの`demo-video/`は生成render、第三者reference、実機capture、個人絶対pathを含むため、作業データを削除せずrepository全体のignore対象にした。公開仕様から未公開video asset固有の制作仕様を分離した。

理由: 海外利用者がGitHub rootだけで価値・導入・制約・参加方法を判断でき、ローカル制作環境や第三者素材を誤ってPublicへ含めないため。
