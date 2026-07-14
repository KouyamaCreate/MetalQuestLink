# MaQuestLink

Apple Silicon Mac 上の Unity エディタPlayモードを Meta Quest 3 実機でリアルタイムにプレビュー・プレイできるようにするシステム（Windows専用の Quest Link Playモードプレビューの代替）。Meta XR Simulator を OpenXR ランタイムとして活かし、自作の OpenXR APIレイヤーで映像を Quest へストリーミングし、実機のトラッキング・入力をエディタへ差し戻す構成。

実装プラン（GPT-5.6 / Codex 実行用プロンプト）は [docs/plan.md](docs/plan.md)。

現在はPhase 1まで実装済み。Apple Silicon向け共有プロトコル、OpenXR pass-through
API layer、Meta XR Simulatorで動くMetal native test clientがある。進捗と実測結果は
[docs/progress.md](docs/progress.md) と [docs/notes.md](docs/notes.md) を参照する。

開発ビルドとPhase 1 E2E:

```sh
cmake -B build
cmake --build build
ctest --test-dir build --output-on-failure
scripts/test_phase1.sh
```

`test_phase1.sh` は `/Applications/MetaXRSimulator.app` のStandalone Meta XR Simulator
v201以降を使う。

実行方法:

```sh
cd /Users/koseiyamamoto/Documents/GitHub/MaQuestLink
codex exec --skip-git-repo-check - < docs/plan.md
```
