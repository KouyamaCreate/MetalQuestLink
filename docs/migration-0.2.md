# MetalQuestLink 0.2 migration

0.2.0はproduct identifierの全面改名を含むbreaking releaseです。0.1.xのEditor package、Quest APK、
環境変数、native layerと混在させません。

## 既存project

1. Unity Package Managerまたは`Packages/manifest.json`から以前のpackage entryを外す。
2. `com.metalquestlink.editor` 0.2.0をtarballまたはgit URLで追加する。
3. **Window > MetalQuestLink > Quick Setup (Project + Quest)**を実行する。
4. 以前のQuest client applicationが端末に残っている場合は、端末設定から削除する。
5. custom automationで指定した環境変数やbinary pathを`METALQUESTLINK_*`と
   `metalquestlink_*`へ更新する。

protocol wire magicも変更されているため、Mac layerとQuest APKは必ず同じ0.2.x releaseを使う。
