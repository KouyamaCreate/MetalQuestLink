# MaQuestLink 仕様

## 実装済み

### 共有プロトコル v1

- TCPストリーム上で使う、長さプレフィックス付きのlittle-endianバイナリ形式。
- 20 byteの共通ヘッダーは、magic、protocol version、message type、payload length、sequence numberを持つ。
- payloadの最大長は64 MiB。magic、version、message type、宣言長、メッセージ固有の可変長データをデシリアライズ時に検証する。
- message type:
  - VideoFrame: host monotonic capture timestamp、左右眼それぞれのrender pose/FOV、画面寸法、codec、eye count、flags、エンコード済み映像
  - PoseInput: sample timestamp、HMD pose、左右コントローラpose、buttons、thumbstick、trigger、grip
  - Control: kind、flags、timestamp、拡張用データ
- codecはH.264とHEVC、control kindはhello/ack、stream開始/停止、ping/pong、disconnectを予約する。
- controller buttonsはuint64 bit field。bit0〜3をPrimary、Secondary、Thumbstick、Menu click、bit4〜7をPrimary、Secondary、Thumbstick、Trigger capacitive touchに割り当てる。

### OpenXR基盤

- Khronos OpenXR-SDK 1.1.61をcommit `5267613edf3d937e3d77556a106a65c2f82b25c6` に固定してCMake FetchContentで取得する。
- `maquestlink_openxr_layer` はloader interface v1をnegotiateし、未知の関数を下位runtimeへそのまま転送するpass-through API layer。
- build時に `build/openxr/1/api_layers/implicit.d/XrApiLayer_maquestlink.json` を生成する。
- implicit layerは `MAQUESTLINK_ENABLE_API_LAYER` が存在するときだけ有効、`MAQUESTLINK_DISABLE_API_LAYER` が存在すると無効。
- `maquestlink_native_test` は `XR_KHR_metal_enable` を使い、runtime提供Metal deviceでstereo array swapchainを作り、指定フレーム数を描画する。
- `scripts/test_phase1.sh` は `XR_RUNTIME_JSON` でStandalone Meta XR Simulatorを選び、`XDG_DATA_HOME=build` でリポジトリ内のimplicit manifestを検出させる。

### 映像ストリーミング

- layerは `xrCreateSession`、swapchainの作成・image列挙・acquireと `xrEndFrame` を横取りし、Metal command queueと提出textureを追跡する。
- client接続中だけ、左右眼のprojection image rectをIOSurface-backed BGRA pixel bufferへside-by-sideでMetal blitする。
- `xrEndFrame` 進入時のhost monotonic timestampと、左右 `XrCompositionLayerProjectionView` のpose/FOVを各 `VideoFrame` に格納する。
- VideoToolbox H.264 encoderはreal-time、frame reorder無効、Main profile、20 Mbps、60-frame key interval。出力はSPS/PPS付きAnnex B。
- TCP serverはUSBのadb reverseとWi-Fi直結の両方に対応するため、既定で `0.0.0.0:42424` をlistenする。`MAQUESTLINK_PORT` で変更できる。mock clientはloopbackで接続する。切断後は即座にコピー・エンコードを止めてpass-throughへ戻る。
- `maquestlink_mock_viewer` はprotocol受信、Annex B解析、VideoToolbox decode、metadata検証、decode fps集計を行う。
- `scripts/test_phase2.sh` は未接続60-frame pass-throughと、接続240-frame producer / 120-frame decoderの両方を検証する。

### pose・入力注入

- transportは単一TCP connectionを全二重で使い、Mac→clientのVideoFrameとclient→MacのPoseInputを同時に処理する。OpenXR instance破棄時にlistenerを停止・joinし、layer unload後にthreadを残さない。
- 最後に受信したPoseInputを各hookが参照する。接続切断時、またはローカル受信から500 ms経過したstale入力ではruntime結果を変更しない。
- `xrLocateViews` はHMD poseを中心として左右±32 mmのeye poseを返す。`xrLocateSpace` はVIEW reference spaceと左右action spaceを追跡し、base spaceからの相対poseへ変換する。
- `xrCreateActionSet` / `xrCreateAction` / `xrSuggestInteractionProfileBindings` を追跡し、actionと左右subaction path、binding componentを対応づける。
- `xrSyncActions` はruntimeへ転送し、`xrGetActionStateBoolean` / `Float` / `Vector2f` / `Pose` の成功結果を受信入力で差し替える。click/touch、trigger、squeeze、thumbstick、grip/aim poseを扱う。
- `changedSinceLastSync` はsession/action/subactionごとに前回返却値と比較する。入力中断時はruntime stateへ戻る。
- `maquestlink_mock_viewer --send-input` は既知の合成HMD/controller pose、button、thumbstick、trigger、gripを約90 Hzで送る。
- `scripts/test_phase3.sh` は映像decodeと同時に、合成値がview、action space、boolean/float/vector action stateへ反映されることを検証する。

### Questクライアント

- `quest-client/` はUnity 6000.3.6f1、Meta XR Core SDK 203.0.0、Unity Meta OpenXR 2.5.1を使うAndroid OpenXR project。
- MediaCodecへAnnex B access unitを投入し、Meta `OVROverlay` のcompositor-managed External Surfaceへ直接出力する。SBS映像の左右halfを各eyeへ割り当てる。
- MediaCodecはlow-latency modeを先に試し、未対応端末では通常modeへfallbackする。H.264とHEVCのprotocol codecに対応する。
- MVP画面はhead-fixedの3.2 m幅Quad。`OVRCameraRig` / `OVRManager`を持つgenerated sceneをCLI build時に生成する。
- HMDと左右controllerをUnity XR inputから毎Update取得し、OpenXR座標へ変換して最新PoseInputを送る。72 fpsを要求し、transportはbacklogを作らず最新sampleだけを送信する。
- 接続候補は`127.0.0.1`（`adb reverse tcp:42424 tcp:42424`）を先に試し、指定されたWi-Fi hostへfallbackする。切断後は500 ms間隔で自動再接続する。
- `adb shell am start` extrasでdiagnostic、host、Wi-Fi fallback、portを上書きできる。diagnostic modeは毎秒 `MAQUESTLINK_DIAGNOSTIC` JSONをlogcatへ出す。
- `scripts/test_quest_client.sh` はXR非依存のprotocol/transportをEditModeで検証し、`scripts/build_quest_client.sh` はIL2CPP/ARM64 APKを生成する。
- `scripts/e2e_device.sh` はinstall、adb reverse、無装着power automation、起動、Mac producer、logcat判定を自動化し、receive/decode 30 fps以上とPose送信60 Hz以上を要求する。

### Unityエディタ統合

- `editor-package/` はlocal UPM package `com.maquestlink.editor`。Unity起動時とPlay開始直前にlayer manifestを `$HOME/.local/share/openxr/1/api_layers/implicit.d` へ登録する。
- Play開始直前に `MAQUESTLINK_ENABLE_API_LAYER`、port、layer log、status JSONの環境変数を設定する。ADB deviceがあれば `adb reverse` を設定し、既定ではQuest clientも起動する。
- `Window > MaQuestLink` はQuest接続状態、fps、平均Metal copy / VideoToolbox encode合計時間、encoded frame数を表示する。layer登録、APK install、adb reverse、client起動を手動でも実行できる。
- native layerは `MAQUESTLINK_STATUS_FILE` 指定時、connection、encoded frames、fps、平均copy / encode / pipeline msを1秒周期でatomic JSON更新する。
- `samples/MetaXRMinimal/` はUnity 6000.3 project。Meta XR Core SDK 203.0.0の`OVRCameraRig`、左右Touch controllerの`OVRGrabber`、`OVRGrabbable` cubeを生成する。
- `scripts/test_phase5.sh` はsample生成、package manifest EditMode test、Meta XR Simulator上のPlayMode testを実行し、Unity applicationへのlayer loadと未接続待ち状態を検証する。PlayMode runはMetal graphics deviceが必要なため`-nographics`を使わない。

## 未実装

- world-fixed再投影、配布パッケージ、Phase 8のQuest拡張機能
