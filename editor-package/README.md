# MaQuestLink Editor

Unity Editor Play ModeをQuest 3 / 3SへstreamするMaQuestLinkの自己完結UPM packageです。
ビルド済みApple Silicon OpenXR API layerとQuest APKを含みます。

導入後は `Window > MaQuestLink` を開き、`Install Quest APK` を押してからPlayします。
package load時にimplicit OpenXR layer manifestを自動登録します。利用者側でCMakeやXcodeを
実行する必要はありません。

対応環境、Meta XR Simulatorの導入、Gatekeeper、診断方法はrepository直下の
`README.md`を参照してください。
