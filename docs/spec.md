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

## 未実装

- OpenXR APIレイヤーとMeta XR Simulator接続
- 映像エンコード・転送、実機入力注入
- Questクライアント、Unityエディタ統合、配布パッケージ
