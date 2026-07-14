# Phase 7 配布パッケージング 最終レポート

最終更新: 2026-07-15

## 結論

MaQuestLinkを、Apple Silicon Mac利用者がCMake / Xcode / Homebrewなしで導入できる自己完結UPM
packageへ変更した。package導入後は、同梱APKのinstallとUnity Playだけで利用できる。

## 配布物

`scripts/build_release.sh`は`editor-package/package.json`のsemverを使い、`dist/`へ次を生成する。

- `com.maquestlink.editor-<version>.tgz`
- `MaQuestLink-<version>.apk`
- `SHA256SUMS`
- `VERSION`

UPM packageは`Native~/macOS/`のarm64 OpenXR layer / manifestと、`QuestClient~/`のQuest APKを含む。
installerはpackage外のrepository build成果物へfallbackしない。native layerは配布build時にad-hoc署名する。

## 導入経路

- Unity Package Managerのlocal tarball
- `https://github.com/<owner>/MaQuestLink.git?path=editor-package#v<version>`形式のgit URL
- source開発時のlocal `editor-package/package.json`

package load時にabsolute native pathを持つimplicit OpenXR layer manifestを自動登録する。
`scripts/doctor.sh`はpackage整合、署名、version、登録先、Simulator、adb、Quest / APKを日本語で診断する。

## 検証結果

- native Release build: 成功
- CTest: 1/1成功
- UPM / APK / VERSIONのSHA-256: 全対象成功
- repository外tarball展開: native layer / manifest / APKを確認
- native layer: Mach-O arm64、ad-hoc署名検証成功
- APK: `com.maquestlink.questclient`、version `0.1.0`
- repository外packageのdoctor: error 0
- tarball経由のrepository外Unity EditMode / Simulator PlayMode: 成功
- 配布packageのlayer load / 接続待ち / doctor: 成功（error 0、Quest未接続warningのみ）
- Quest実機E2E: Quest未接続のため未実施

## CI

- `native.yml`: GitHub-hosted macOS 15 arm64でCMake build、ctest、architecture、署名を検証する。
- `quest-apk.yml`: 手動起動。GameCI Unity Builder v4とUnity license secretsでAndroid APKをbuildする。

## 未確認とユーザー作業

- Quest 3 / 3S接続後に`scripts/e2e_device.sh`を実行し、配布APKの映像・入力・world-fixed・clock syncを確認する。
- 公開releaseでは配布binaryを同期したcommitへ`v<semver>`tagを付ける。
- Apple Developer ID公証にはユーザー所有credentialが必要。READMEの手順に従い、credentialはrepositoryへ保存しない。
