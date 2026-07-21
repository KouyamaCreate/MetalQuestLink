# MetalQuestLink 仕様

## 実装済み

### 共有プロトコル v1

- TCPストリーム上で使う、長さプレフィックス付きのlittle-endianバイナリ形式。
- wire magicはlittle-endian `MTLK`（`0x4b4c544d`）。旧releaseとは接続互換性を持たない。
- 20 byteの共通ヘッダーは、magic、protocol version、message type、payload length、sequence numberを持つ。
- payloadの最大長は64 MiB。magic、version、message type、宣言長、メッセージ固有の可変長データをデシリアライズ時に検証する。
- message type:
  - VideoFrame: host monotonic capture timestamp、左右眼それぞれのrender pose/FOV、画面寸法、codec、eye count、flags、エンコード済み映像
  - PoseInput: sample timestamp、HMD pose、左右コントローラpose、buttons、thumbstick、trigger、grip
  - Control: kind、flags、timestamp、拡張用データ
  - HapticCommand: host timestamp、左右、apply/stop、振幅、周波数、持続時間
  - HandTrackingInput: sample timestamp、左右active、各26関節のpose/radius/valid/tracked flags
- codecはH.264とHEVC、control kindはhello/ack、stream開始/停止、ping/pong、disconnectを予約する。
- controller buttonsはuint64 bit field。bit0〜3をPrimary、Secondary、Thumbstick、Menu click、bit4〜7をPrimary、Secondary、Thumbstick、Trigger capacitive touchに割り当てる。

### OpenXR基盤

- Khronos OpenXR-SDK 1.1.61をcommit `5267613edf3d937e3d77556a106a65c2f82b25c6` に固定してCMake FetchContentで取得する。
- `metalquestlink_openxr_layer` はloader interface v1をnegotiateし、未知の関数を下位runtimeへそのまま転送するpass-through API layer。
- build時に `build/openxr/1/api_layers/implicit.d/XrApiLayer_metalquestlink.json` を生成する。
- implicit layerは `METALQUESTLINK_ENABLE_API_LAYER` が存在するときだけ有効、`METALQUESTLINK_DISABLE_API_LAYER` が存在すると無効。
- `metalquestlink_native_test` は `XR_KHR_metal_enable` を使い、runtime提供Metal deviceでstereo array swapchainを作り、指定フレーム数を描画する。
- `scripts/test_phase1.sh` は `XR_RUNTIME_JSON` でStandalone Meta XR Simulatorを選び、`XDG_DATA_HOME=build` でリポジトリ内のimplicit manifestを検出させる。

### 映像ストリーミング

- layerは `xrCreateSession`、swapchainの作成・image列挙・acquireと `xrEndFrame` を横取りし、Metal command queueと提出textureを追跡する。
- client接続中だけ、左右眼のprojection image rectをIOSurface-backed BGRA pixel bufferへside-by-sideでコピーする。同一2D-array swapchainと左右別2D / 2D-array swapchainを扱う。BGRA8はMetal blit、それ以外のRGBA8、RGB10A2、BGR10A2、BGR10_XR、RGBA16Float対応formatはMetal computeで8-bit BGRAへ変換する。
- `xrEndFrame` 進入時のhost monotonic timestampと、左右 `XrCompositionLayerProjectionView` のpose/FOVを各 `VideoFrame` に格納する。
- VideoToolbox H.264 encoderはreal-time、frame reorder無効、Main profile、60-frame key interval。bitrate既定値は左右眼合計pixel数に比例する8〜40 Mbps（3360x1760で20 Mbps）で、1〜80 Mbpsの固定値へ上書きできる。非同期encode待ちは既定2 frame（1〜8へ変更可能）に制限し、超過時は遅延を積まずframeをdropする。出力はSPS/PPS付きAnnex B。TCP再接続後の最初の映像は強制keyframeとし、decoderがGOP途中から開始しないようにする。
- TCP serverはUSBのadb reverseとWi-Fi直結の両方に対応するため、既定で `0.0.0.0:42424` をlistenする。`METALQUESTLINK_PORT` で変更できる。mock clientはloopbackで接続する。切断後は即座にコピー・エンコードを止めてpass-throughへ戻る。
- `metalquestlink_mock_viewer` はprotocol受信、Annex B解析、VideoToolbox decode、metadata検証、decode fps集計を行う。
- `scripts/test_phase2.sh` は未接続60-frame pass-throughと、接続240-frame producer / 120-frame decoderの両方を検証する。

### pose・入力注入

- transportは単一TCP connectionを全二重で使い、Mac→clientのVideoFrameとclient→MacのPoseInputを同時に処理する。OpenXR instance破棄時にlistenerを停止・joinし、layer unload後にthreadを残さない。listener / accepted socketにはclose-on-execを設定し、layer processが起動したadbなどの子孫processへsocketを継承させない。
- 最後に受信したPoseInputを各hookが参照する。接続切断時、またはローカル受信から500 ms経過したstale入力ではruntime結果を変更しない。
- `xrLocateViews` はHMD poseを中心として左右±32 mmのeye poseを返す。`xrLocateSpace` はVIEW reference spaceと左右action spaceを追跡し、base spaceからの相対poseへ変換する。
- `xrCreateActionSet` / `xrCreateAction` / `xrSuggestInteractionProfileBindings` を追跡し、actionと左右subaction path、binding componentを対応づける。
- `xrSyncActions` はruntimeへ転送し、`xrGetActionStateBoolean` / `Float` / `Vector2f` / `Pose` の成功結果を受信入力で差し替える。click/touch、trigger、squeeze、thumbstick、grip/aim poseを扱う。
- `changedSinceLastSync` はsession/action/subactionごとに前回返却値と比較する。入力中断時はruntime stateへ戻る。
- `metalquestlink_mock_viewer --send-input` は既知の合成HMD/controller pose、button、thumbstick、trigger、gripを約90 Hzで送る。
- `scripts/test_phase3.sh` は映像decodeと同時に、合成値がview、action space、boolean/float/vector action stateへ反映されることを検証する。native testの出力はpipeを介さずlog fileへ直接書き、子孫processがpipe writerを継承して終了を妨げる経路を作らない。
- `xrApplyHapticFeedback` / `xrStopHapticFeedback`の成功結果を左右のHapticCommandへ変換する。振幅は0〜1へclampし、周波数と持続時間を保持する。
- implicit layer manifestで`XR_EXT_hand_tracking`を列挙し、`xrGetSystemProperties`のsupport、hand tracker作成／破棄、左右26関節のlocateを提供する。受信手がinactiveまたはstaleならinactiveを返す。

### Questクライアント

- `quest-client/` はUnity 6000.3.6f1、Meta XR Core SDK 203.0.0、Unity Meta OpenXR 2.5.1を使うAndroid OpenXR project。
- MediaCodecへAnnex B access unitを投入し、Meta `OVROverlay` のcompositor-managed External Surfaceへ直接出力する。SBS映像の左右halfを各eyeへ割り当てる。
- MediaCodecはlow-latency modeを先に試し、未対応端末では通常modeへfallbackする。H.264とHEVCのprotocol codecに対応する。
- Quest 3 / 3Sでは`XR_KHR_android_surface_swapchain`でMediaCodec出力先を作り、side-by-side映像を左右の`XrCompositionLayerProjectionView`へ直接提出する。必要な拡張が使えない場合だけ3.2 m幅の`OVROverlay` Quadへfallbackする。`OVRCameraRig` / `OVRManager`を持つgenerated sceneをCLI build時に生成する。
- HMDと左右controllerをUnity XR inputから毎Update取得し、OpenXR座標へ変換して最新PoseInputを送る。72 fpsを要求し、transportはbacklogを作らず最新sampleだけを送信する。
- Unity XR HandsのHand Tracking SubsystemをAndroidで型指定して有効化する。同一feature IDを持つMicrosoft Hand Interaction Profileは使わない。追跡中の左右26関節をOpenXR順へ写し、PoseInputと同じ最新値優先transportで送る。
- hand visualization指定時は受信側にcollider不要のprocedural joint sphere / bone cylinderを生成し、左手を緑、右手を青で表示する。追跡無効時は該当手を隠す。
- HapticCommandは`OVRInput.SetControllerVibration`へ写し、duration満了またはstopで左右個別に停止する。Quest APIのfrequency値は0〜320 Hzを0〜1へ正規化する。
- VideoFrameのPassthrough flag受信時はMeta Passthrough underlayを有効にし、External Surface overlay全体へ固定alpha 0.82を適用する。
- 接続候補は`127.0.0.1`（`adb reverse tcp:42424 tcp:42424`）を先に試し、指定されたWi-Fi hostへfallbackする。切断後は500 ms間隔で自動再接続する。
- `adb shell am start` extrasでdiagnostic、host、Wi-Fi fallback、port、Passthrough、hand visualizationを上書きできる。diagnostic modeは毎秒 `METALQUESTLINK_DIAGNOSTIC` JSONをlogcatへ出す。
- `scripts/test_quest_client.sh` はXR非依存のprotocol/transportをEditModeで検証し、`scripts/build_quest_client.sh` はIL2CPP/ARM64 APKを生成する。
- `scripts/e2e_device.sh` はinstall、adb reverse、無装着power automation、起動、Mac producer、logcat判定を自動化し、receive/decode 30 fps以上とPose送信60 Hz以上を要求する。実機入力modeは合成固定値を要求せず、Quest診断のpose / hand送信と接続後に再送するhapticで全二重経路を判定する。producer失敗時はMac側末尾と関連するQuest例外を表示する。

### Unityエディタ統合

- `editor-package/` はUnity 2022.3 LTS以降向けlocal UPM package `com.metalquestlink.editor`。XR Plug-in Management 4.4.0 / OpenXR 1.8.2をresolver baselineとし、既存projectの新しい互換versionを維持する。Unity起動時とPlay開始直前にlayer manifestを `$HOME/.local/share/openxr/1/api_layers/implicit.d` へ登録する。
- Play開始直前にUnity version、Standalone OpenXR、Simulator runtime、native layer、port、bitrate、pending上限をpreflightする。6000.2 / 6000.3はverified、2022.3以上のmatrix外versionはwarningと能力check、2022.3未満はerrorとする。能力checkのerror時はPlayを止め、設定windowに理由を表示する。
- Standaloneの自動設定はOpenXRを先頭loaderにし、既存loaderをfallbackとして保持する。Android側のloader / featureは変更しない。利用者指定pathは絶対pathまたはUnity project root基準の相対pathとして解決する。
- Play開始直前に `METALQUESTLINK_ENABLE_API_LAYER`、port、bitrate、pending frame上限、layer log、status JSONの環境変数を設定する。ADB deviceがあればserial指定付きで `adb reverse` を設定し、既定ではインストール済みQuest clientをPassthrough / hand visualization / Wi-Fi fallback指定付きの分離processで起動する。Play時にAPK buildは行わない。
- `Window > MetalQuestLink` はQuest接続状態、fps、平均Metal copy / VideoToolbox encode合計時間、encoded / dropped frame数、stream解像度、Unity互換性levelを表示する。`Quick Setup (Project + Quest)`はStandalone OpenXR設定、layer登録、Quest APK installを一括実行し、Quest未接続時はproject setupを完了してAPK installをpendingとして報告する。個別操作も残す。
- native layerは `METALQUESTLINK_STATUS_FILE` 指定時、connection、encoded / dropped frames、stream寸法、fps、平均copy / encode / pipeline msを1秒周期でatomic JSON更新する。
- `samples/MetaXRMinimal/` はUnity 6000.3 project。Meta XR Core SDK 203.0.0の`OVRCameraRig`、左右Touch controllerの`OVRGrabber`、`OVRGrabbable` cubeを生成する。
- `scripts/test_phase5.sh` はsample生成、package manifest EditMode test、Meta XR Simulator上のPlayMode testを実行し、Unity applicationへのlayer loadと未接続待ち状態を検証する。PlayMode runはMetal graphics deviceが必要なため`-nographics`を使わない。
- `scripts/test_unity_matrix.sh`はrepository外の一時projectで同一packageをUnity 2022.3.44f1 / 6000.2.5f1 / 6000.3.6f1へ解決し、compatibility unit testとC# compile error不在を検証する。

### 再投影とレイテンシ計測

- Quest clientのnative OpenXR featureは`xrEndFrame`をchain hookし、Unityが提出するprojection layerを受信映像用projection layerへ差し替える。VideoFrameの左右render pose/FOVを座標変換せず各眼へ渡し、side-by-side swapchainの左半分／右半分を対応づける。その後のhead motionはQuest compositorの再投影へ任せる。
- Passthrough時は他のcomposition layerを保持したままprojectionへsource-alphaと固定alpha 0.82を適用する。Android Surface拡張が無効な場合だけ、左右render poseの平均から2 m先のworld-fixed Quadを配置するfallbackを使う。
- Quest clientは1秒周期でclient monotonic timestampを持つControl Pingを送る。Mac layerは受信host monotonic timestampと元client timestampを持つPongを即時返信する。
- QuestはPing送信/受信の中央時刻とhost受信時刻から`host - client` offsetを推定する。このoffsetでMac capture timestampをQuest clockへ写し、capture→TCP受信とcapture→MediaCodec output Surface releaseを計測する。
- `METALQUESTLINK_DIAGNOSTIC` は`reprojection`、`clock_synced`、`clock_rtt_ms`、`capture_to_receive_ms`、`capture_to_decode_ms`を含む。未同期値は`-1`。
- Mac layer statusはschema `version: 1`とconnection、encoded / dropped frame、stream寸法、copy、encode、合計pipeline msを安定した`Library/MetalQuestLink/status.json`へ出す。Quest側decode値はSurface releaseまでであり、光学的motion-to-photonではない。
- `scripts/e2e_device.sh` はQuestのOpenXR初期化完了後に有限producerを開始し、既存fps / pose Hzに加え、immersive projection Surface生成、clock sync、capture-to-decode値を判定する。

### 配布package

- `editor-package/`はgit URLまたはlocal tarballで導入できる自己完結UPM package `com.metalquestlink.editor`。`Native~/macOS/`にarm64 OpenXR layerとmanifest、`QuestClient~/`にIL2CPP / ARM64 APKを含む。installerはrepositoryの`build/`や`quest-client/Builds/`へfallbackしない。
- release 0.2.0でproduct identifierを全面変更した。旧package / APKとのin-place upgradeではなく、新package / applicationとして導入する。
- package versionの正本は`package.json`。`VERSION`とQuest APK `bundleVersion`を同じsemverに保つ。HEADにtagがあるrelease buildでは`v<semver>`との一致を必須とする。
- `scripts/build_release.sh`はnative Release build / ctest、Quest APK build、dylib ad-hoc署名を行い、`dist/`へUPM tarball、APK、`SHA256SUMS`、`VERSION`を生成する。
- `scripts/doctor.sh`はApple Silicon / macOS、package binaryと署名、package/APK version、implicit manifest、Meta XR Simulator、adb、接続deviceとinstall済みAPK versionを検査する。Quest未接続とSimulator停止は警告として通常診断を継続する。
- `scripts/test_phase7.sh`はrepository外へtarballを展開して自己完結性とchecksumを検証する。`scripts/test_phase7_clean.sh`はtarballだけを参照する一時Unity sampleでEditMode / Simulator PlayMode E2Eを行う。
- native CIはGitHub-hosted `macos-15` arm64 runnerを使う。Quest APK CIはUnity license secretsを必要とする手動workflow。

### Quest拡張機能

- Mac layerは`XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND` / `ADDITIVE`、またはprojection layerの`XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT`を検出し、VideoFrameのPassthrough flagを設定する。
- protocol v1のPassthrough flagは固定uniform alpha 0.82を意味する。H.264の画素alphaは伝送せず、QuestではPassthrough underlayの前に半透明の映像overlayを合成する近似方式。
- mock E2Eはhaptic apply/stopの振幅・duration、左右26関節、Passthrough flagとalpha 0.82のsemanticを検証する。
- Quest実機E2Eはhands送信、haptic受信、passthrough diagnosticを既存stream/input/clock条件に追加する。

## 対応外

- シーンアンカー、空間メッシュ、アイ／フェイストラッキング。
- パススルー映像の画素alpha、black key、premultiplied alpha、HEVC with alpha。
- Play中のeye texture解像度／codec変更に伴うQuest MediaCodec Surfaceの動的再構成。
