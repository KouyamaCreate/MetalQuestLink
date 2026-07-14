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
