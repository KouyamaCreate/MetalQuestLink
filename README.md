# MaQuestLink

Apple Silicon Mac 上の Unity エディタPlayモードを Meta Quest 3 実機でリアルタイムにプレビュー・プレイできるようにするシステム（Windows専用の Quest Link Playモードプレビューの代替）。Meta XR Simulator を OpenXR ランタイムとして活かし、自作の OpenXR APIレイヤーで映像を Quest へストリーミングし、実機のトラッキング・入力をエディタへ差し戻す構成。

実装プラン（GPT-5.6 / Codex 実行用プロンプト）は [docs/plan.md](docs/plan.md)。

現在はPhase 5まで実装済み。Apple Silicon向け共有プロトコル、OpenXR API layer、
Meta XR Simulatorで動くMetal native test client、VideoToolbox H.264映像送信とmacOS
mock client、pose/action入力注入、MediaCodec + OVROverlayのQuest client、Unity Editor packageと
Meta XR minimal sampleがある。進捗と実測結果は
[docs/progress.md](docs/progress.md) と [docs/notes.md](docs/notes.md) を参照する。

開発ビルドとE2E:

```sh
cmake -B build
cmake --build build
ctest --test-dir build --output-on-failure
scripts/test_phase1.sh
scripts/test_phase2.sh
scripts/test_phase3.sh
scripts/test_quest_client.sh
scripts/build_quest_client.sh
scripts/test_phase5.sh
# Quest 3をUSB接続してUSB debuggingを許可した場合
scripts/e2e_device.sh
```

各E2Eは `/Applications/MetaXRSimulator.app` のStandalone Meta XR Simulator v201以降を
使う。`test_phase2.sh` は未接続パススルーと、左右眼side-by-side H.264のloopback
encode/decodeを自動検証する。
`test_phase3.sh` はmock clientが合成HMD/controller入力を送信し、OpenXR view/space/
action stateへ反映されることを映像streamと同時に検証する。
`test_quest_client.sh` はQuest側protocol/transportをUnity EditModeで検証し、
`build_quest_client.sh` はUnity 6000.3.6f1でIL2CPP/ARM64 APKを生成する。
`test_phase5.sh` はEditor packageとsample sceneを検証し、Meta XR Simulator PlayModeで
layer loadとQuest接続待ち状態を確認する。
実機未接続時の残作業は `scripts/e2e_device.sh` の1コマンドだけである。

Editorでの最短手順は、Package Managerから `editor-package/` を導入し、
`Window > MaQuestLink` の `Install Quest APK` を1回押してからPlayする。layer登録、
`adb reverse`、client起動は自動で行われる。最小sceneは `samples/MetaXRMinimal/` にある。

実行方法:

```sh
cd /Users/koseiyamamoto/Documents/GitHub/MaQuestLink
codex exec --skip-git-repo-check - < docs/plan.md
```
