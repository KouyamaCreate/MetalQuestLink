# 実装進捗

最終更新: 2026-07-15

| Phase | 状態 | 検証 |
|---|---|---|
| 0 — リポジトリ基盤とプロトコル | 完了 | build成功、ctest 1/1成功、arm64確認 |
| 1 — 技術検証スパイク | 未着手 | — |
| 2 — 映像パイプライン | 未着手 | — |
| 3 — 入力パイプライン | 未着手 | — |
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
