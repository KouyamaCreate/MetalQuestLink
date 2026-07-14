# MaQuestLink 最終レポート

最終更新: 2026-07-15

## 結論

Phase 0〜8の実装と、Quest実機を必要としない全受け入れ検証は完了した。
最終一括実行は`Phase 0-8 regression passed (device result: 2)`。device result 2は失敗ではなく、
この環境にQuest 3 / 3Sが接続されていないため実機E2Eだけを保留した結果である。

## できたこと

- Apple Silicon macOSのUnity Play ModeをMeta XR Simulatorで動かすimplicit OpenXR API layer。
- 左右眼3360x1760 SBS H.264のVideoToolbox → TCP → Quest MediaCodec External Surface経路。
- QuestのHMD / Touch入力をMac OpenXR applicationのview / space / actionへ差し戻す経路。
- world-fixed compositor再投影とMac / Quest monotonic clock同期、段階別latency診断。
- 自己完結UPM package、arm64 ad-hoc signed layer、Quest ARM64 APK、checksum付きrelease。
- Phase 8のcontroller haptics、`XR_EXT_hand_tracking`左右26関節、Passthrough underlay + uniform alpha 0.82近似。
- READMEの第三者向け導入手順、Quest機能対応表、既知の制約、troubleshooting。

## 受け入れ基準

| Phase | 結果 | 最終検証 |
|---|---|---|
| 0 | 達成 | CMake build、ctest 1/1、arm64 |
| 1 | 達成 | Meta XR Simulator Metal frame loop、layer load、120 frames以上 |
| 2 | 達成 | 未接続pass-through、H.264 120/120 decode、30 fps以上 |
| 3 | 達成 | 合成view / space / action注入、全二重stream、clock sync |
| 4 | 自動基準達成 | Quest EditMode 9/9、Unity IL2CPP / ARM64 APK build。実機表示のみ保留 |
| 5 | 達成 | Editor package EditMode 3/3、Simulator PlayMode 1/1、接続待ち |
| 6 | 自動基準達成 | world-fixed pose / clock unit、capture診断、Phase 0〜5回帰。実機latencyのみ保留 |
| 7 | 達成 | release 4点、checksum、doctor error 0、repository外tarball Unity/Simulator E2E |
| 8 | 自動基準達成 | haptic値、左右26 joint、Passthrough flag / alpha 0.82 E2E、Phase 0〜7回帰。実機効果のみ保留 |

Phase 8の最終mock結果は120/120 decode、76.6143 fps、input 163 samples、clock sync、
`haptic_apply=1`、`haptic_stop=1`、`passthrough=1`、`passthrough_alpha=0.82`。
native側は`hands=1 haptics=1`。

## 配布物

`dist/`の最終4点:

- `com.maquestlink.editor-0.1.0.tgz` — 38,563,835 bytes — SHA-256 `981ce424f1604731512f88f332db63c1a2916ca8ff6a51cbcdca11a73a095a30`
- `MaQuestLink-0.1.0.apk` — 43,693,811 bytes — SHA-256 `51798579417bc867e1cd9c0b42c6299f5d6d498ba232feceaeae3803ad02f3ff`
- `SHA256SUMS`
- `VERSION` (`0.1.0`)

`shasum -a 256 -c SHA256SUMS`は全対象で成功した。

## できていないこと

- Quest実機のMediaCodec External Surface表示と実fps / pose rate / capture-to-decode latency。
- 実際の左右26関節が60 Hz以上でMac applicationへ反映されること。
- Touch Plusの振動強度、周波数、duration停止の体感。
- Passthrough underlayとuniform alpha 0.82の実機合成、装着時の視認性。
- Apple Developer ID署名／公証、GitHub公開／push、release tag、CI secrets設定。

シーンアンカー、空間メッシュ、アイ／フェイストラッキング、画素単位alphaは計画どおり対応外。

## ユーザーにしかできない残作業

1. developer modeとUSB debuggingを有効にしたQuest 3 / 3Sを1台USB接続し、headset内の許可を承認する。
2. `adb devices`が1台の`device`を返すことを確認する。
3. repository rootで`scripts/e2e_device.sh`を実行する。APK install、adb reverse、無装着power automation、起動、診断、後片付けは自動。
4. `MAQUESTLINK_DEVICE_E2E_OK`を確認する。30 fps以上、pose 60 Hz以上、world-fixed、clock sync、hand message、haptic、passthroughを同時判定する。
5. headsetを装着し、手をcamera範囲へ入れてjoint動作、Touch振動、Passthroughの見え方を目視／体感確認する。
6. 公開する場合はPhase 8 commitへ`v0.1.0` tagを付けてpushし、GitHub ActionsへUnity license secretsを設定する。必要ならユーザー所有のDeveloper IDで署名・公証する。

実機E2Eが失敗した場合は、出力された`build/phase4-device-logcat-*.log`と
`build/phase4-device-producer-*.log`を確認する。
