# MaQuestLink

Apple Silicon Mac 上の Unity エディタPlayモードを Meta Quest 3 実機でリアルタイムにプレビュー・プレイできるようにするシステム（Windows専用の Quest Link Playモードプレビューの代替）。Meta XR Simulator を OpenXR ランタイムとして活かし、自作の OpenXR APIレイヤーで映像を Quest へストリーミングし、実機のトラッキング・入力をエディタへ差し戻す構成。

実装プラン（GPT-5.6 / Codex 実行用プロンプト）は [docs/plan.md](docs/plan.md)。

現在はPhase 3まで実装済み。Apple Silicon向け共有プロトコル、OpenXR API layer、
Meta XR Simulatorで動くMetal native test client、VideoToolbox H.264映像送信とmacOS
mock client、Quest相当のpose/action入力注入がある。進捗と実測結果は
[docs/progress.md](docs/progress.md) と [docs/notes.md](docs/notes.md) を参照する。

開発ビルドとE2E:

```sh
cmake -B build
cmake --build build
ctest --test-dir build --output-on-failure
scripts/test_phase1.sh
scripts/test_phase2.sh
scripts/test_phase3.sh
```

各E2Eは `/Applications/MetaXRSimulator.app` のStandalone Meta XR Simulator v201以降を
使う。`test_phase2.sh` は未接続パススルーと、左右眼side-by-side H.264のloopback
encode/decodeを自動検証する。
`test_phase3.sh` はmock clientが合成HMD/controller入力を送信し、OpenXR view/space/
action stateへ反映されることを映像streamと同時に検証する。

実行方法:

```sh
cd /Users/koseiyamamoto/Documents/GitHub/MaQuestLink
codex exec --skip-git-repo-check - < docs/plan.md
```
