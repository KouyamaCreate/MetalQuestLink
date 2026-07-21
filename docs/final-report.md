# MetalQuestLink 最終レポート

最終更新: 2026-07-21

## 結論

Phase 0〜8の実装、実機不要の全回帰、Quest 3実機E2Eが完了した。
最終配布APKの実機結果は`METALQUESTLINK_DEVICE_E2E_OK`。映像受信74 fps、decode 76 fps、Pose 73 Hz、
capture-to-decode 140.498283 ms、hand message 2,113件、haptic 34件、immersive projection / clock sync / Passthroughを同時に確認した。
追加の装着テストでは左右52関節の追跡、診断hand skeletonの指開閉、Passthrough表示を目視確認した。
その後、有限QuadをOpenXR stereo projectionへ置き換え、Quest 3装着時に視野全体を覆う一人称表示も目視確認した。

## できたこと

- Apple Silicon macOSのUnity Play ModeをMeta XR Simulatorで動かすimplicit OpenXR API layer。
- 左右眼3360x1760 SBS H.264のVideoToolbox → TCP → Quest MediaCodec External Surface経路。
- QuestのHMD / Touch入力をMac OpenXR applicationのview / space / actionへ差し戻す経路。
- OpenXR stereo projection再投影（拡張非対応時はworld-fixed Quad fallback）とMac / Quest monotonic clock同期、段階別latency診断。
- 自己完結UPM package、arm64 ad-hoc signed layer、Quest ARM64 APK、checksum付きrelease。
- Phase 8のcontroller haptics、`XR_EXT_hand_tracking`左右26関節、Passthrough underlay + uniform alpha 0.82近似。
- Quest 3実機上のMediaCodec Surface release、全二重Pose / hand送信、haptic command受信、Passthrough設定。
- 既存Unity projectのPlayからAPKをbuildせず、インストール済みQuest clientを自動起動する即時preview導線。
- Unity 6000.2 / 6000.3、既存XR loader、相対path、複数Quest、USB / Wi-Fi、Single Pass / Multi Pass相当のswapchain差を吸収するproject preflightと設定。
- 解像度連動bitrate、encode待ち上限、frame dropによる遅延蓄積防止、drop数とstream解像度のEditor診断。
- READMEの第三者向け導入手順、Quest機能対応表、既知の制約、troubleshooting。

## 受け入れ基準

| Phase | 結果 | 最終検証 |
|---|---|---|
| 0 | 達成 | CMake build、ctest 1/1、arm64 |
| 1 | 達成 | Meta XR Simulator Metal frame loop、layer load、120 frames以上 |
| 2 | 達成 | 未接続pass-through、H.264 120/120 decode、30 fps以上 |
| 3 | 達成 | 合成view / space / action注入、全二重stream、clock sync |
| 4 | 達成 | Quest EditMode 12/12、Unity IL2CPP / ARM64 APK build、実機receive 74 fps / decode 76 fps / Pose 73 Hz |
| 5 | 達成 | Editor package EditMode 9/9、Simulator PlayMode 1/1、Play時Quest client自動起動 |
| 6 | 達成 | world-fixed pose / clock unit、capture診断、Phase 0〜5回帰、実機clock sync / capture-to-decode |
| 7 | 達成 | release 4点、checksum、doctor error 0、repository外tarball Unity/Simulator E2E |
| 8 | 達成 | haptic値、左右26 joint、Passthrough flag / alpha 0.82、Phase 0〜7回帰、Quest 3実機E2E |

Phase 8の最終mock結果は120/120 decode、76.6143 fps、input 163 samples、clock sync、
`haptic_apply=1`、`haptic_stop=1`、`passthrough=1`、`passthrough_alpha=0.82`。
native側は`hands=1 haptics=1`。

汎用パッケージ化後は通常の同一2D-arrayと左右別2D swapchainをそれぞれVideoToolboxでdecodeし、
Editor 9/9、PlayMode 1/1、Quest 12/12、repository外tarball Unity/Simulator E2E、doctor error 0を確認した。
最終全回帰の実機以外は成功した。最後のdevice runだけ、未装着QuestのscreenがOFFとなりOpenXR sessionが
90秒以内に開始しなかったため未完了。直前までの実機E2Eと装着目視結果は上記のとおり成功済み。

## 配布物

`dist/`のMetalQuestLink 0.2.0最終4点:

- `com.metalquestlink.editor-0.2.0.tgz` — 38,608,114 bytes — SHA-256 `31c102cc98d48ea2bb2ab3dbcd80f3e270c3858ef54849c1ca7a58c72ab0e5b2`
- `MetalQuestLink-0.2.0.apk` — 43,721,918 bytes — SHA-256 `93a608f7540776e24646cf7cf3629ad1cb474b56f39537706254e8bfb6a80bb2`
- `SHA256SUMS`
- `VERSION` (`0.2.0`)

`shasum -a 256 -c SHA256SUMS`は全対象で成功した。

## できていないこと

- Touch Plusの振動強度、周波数、duration停止の体感。
- 高負荷時の映像乱れを含む長時間性能測定と、未装着でscreen OFFになった場合の自動実機回帰。
- Play中にeye texture解像度／codecを変更する動的decoder再構成。
- Apple Developer ID署名／公証、GitHub公開／push、release tag、CI secrets設定。

シーンアンカー、空間メッシュ、アイ／フェイストラッキング、画素単位alphaは計画どおり対応外。

## ユーザーにしかできない残作業

1. Touch Plusを用意できたら振動の強度、周波数、duration停止を体感確認する。
2. 公開する場合は修正commitへ`v0.2.0` tagを付けてpushし、GitHub ActionsへUnity license secretsを設定する。必要ならユーザー所有のDeveloper IDで署名・公証する。

実機E2Eが失敗した場合は、出力された`build/phase4-device-logcat-*.log`と
`build/phase4-device-producer-*.log`を確認する。
