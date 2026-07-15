# MaQuestLink 最終レポート

最終更新: 2026-07-15

## 結論

Phase 0〜8の実装、実機不要の全回帰、Quest 3実機E2Eが完了した。
最終配布APKの実機結果は`MAQUESTLINK_DEVICE_E2E_OK`。映像受信74 fps、decode 76 fps、Pose 73 Hz、
capture-to-decode 140.498283 ms、hand message 2,113件、haptic 34件、world-fixed / clock sync / Passthroughを同時に確認した。

## できたこと

- Apple Silicon macOSのUnity Play ModeをMeta XR Simulatorで動かすimplicit OpenXR API layer。
- 左右眼3360x1760 SBS H.264のVideoToolbox → TCP → Quest MediaCodec External Surface経路。
- QuestのHMD / Touch入力をMac OpenXR applicationのview / space / actionへ差し戻す経路。
- world-fixed compositor再投影とMac / Quest monotonic clock同期、段階別latency診断。
- 自己完結UPM package、arm64 ad-hoc signed layer、Quest ARM64 APK、checksum付きrelease。
- Phase 8のcontroller haptics、`XR_EXT_hand_tracking`左右26関節、Passthrough underlay + uniform alpha 0.82近似。
- Quest 3実機上のMediaCodec Surface release、全二重Pose / hand送信、haptic command受信、Passthrough設定。
- READMEの第三者向け導入手順、Quest機能対応表、既知の制約、troubleshooting。

## 受け入れ基準

| Phase | 結果 | 最終検証 |
|---|---|---|
| 0 | 達成 | CMake build、ctest 1/1、arm64 |
| 1 | 達成 | Meta XR Simulator Metal frame loop、layer load、120 frames以上 |
| 2 | 達成 | 未接続pass-through、H.264 120/120 decode、30 fps以上 |
| 3 | 達成 | 合成view / space / action注入、全二重stream、clock sync |
| 4 | 達成 | Quest EditMode 9/9、Unity IL2CPP / ARM64 APK build、実機receive 74 fps / decode 76 fps / Pose 73 Hz |
| 5 | 達成 | Editor package EditMode 3/3、Simulator PlayMode 1/1、接続待ち |
| 6 | 達成 | world-fixed pose / clock unit、capture診断、Phase 0〜5回帰、実機clock sync / capture-to-decode |
| 7 | 達成 | release 4点、checksum、doctor error 0、repository外tarball Unity/Simulator E2E |
| 8 | 達成 | haptic値、左右26 joint、Passthrough flag / alpha 0.82、Phase 0〜7回帰、Quest 3実機E2E |

Phase 8の最終mock結果は120/120 decode、76.6143 fps、input 163 samples、clock sync、
`haptic_apply=1`、`haptic_stop=1`、`passthrough=1`、`passthrough_alpha=0.82`。
native側は`hands=1 haptics=1`。

## 配布物

`dist/`の最終4点:

- `com.maquestlink.editor-0.1.0.tgz` — 38,566,486 bytes — SHA-256 `0d4a5284e9732a5247819cefc8df54a163d42e29f0ed9d2dc91a8f82d0a35518`
- `MaQuestLink-0.1.0.apk` — 43,696,083 bytes — SHA-256 `ea8f0ed6420dc0a4144b54e0357acbd430b3a0734e12076151434c1587a8f25a`
- `SHA256SUMS`
- `VERSION` (`0.1.0`)

`shasum -a 256 -c SHA256SUMS`は全対象で成功した。

## できていないこと

- headsetを装着した状態でのExternal Surface映像とworld-fixed再投影の目視品質。
- 手をcamera範囲へ入れた状態での実joint姿勢の目視確認（実機E2Eはhand message 60 Hz以上を確認）。
- Touch Plusの振動強度、周波数、duration停止の体感。
- Passthrough underlayとuniform alpha 0.82の装着時の視認性。
- Apple Developer ID署名／公証、GitHub公開／push、release tag、CI secrets設定。

シーンアンカー、空間メッシュ、アイ／フェイストラッキング、画素単位alphaは計画どおり対応外。

## ユーザーにしかできない残作業

1. headsetを装着し、External Surface映像とworld-fixed再投影を目視確認する。
2. 手をcamera範囲へ入れてjoint動作、Touch振動、Passthroughの見え方を目視／体感確認する。
3. 公開する場合は修正commitへ`v0.1.0` tagを付けてpushし、GitHub ActionsへUnity license secretsを設定する。必要ならユーザー所有のDeveloper IDで署名・公証する。

実機E2Eが失敗した場合は、出力された`build/phase4-device-logcat-*.log`と
`build/phase4-device-producer-*.log`を確認する。
