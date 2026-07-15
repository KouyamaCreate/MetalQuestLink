# 調査・設計メモ

## 2026-07-15 — Phase 0

- wire formatはC++/C#/Java間で再現しやすい固定幅little-endianとした。
- 通信路がTCPのため、共通ヘッダーにpayload lengthを持たせ、1メッセージずつ復元できるようにした。
- OpenXR固有の構造体をwire formatへ直接埋めず、position、quaternion、valid/tracked flagsを明示的に表現した。SDKのABI差に依存しないため。
- hand trackingは現時点でスコープ外。新しいmessage typeまたはcontrol dataで拡張できる。

## 2026-07-15 — Phase 1 実測

### Meta XR Simulatorの導入形態

- Meta公式の現行手順はStandalone XR Simulator。旧 `com.meta.xr.simulator` Unity packageは非推奨。
- 公式macOS ARM版 v201.0（build `201.0.0.25.633`）をDMGから `/Applications/MetaXRSimulator.app` へ導入した。
- DMG SHA-256: `7aa29d8a2c89aeb34c3256d2b8d1ebc78e96492c7b651afa072587f28d690d91`。
- appとruntime `SIMULATOR.so` はarm64。appのcode signatureを `codesign --verify --deep --strict` で検証済み。
- runtime manifestはapp bundle内の `Contents/Resources/MetaXRSimulator/meta_openxr_simulator.json`、runtime libraryは同階層の `SIMULATOR.so`。
- 公式資料: [Get Started with Meta XR Simulator](https://developers.meta.com/horizon/documentation/unity/xrsim-getting-started/)、[Native setup](https://developers.meta.com/horizon/documentation/native/xrsim-getting-started/)、[macOS ARM download](https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-mac-arm/)。

### macOS OpenXR loaderの探索パス

- Khronos OpenXR-SDK 1.1.61 commit `5267613edf3d937e3d77556a106a65c2f82b25c6` のloaderをビルド・実行した。
- global active runtimeは `/usr/local/share/openxr/1/active_runtime.json`。Simulator同梱のactivate scriptも同じ場所へsymlinkを置く。
- `XR_RUNTIME_JSON` はglobal設定を上書きする。E2Eでは管理者権限を要求しないようapp bundle内manifestを直接指定した。
- macOSのAPI layer探索はXDG/Unix系の探索処理を使う。implicit suffixは `openxr/1/api_layers/implicit.d`、explicit suffixは `openxr/1/api_layers/explicit.d`。
- 標準候補は `/etc`、`/usr/local/share`、`/usr/share`、`$HOME/.local/share` とXDG環境変数由来のパス。
- `XR_API_LAYER_PATH` はexplicit layerだけを上書きする。implicit layerの検証には `XDG_DATA_HOME=<repo>/build` と `build/openxr/1/api_layers/implicit.d` を使った。
- `MAQUESTLINK_ENABLE_API_LAYER=1` でmanifestが有効になり、loader logに `succeeded loading layer XR_APILAYER_MAQUESTLINK_streaming` が出た。
- 一次資料: [OpenXR loader design](https://registry.khronos.org/OpenXR/specs/1.1/loader.html)、Khronos source `src/loader/manifest_file.cpp` / `src/common/platform_utils.hpp`。

### Metal拡張

- 正式な拡張名は `XR_KHR_metal_enable` revision 3。
- graphics bindingは `XrGraphicsBindingMetalKHR`（`commandQueue`）、runtime requirementsは `XrGraphicsRequirementsMetalKHR`（`metalDevice`）、swapchain imageは `XrSwapchainImageMetalKHR`（`texture`）。各object pointerのAPI型は `void*`。
- Simulator v201は `XR_KHR_metal_enable` と旧 `XR_KHRX2_metal_enable` の両方を列挙し、正式拡張でsession作成に成功した。
- 実測runtimeは `Meta XR Simulator 201.0.0`。runtime提供deviceは `Apple M4 Pro`、session logは `GraphicsApi=Metal`。
- Simulator内部は `RenderingMetalOnVulkan` を有効化し、Metal/Vulkan interopを使っている。
- native testでstereo array swapchainを取得し、120フレームのclear描画とprojection layer提出に成功した。
- 一次資料: [XR_KHR_metal_enable](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XR_KHR_metal_enable.html)、Khronos `hello_xr` の `graphicsplugin_metal.cpp`。

### 判明した制約

- Simulator v201 runtimeは `xrDestroyInstance` 後のprocess-exit時、内部gRPC static destructorで終了待ちになる場合がある。stack sampleで `ServiceManager::~ServiceManager` → `grpc::Server::ShutdownInternal` の待機を確認した。
- native testは全OpenXR resourceを明示破棄して成功ログをflushした後、`std::_Exit` でvendor runtimeのstatic destructorだけを迂回する。Unity Editorのような長寿命processではprocess-exitだけの問題なので通常のPlay loopには影響しないと判断した。
- OVRPluginの要求拡張一覧はnative clientでは観測できない。Meta XR SDK/Unity側の起動を組み込むPhase 1〜3の後続で実測する。

## 2026-07-15 — Phase 2 実測

### 映像形式とmetadata

- Meta XR Simulatorのstereo array swapchainから左右眼をMetal blitし、3360x1760のside-by-side BGRA frameとしてVideoToolboxへ入力した。
- encoderはH.264 real-time、B-frameなし、Main profile、20 Mbpsとした。mock viewerがSPS/PPSと120 access unitをVideoToolboxで全てデコードした。
- 各VideoFrameには `xrEndFrame` hook進入時のhost monotonic ns、左右眼それぞれのprojection pose/FOVを格納する。runtime固有epochの `XrFrameEndInfo::displayTime` は、Mac/Quest間の遅延基準に使えないためwireへ追加していない。

### 性能

- 実行コマンド: `scripts/test_phase2.sh`。
- 環境: Apple M4 Pro、Meta XR Simulator 201.0.0、3360x1760 side-by-side H.264。
- mock viewer: received 120 / decoded 120、66.8345 fps。受け入れ基準の30 fps以上を満たした。
- 120-frame時点の平均Metal copy時間: 4.02939 ms。計測区間は `xrEndFrame` hook進入からpixel buffer作成とblit完了まで。
- 120-frame時点の平均VideoToolbox encode時間: 16.1017 ms。計測区間はblit完了からcompression callbackまで。
- copy + encode合計平均: 20.13109 ms。
- viewer未接続時は60-frame loopを完走した。接続判定より先ではCVPixelBuffer作成、Metal copy、VideoToolbox encodeを行わない。

### 現時点の制約

- Phase 2の映像抽出は、左右眼が同じ2D-array swapchainにあり、BGRA8UnormまたはBGRA8Unorm_sRGBである構成を対象とする。Meta XR Simulator native E2Eではこの構成を実測確認した。Unity/OVRPluginの実際のswapchain構成はPhase 5で検証する。

## 2026-07-15 — Phase 3 実測

### Meta Touch Plus interaction profile

- OpenXR 1.1へpromoteされた現行profile pathは `/interaction_profiles/meta/touch_plus_controller`。
- OpenXR 1.0の `XR_META_touch_controller_plus` extensionが定義した旧pathは `/interaction_profiles/meta/touch_controller_plus`。promote時に語順が変更されている。
- native testはOpenXR 1.1を要求し、現行pathへの左右grip pose、X/A click、trigger value、thumbstick binding suggestionに成功した。
- Simulator logで左右 `/user/hand/*/interaction_profiles/meta/touch_plus_controller` へのinteraction profile changeを実測した。
- componentはKhronos OpenXR-SDK 1.1.61の `specification/registry/xr.xml` と[OpenXR 1.1仕様](https://registry.khronos.org/OpenXR/specs/1.1-khr/html/xrspec.html)で確認した。

### 合成入力E2E

- 実行コマンド: `scripts/test_phase3.sh`。
- mock clientはHMD `(1, 2, 3)`、左controller `(-0.25, 1.25, -0.5)`、Primary click、thumbstick `(0.25, -0.5)`、trigger `0.75`、grip `0.5`を送信した。
- 最終runで138 PoseInput samplesを送信した。`xrLocateViews` の両眼center、`xrLocateSpace` の左action space、`xrGetActionStateBoolean` / `Float` / `Vector2f` の全合成値が一致した。
- 同じconnectionで3360x1760 H.264をreceived 120 / decoded 120、76.4866 fpsで処理した。映像と入力の全二重動作を確認した。
- `scripts/test_phase2.sh` も再実行し、viewer未接続60-frame pass-throughと映像120-frame decodeの回帰がないことを確認した。

### 入力の安全側動作

- connection切断時は最新入力を破棄する。connectionが残っていても最後の受信から500 msを超えたPoseInputは無効とし、Simulatorのruntime値をそのまま返す。
- protocol v1はclick 4種に加えcapacitive touch 4種のbitを確保した。mock E2Eではclick/analogを検証し、Questからのtouch取得はPhase 4で実装する。
- controller pose protocolは左右1 poseずつのため、grip poseとaim poseへ同じ値を返す。別poseが必要になった場合はprotocol拡張が必要。
- `xrLocateViews` のeye offsetは現時点で固定IPD 64 mm。実機HMDから左右eye transformを送らないMVP仕様であり、Phase 6の再投影検証時に必要なら調整する。

## 2026-07-15 — Phase 4 実測

### Unity / Meta XR構成

- Unityは指定どおり6000.3.6f1、Android Build Support同梱のSDK 36 / NDK / OpenJDKを使用した。
- Meta公式npm registryのMeta XR Core SDK 203.0.0を採用した。package tarball SHA-1は `3196ba8fc3d47351251e3b6c021371bf77ccaf17`。
- registryからの183 MiB取得が極端に低速だったため、検証runは同じ公式tarballをSHA-1確認後にローカル展開して実行した。commitする`manifest.json` / `packages-lock.json`は公開registryのversion `203.0.0`指定へ戻しており、一時pathへの依存はない。
- Unity Meta OpenXR 2.5.1、OpenXR Plugin 1.15.1を使用した。Android OpenXR loader、Meta Quest Support、Oculus Touch / Meta Quest Touch Plus profile、Composition Layers Supportをbatch build時に有効化する。
- APKはQuest 3 (`eureka`) / Quest 3S (`quest3s`) のみを対象とする。Meta Quest featureの既定値のままQuest Proも対象にするとeye-tracking必須feature/permissionがmanifestへ入るため、明示的に除外した。

### OVROverlay External Surface

- Core SDK 203.0.0の `OVROverlay` は `isExternalSurface`、pixel width/height、JNI Surface jobjectを示す `IntPtr externalSurfaceObject` を公開する。
- compositorが作ったjobjectをUnityの `AndroidJavaObject(IntPtr)` で保持し、MediaCodecのoutput Surfaceとしてconfigureする。Unity textureへのcopyは行わない。
- side-by-sideは `srcRectLeft=(0,0,0.5,1)`、`srcRectRight=(0.5,0,0.5,1)` と `overrideTextureRectMatrix=true` で指定する。
- MVP表示はCenterEye cameraの子に置く3.2 m幅、2 m先のhead-fixed Quad。Phase 6でrender poseを使うworld-fixed reprojectionを既定化する。

### デコードと診断

- MediaCodecはH.264 `video/avc` またはHEVC `video/hevc`、`low-latency=1`、priority 0を要求する。端末がlow-latency keyを拒否した場合は同じSurfaceへ通常modeで再configureする。
- `MAQUESTLINK_DIAGNOSTIC` にconnected、累積received/decoded/poses、1秒区間のreceive/decode fps・pose Hz、drop数、low-latency要求結果をJSON出力する。
- Android extrasは `maquestlink_diagnostic`、`maquestlink_host`、`maquestlink_wifi_host`、`maquestlink_port`。

### 検証結果と未実測

- Unity EditMode testは4/4成功。C++ protocolと同じ20-byte header、152-byte PoseInput、120-byte固定部+映像dataのVideoFrameを確認した。
- APKはUnity batch modeで生成成功。42 MiB、IL2CPP / arm64-v8a、minSdk 32、targetSdk 36、GameActivityをaaptで確認した。
- Quest実機は未接続。このためMediaCodecの`low-latency`実受理、OVROverlayへの実表示、無装着時のpower-manager broadcast結果、実測30 fps / 60 Hzは未確認。`scripts/e2e_device.sh` は接続後にinstall、adb reverse、power automation、診断判定、後片付けまで自動実行する。

## 2026-07-15 — Phase 5 実測

### Unity Editor / Meta XR Simulator

- Unity 6000.3.6f1 PlayModeからMeta XR Simulator 201.0.0へ接続し、OVRPluginの`CompositorOpenXR::Initialize()`成功とMetal `XrSession`作成を確認した。
- layer logはapplication名 `Oculus VR Plugin (Unity 6000.3.6f1 [Editor])` と `Unity Application` のinstance load / destroyを記録した。
- Unity OpenXR diagnostic reportにも `XR_APILAYER_MAQUESTLINK_streaming` が列挙された。
- Quest未接続時のstatus JSONは `connected=false`、encoded frames / fps / copy / encode / pipelineが0。PlayMode testはこの接続待ち状態を確認した。
- PlayMode E2Eへ `-nographics` を付けるとNull graphics deviceとなり、`xrCreateSession` が `XR_ERROR_GRAPHICS_DEVICE_INVALID` を返す。Meta XR SimulatorのMetal sessionを検証するrunではgraphics deviceを有効にする必要がある。

### package導入と検証

- package load時にlayer manifestを自動登録し、Play開始直前にも再確認する。ADB deviceがない場合もlayer側はlistenを開始し、Playは継続する。
- Meta XR Core SDK 203.0.0は公式tarballのローカル展開を検証時だけ使用した。`samples/MetaXRMinimal/Packages/manifest.json` は公開registryの`203.0.0`指定へ復元される。
- `scripts/test_phase5.sh` 最終runはEditor package EditMode 1/1、sample PlayMode 1/1 passed。sample内で`OVRCameraRig`と`OVRGrabbable`の存在も確認した。
- Quest実機は未接続のため、Editor windowがconnected状態へ遷移し、実fps/latencyを表示する経路は未実測。

## 2026-07-15 — Phase 6 実測

### world-fixed再投影

- Macが送る左右eye render poseは、Questから送ったtracking poseをlayerがUnity OpenXRへ注入した結果と同じtracking origin上にある。
- Quest側で左右position / orientationを平均し、render head poseの2 m前へExternal Surface Quadをworld配置する。frame間はtransformをheadへ追従させないため、Meta compositorが現在head poseとの差を再投影する。
- pure EditMode testでOpenXR position `(-0.032,1,-2)` / `(0.032,1,-2)` がUnity world center `(0,1,2)`、Quad position `(0,1,4)`になること、invalid poseを拒否することを確認した。
- Quest実機がないため、頭を動かした際の見え方とcompositorへの実表示は未確認。

### clock同期と計測範囲

- MacとQuestの`steady_clock` / `Stopwatch`はepochが異なる。Control PingはQuest送信時刻、PongはMac受信時刻とechoしたQuest送信時刻を持つ。
- Questは往復時間の中央をMac受信瞬間とみなし、`host - client` offsetを1秒周期で更新する。USB / LANの往復非対称性は誤差として残る。
- `capture_to_receive_ms` はMac `xrEndFrame` hookからQuest TCP deserializeまで、`capture_to_decode_ms` はMediaCodec outputをExternal Surfaceへreleaseした直後まで。
- Editor / doctor向けhost status JSONは後方互換判定用のschema `version: 1`を持つ。
- display scanout / panel発光は観測していないため、motion-to-photon値とは呼ばない。
- native mock E2EはPingにPongが返ることを映像・入力と同じconnection上で確認した。Quest EditModeはclock offset / RTT / age換算を既知値で確認した。

### Phase 6回帰run

- `scripts/test_phase3.sh`: 3360x1760 H.264 received / decoded 120/120、76.2646 fps、PoseInput 136 samples、`clock_sync=1`。
- 120-frame平均Metal copy 1.82951 ms、VideoToolbox encode 15.5753 ms、合計17.40481 ms。
- Quest EditMode 7/7、IL2CPP / ARM64 APK build成功。
- Phase 0〜5のMac/Simulator/Unity検証を最終状態で再実行し成功。device E2EだけはQuest未接続のためexit 2。
- `scripts/test_quest_client.sh` / `build_quest_client.sh` / `test_phase5.sh` は公式Meta XR Core 203.0.0 tarballのローカル展開がある場合だけ一時利用し、終了時に公開registry manifest / lockへ戻す。

## 2026-07-15 — Phase 7 配布検証

### 自己完結package

- `OpenXRLayerInstaller`の探索先をpackage内`Native~/macOS`と`QuestClient~`だけに限定した。repository内buildへのfallbackはない。
- 同梱layerはMach-O bundle arm64、760 KiB。`codesign --verify --strict`でad-hoc署名を確認した。
- 同梱APKは42 MiB。Unity SDKの`aapt`でpackage `com.maquestlink.questclient`、version `0.1.0`を確認した。
- UPM tarballは約37 MiB。repository外へ展開したpackageにnative layer、manifest、APKがあり、source textにrepository build path依存がないことを確認した。

### release / doctor

- `scripts/test_phase7.sh`はUPM tarball、standalone APK、VERSIONのSHA-256を生成直後と展開前に検証して成功した。
- repository外packageを対象に`doctor.sh --register`を実行し、error 0。Apple Silicon、macOS 26.4.1、layer architecture / signature、package/APK version、manifest、Meta XR Simulator、adbを確認した。
- Simulator停止とQuest未接続は利用準備を妨げないため警告。Quest接続時は端末上の`com.maquestlink.questclient` versionもpackage versionと比較する。
- `scripts/test_phase7_clean.sh`はtarballだけを参照するrepository外の一時Unity sampleを作り、EditMode / Meta XR Simulator PlayMode、layer load、接続待ち、doctor error 0を確認して成功した。
- 初回実行では過去の並行batchmode実行が残したorphanのUnity Licensing ClientによりIPC再接続が停止した。stale processだけを終了し、license cacheは変更せず再実行して成功した。

## 2026-07-15 — Phase 8 実測

### ハプティクス

- Meta Touch bindingの`/output/haptic`へ紐づくaction/subactionを既存binding追跡から特定できた。OpenXR runtime呼び出しが成功した場合だけHapticCommandを送る。
- native testがleft controllerへamplitude 0.6、frequency 120 Hz、duration 20 msをapplyしてstopし、mock clientが値と順序を検証した。
- Quest側は`OVRInput.SetControllerVibration`を使う。frequencyはQuest APIの0〜1へ0〜320 Hz基準で正規化し、duration満了もclient側で停止する。実機の振動強度／周波数感は未実測。

### ハンドトラッキング

- Unity XR Hands 1.7.2のOpenXR providerは`XR_EXT_hand_tracking`で26関節を取得する。Meta XR Core 203.0.0のproject capabilityをControllersAndHands / HIGHに設定した。
- Unity OpenXRのMicrosoft Hand Interaction ProfileとXR Hands Hand Tracking Subsystemは同じfeature ID `com.unity.openxr.feature.input.handtracking`を持つ。ID検索では前者を誤選択したため、build設定は`UnityEngine.XR.Hands.OpenXR.HandTracking`型で後者を選ぶ。生成したAndroid assetで前者0、後者1を確認した。
- XR Hands joint IDはPalm / WristだけOpenXR順とUnity enum値が逆で、それ以降はOpenXR index + 1。unit testでPalm 0→2、Wrist 1→1、index 10→11を固定した。
- layer manifestの`instance_extensions`で`XR_EXT_hand_tracking`をloader列挙へ追加し、system support、create/locate/destroyをhookする。mock 26関節がnative testの`xrLocateHandJointsEXT`へ反映され、`hands=1`を確認した。
- 基本joint取得に`XR_FB_hand_tracking_aim`などのMeta追加拡張は不要。Meta aim gestureやmeshは提供していないため、それらを要求するアプリはMaQuestLink経由では利用できない。

### パススルー近似

- Mac layerはenvironment blend modeがalpha/additive、またはprojection layerがsource-alpha flagを持つ場合にPassthrough flagを送る。
- VideoToolbox H.264 / Quest MediaCodec経路は画素alphaを保持しない。実機なしではHEVC alpha support、premultiplied挙動、black key品質を実測できないため、protocol v1はunderlay + overlay全体の固定alpha 0.82を採用した。
- mock E2EでPassthrough flagを復元し、protocol semanticのalpha 0.82を出力して検証した。Quest EditModeも同じalpha定数を検証する。
- 実機でunderlayが表示されること、External Surface overlayのcolor scaleが意図どおり合成されること、視認性は未実測。

### Phase 8検証

- `scripts/test_phase3.sh`: 3360x1760 H.264 received / decoded 120/120、76.5903 fps、PoseInput 173 samples、clock sync、hand joints、haptic apply/stop、passthrough近似が成功。
- Quest EditMode 9/9、Unity 6000.3.6f1 IL2CPP / ARM64 APK buildが成功。Phase 8 release APKのSHA-256は`51798579417bc867e1cd9c0b42c6299f5d6d498ba232feceaeae3803ad02f3ff`。
- 最終`test_phase8.sh`はPhase 0〜7回帰、release checksum、doctor error 0、repository外tarball Unity/Simulator E2Eまで成功。Phase 3最終runは120/120、76.6143 fps、input 163 samples。
- Quest未接続のためdevice E2Eはexit 2を期待値として扱い、実機が必要な4項目は最終レポートへ分離する。

## 2026-07-15 — Quest 3実機追試

- USB debuggingを承認したQuest 3 (`eureka`)へ修正版APKをinstallし、`scripts/e2e_device.sh`が成功した。
- 最終配布APKの値はreceive最大74 fps、decode最大76 fps、Pose最大73 Hz、健全なstream中のcapture-to-decode 140.498283 ms、hand message 2,113件、haptic command 34件、Passthrough有効。
- 実機修正後の配布APK SHA-256は`ea8f0ed6420dc0a4144b54e0357acbd430b3a0734e12076151434c1587a8f25a`、UPM tarballは`0d4a5284e9732a5247819cefc8df54a163d42e29f0ed9d2dc91a8f82d0a35518`。checksum検証は全3対象で成功した。
- 初回APKではgenerated sceneの`QuestClientController.presenter`が未設定で、`EnablePassthrough` / `EmitDiagnostic`がNullReferenceExceptionになった。scene生成順序とserialized参照を修正し、runtime resolverも追加した。
- device scriptが合成入力用の固定pose値を実機にも要求していたため、実機専用modeを追加した。実機modeはQuest診断のPose / hand rateとOpenXR haptic往復で判定する。
- hapticの初回命令はQuest接続成立前に破棄されたため、実機test中だけapply / stopを周期再送する。製品runtimeのhaptic挙動には影響しない。
- 自動E2EはMediaCodec Surface releaseと設定値を検証する。装着時の光学的表示品質、実jointの見え方、振動体感、Passthrough視認性は未確認。

## 2026-07-15 — 装着ハンド／パススルー追試とPlay統合

- 診断用hand skeletonを有効にして装着テストし、ユーザーが手の認識と開閉追従、Passthrough表示を確認した。
- 自動判定は`active_hand_joints=52`、`hands_sent=2465`、`passthrough=1`、receive 74 fps、decode 76 fps、Pose 74 Hzで成功した。
- Unity sampleのEditMode 4/4、PlayMode 1/1が成功し、Play hookの`adb reverse`、Quest client起動、Passthrough / hands指定を確認した。
- Quest activityがPlay hookからresumedになり、OVRPluginのPassthrough初期化とcamera stream開始をlogcatで確認した。
