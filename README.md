# MaQuestLink

Apple Silicon Mac 上の Unity エディタPlayモードを Meta Quest 3 実機でリアルタイムにプレビュー・プレイできるようにするシステム（Windows専用の Quest Link Playモードプレビューの代替）。Meta XR Simulator を OpenXR ランタイムとして活かし、自作の OpenXR APIレイヤーで映像を Quest へストリーミングし、実機のトラッキング・入力をエディタへ差し戻す構成。

実装プラン（GPT-5.6 / Codex 実行用プロンプト）は [docs/plan.md](docs/plan.md)。

実行方法:

```sh
cd /Users/koseiyamamoto/Documents/GitHub/MaQuestLink
codex exec --skip-git-repo-check - < docs/plan.md
```
