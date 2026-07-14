# MaQuestLink 仕様

## 実装済み

### 共有プロトコル v1

- TCPストリーム上で使う、長さプレフィックス付きのlittle-endianバイナリ形式。
- 20 byteの共通ヘッダーは、magic、protocol version、message type、payload length、sequence numberを持つ。
- payloadの最大長は64 MiB。magic、version、message type、宣言長、メッセージ固有の可変長データをデシリアライズ時に検証する。
- message type:
  - VideoFrame: capture timestamp、render pose、画面寸法、codec、eye count、flags、エンコード済み映像
  - PoseInput: sample timestamp、HMD pose、左右コントローラpose、buttons、thumbstick、trigger、grip
  - Control: kind、flags、timestamp、拡張用データ
- codecはH.264とHEVC、control kindはhello/ack、stream開始/停止、ping/pong、disconnectを予約する。

### OpenXR基盤

- Khronos OpenXR-SDK 1.1.61をcommit `5267613edf3d937e3d77556a106a65c2f82b25c6` に固定してCMake FetchContentで取得する。
- `maquestlink_openxr_layer` はloader interface v1をnegotiateし、未知の関数を下位runtimeへそのまま転送するpass-through API layer。
- build時に `build/openxr/1/api_layers/implicit.d/XrApiLayer_maquestlink.json` を生成する。
- implicit layerは `MAQUESTLINK_ENABLE_API_LAYER` が存在するときだけ有効、`MAQUESTLINK_DISABLE_API_LAYER` が存在すると無効。
- `maquestlink_native_test` は `XR_KHR_metal_enable` を使い、runtime提供Metal deviceでstereo array swapchainを作り、指定フレーム数を描画する。
- `scripts/test_phase1.sh` は `XR_RUNTIME_JSON` でStandalone Meta XR Simulatorを選び、`XDG_DATA_HOME=build` でリポジトリ内のimplicit manifestを検出させる。

## 未実装

- 映像エンコード・転送、実機入力注入
- Questクライアント、Unityエディタ統合、配布パッケージ
