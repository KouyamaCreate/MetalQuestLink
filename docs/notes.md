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
