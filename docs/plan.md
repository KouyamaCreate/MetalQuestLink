# MetalQuestLink 実装プラン（GPT-5.6 実行用プロンプト）

このファイル自体が GPT-5.6 に渡すプロンプト。

実行方法:

```sh
cd <repository-root>
codex exec --skip-git-repo-check - < docs/plan.md
```

---

# Role（役割）

あなたは自律的に動くシニアXRシステムエンジニア。`<repository-root>`（git初期化済み・ほぼ空）で、Apple Silicon Mac 上の Unity エディタPlayモードを Meta Quest 3 実機でリアルタイムにプレビュー・プレイできるシステム「MetalQuestLink」を作り切る。

作業は下記の **Phase 0〜8 を順番に** 進める。各フェーズの受け入れ基準を検証コマンドで確認してからコミットし、`docs/progress.md` にフェーズの状態（完了/未完了、検証結果、判明した事実）を追記して次へ進む。途中で実行が中断されても、`docs/progress.md` を読めば次のフェーズから再開できる状態を常に保つ。

**自動検証優先の原則**: ユーザーの手（ヘッドセット装着・体感確認）を借りる検証は最後の最後まで登場させない。それ以前に自動化できる検証（モッククライアント、ループバックE2E、実機接続時はヘッドセットを被らずに行う adb 経由の自動E2E）をすべて実装・実行し尽くすこと。「実機がないので確認できない」で済ませず、実機なしで確認できる形に検証を分解する。

# Goal（ゴール）

Mac の Unity プロジェクト（Meta XR SDK / OVRCameraRig 構成）で再生ボタンを押すと、USB接続した Quest 3 に映像が低遅延表示され、Quest の頭・コントローラの動きとボタン入力がそのままエディタ内のアプリを操作する。Windows の Quest Link Playモードプレビューの実用的な代替として機能する。

# アーキテクチャ（この方針で作る。細部の実装は裁量）

Quest Link 自体（MetaのクローズドなPCランタイム）はmacOSに存在しないため、**OpenXR APIレイヤー方式**で等価機能を作る:

1. Mac上のUnityエディタは Meta XR Simulator（MetaのmacOS対応OpenXRランタイム）に対してPlayモードを実行する。セッション管理・スワップチェーン・OVRPluginが要求するMeta拡張はシミュレータに任せる。
2. 新規実装する **OpenXR APIレイヤー**（C++/Objective-C++、arm64 dylib）をローダーとシミュレータの間に挟む:
   - `xrEndFrame` を横取りし、プロジェクションレイヤーのスワップチェーンイメージ（Metalテクスチャ）を取得 → VideoToolbox で H.264（可能ならHEVC）低遅延エンコード → TCPでQuestクライアントへ送信。各フレームにレンダー時の視点ポーズとタイムスタンプを付与する。
   - `xrLocateSpace` / `xrLocateViews` / `xrSyncActions` / `xrGetActionState*` を横取りし、Questクライアントから受信した実機のHMD・コントローラポーズとボタン/スティック/トリガー入力に差し替える。アクションハンドルとサブアクションパス（左右手）の対応は `xrSuggestInteractionProfileBindings` / `xrCreateAction` の横取りで構築する。
   - クライアント未接続時は何もせずパススルー（従来のシミュレータ動作のまま）。接続・切断はPlayモード中でも安全に行える。
3. **Questクライアントアプリ**（Unity 6 + Meta XR SDK、APK）:
   - MediaCodec（low-latencyモード）でデコードし、OVROverlay の External Surface（コンポジタレイヤー直行）に表示する。
   - 表示モードは2つ: (a) 頭部固定の大画面ステレオ（MVP・確実に動く）、(b) フレームに付与されたレンダーポーズを使いワールド空間にクアッドを置く再投影モード（コンポジタのTimeWarpで頭の微動に追従し、VR酔いを抑える）。(b)を最終デフォルトにする。
   - HMD・左右コントローラのポーズ、ボタン・スティック・トリガー・グリップ状態を60Hz以上でMacへ送信する。
4. **接続**: USB優先。Mac側がリッスンし、`adb reverse tcp:<port> tcp:<port>` でQuest側から `localhost:<port>` に接続する。Wi-Fi（Mac のIP直指定）フォールバックも用意する。
5. **Unityエディタ統合パッケージ**（`com.metalquestlink.editor`、ローカルUPMパッケージ）:
   - APIレイヤーの登録（macOSのOpenXR implicit layer探索パスへのマニフェスト設置、または環境変数方式。ローダー仕様を確認して確実な方を選ぶ）。
   - エディタウィンドウ: 接続状態、レイテンシ/fps表示、adb reverse の自動設定、クライアントAPKのインストール・起動ボタン。
   - 再生開始時の自動接続。

# フェーズ計画（この順で進める。各フェーズの受け入れ基準を満たすまで次へ進まない）

## Phase 0 — リポジトリ基盤とプロトコル

**成果物**: `layer/` のcmakeスキャフォールド、共有プロトコルライブラリ（映像フレーム: タイムスタンプ+レンダーポーズ付き / ポーズ・入力 / 制御メッセージ。バージョン付き・長さプレフィックスのバイナリ形式）、`docs/progress.md` の初期化。

**受け入れ基準**:
- `cmake -B build && cmake --build build` が arm64 で成功する。
- プロトコルのシリアライズ/デシリアライズのユニットテストが `ctest` で全て通る。

## Phase 1 — 技術検証スパイク（最大リスクの早期解消）

**成果物**: Meta XR Simulator の導入、OpenXR-SDK-Source の hello_xr 相当の Metal 対応ネイティブテストクライアント、何もせずパススルーするだけの空APIレイヤー、実測結果の記録（`docs/notes.md`）。

**受け入れ基準**:
- ネイティブテストクライアントが Meta XR Simulator に対して起動しフレームループが回る。
- 空APIレイヤーがローダーに認識され、テストクライアント実行時にロードされたことをログで確認できる。
- 「実装時に必ず実測で確認すること」のうち、macOSローダーのレイヤー探索パス／Metal拡張の仕様／シミュレータの導入形態、の実測結果が `docs/notes.md` に記録されている。

**判断ポイント**: ここでAPIレイヤー方式が根本的に成立しない事実が判明した場合のみ、「制約と判断基準」記載のフォールバックに切り替え、理由を `docs/notes.md` と README に記録して以降のフェーズを読み替える。

## Phase 2 — 映像パイプライン（Mac側送信）

**成果物**: APIレイヤーの `xrEndFrame` 横取り → Metalテクスチャ取得 → VideoToolbox 低遅延エンコード → TCP送信。受信検証用のモック視聴クライアント（macOSで動くCLI、受信フレームのデコード・統計出力）。

**受け入れ基準（ヘッドセット不要E2Eその1）**:
- テストクライアント + シミュレータ + 本レイヤーを起動し、ループバック接続したモック視聴クライアントが有効なH.264フレームを毎秒30枚以上デコードできることが自動テストで確認できる。
- クライアント未接続時はパススルーで動作し、Playループを阻害しない。

## Phase 3 — 入力パイプライン（実機ポーズ・入力の注入）

**成果物**: `xrLocateViews` / `xrLocateSpace` / `xrSyncActions` / `xrGetActionState*` の横取りと差し替え、アクションハンドル⇄サブアクションパスのマッピング構築、モッククライアントからの合成ポーズ・入力送信機能。

**受け入れ基準（ヘッドセット不要E2Eその2）**:
- モッククライアントが送った合成ポーズが `xrLocateViews` の結果に反映されることが自動テストで確認できる。
- 合成のボタン/スティック/トリガー入力が `xrGetActionState*` の結果に反映されることが自動テストで確認できる。

## Phase 4 — Questクライアントアプリ

**成果物**: `quest-client/`（Unity 6 + Meta XR SDK）。MediaCodec低遅延デコード → OVROverlay External Surface 表示（まず頭部固定の大画面ステレオモード）、HMD・コントローラのポーズ/入力の60Hz以上送信、`adb reverse` 前提の localhost 接続と Wi-Fi フォールバック、CLIビルドスクリプト。

クライアントには**ヘッドセットを被らずに検証するための診断モード**を組み込む: 起動引数（`adb shell am start` の extras）で有効化でき、受信フレーム数・デコード成功数・送信ポーズ数・接続状態を毎秒logcatに構造化出力する。トランスポート層とデコード制御ロジックはXR非依存のモジュールに分離し、ユニットテスト可能にする。

**受け入れ基準**:
- Unity 6000.3.6f1 のバッチモードCLIでAPKがビルドできる（`Unity -batchmode -buildTarget Android ...`）。
- トランスポート/プロトコル部のユニットテスト（EditModeテストまたはNUnit）が通る。
- **無装着・実機自動E2E**（Quest 3 がUSB接続されている場合）: `adb install` → 近接センサーを無効化してヘッドセットを被らずにスリープさせない（`adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable` 系。正確なコマンドは実装時にlogcatで実測確認） → 診断モードで起動 → Mac側テストクライアントと接続し、logcatの構造化出力で「受信フレーム30fps以上・送信ポーズ60Hz以上」をスクリプトで自動判定する。このE2Eは1コマンドで再実行できるスクリプト（`scripts/e2e_device.sh` 等）として残す。
- 実機が未接続の場合: 上記E2Eスクリプトまで完成させた上で、`docs/progress.md` に「実機を接続して `scripts/e2e_device.sh` を実行」とだけ書けば済む状態にしてフェーズ完了とみなす。ヘッドセットの装着はこのフェーズでは一切要求しない。

## Phase 5 — Unityエディタ統合とサンプル

**成果物**: `editor-package/`（`com.metalquestlink.editor` ローカルUPMパッケージ: レイヤー登録、エディタウィンドウ、adb reverse自動設定、APKインストール・起動ボタン、再生開始時の自動接続）、`samples/`（Meta XR SDK の最小シーン: OVRCameraRig + 掴めるキューブ）。

**受け入れ基準**:
- サンプルプロジェクトのエディタPlayモード起動時に本レイヤーがロードされ接続待ち状態になることをログで確認できる。
- 新規ユーザーの手順が「パッケージ導入 → APKインストール → 再生ボタン」以下に収まっている。

## Phase 6 — 仕上げ（再投影・計測・ドキュメント）

**成果物**: 再投影モード（レンダーポーズによるワールド固定クアッド）を実装しデフォルト化、レイテンシ計測（プロトコルのタイムスタンプでパイプライン各段を計測。実機接続時は無装着E2Eの範囲でエンドツーエンド遅延を実測）、`README.md`（日本語: ゼロから再生までのセットアップ、アーキテクチャ図、既知の制約、レイテンシ実測値、トラブルシューティング）。

**受け入れ基準**:
- Phase 0〜5 の全検証コマンドが最終状態のコードで再度通る（リグレッションなし）。
- README の手順だけで第三者がセットアップを再現できる記述になっている。

## Phase 7 — 配布パッケージング（他のMacユーザが使える形にする）

**成果物**: 開発ツールチェーン（cmake/Xcode）を持たない一般のMacユーザが「Unityパッケージを入れる → APKを入れる → 再生」だけで使えるようにする配布物一式。

- **UPMパッケージの自己完結化**: `com.metalquestlink.editor` に、ビルド済み arm64 dylib（APIレイヤー）とレイヤーマニフェストを同梱する。パッケージ導入だけでレイヤー登録・接続・APK導入ボタンまで完結し、利用者にcmake/Xcode/Homebrewを要求しない。git URL 経由（`https://... .git?path=editor-package`）とローカルtarballの両方でインストールできる構成にする。
- **配布物ビルドスクリプト**: `scripts/build_release.sh` 1発で `dist/` に「UPMパッケージtarball / クライアントAPK / SHA-256チェックサム / バージョン番号（semver、gitタグ連動）」を生成する。
- **コード署名の扱い**: dylibはad-hoc署名（`codesign -s -`）を配布ビルドに組み込む。Gatekeeper/quarantineで実行拒否される場合の解除手順をREADMEに明記する。Apple Developer証明書による公証はユーザーにしかできないため、手順のみ文書化する。
- **doctorコマンド**: `scripts/doctor.sh`（またはエディタウィンドウ内の診断）で、adb・レイヤー登録・シミュレータ・Quest接続・APKバージョン整合を検査し、問題を日本語で指摘する。
- **CI設定**: GitHub Actions（macos, arm64ランナー）でレイヤーのビルド+ctestを回すワークフローを用意する。APKビルドはUnityライセンスが必要なためワークフローは用意しつつ必要なシークレットをREADMEに文書化する（有効化はユーザー作業）。
- **最終レポート**（日本語）。

**受け入れ基準**:
- クリーン環境シミュレーション: このリポジトリの外に新規Unity 6プロジェクトを作り、`dist/` のtarball（またはgit URL）からパッケージを導入 → doctorが通り、Playモードでレイヤーがロードされ接続待ちになる — を、リポジトリ内のビルド成果物への暗黙依存なしで確認できる。
- `scripts/build_release.sh` が成功し、`dist/` に上記4点が揃い、チェックサムが検証できる。
- 配布物のみ（ソースビルドなし）を使った Phase 4 の無装着・実機自動E2Eが（実機接続時に）通る。未接続なら手順を `docs/progress.md` に列挙する。
- README に他のMacユーザ向けインストール手順（要件: Apple Silicon / macOS バージョン / Quest 3、導入手順、既知の制約）が含まれる。

## Phase 8 — Quest機能の拡張対応（Phase 7完了後に着手）

**目的**: コントローラ以外のQuest機能をできる限りMetalQuestLink上でも使えるようにする。価値と実現性の高い順に取り組み、各機能ごとに自動E2Eを付ける。

1. **ハプティクス（最優先・低コスト）**: `xrApplyHapticFeedback` / `xrStopHapticFeedback` を横取りし、protocol経由でQuestコントローラを振動させる。振幅・周波数・持続時間を伝達する。
   - 受け入れ基準: モッククライアントが受信したハプティクスイベント（振幅・持続時間）を検証する自動E2E。実機接続時は無装着E2Eに組み込む。
2. **ハンドトラッキング**: レイヤーが `XR_EXT_hand_tracking` を提供する（`xrEnumerateInstanceExtensionProperties` への追加、`xrCreateHandTrackerEXT` / `xrLocateHandJointsEXT` / `xrDestroyHandTrackerEXT` の実装）。Questクライアントは実機のハンドジョイント（左右26関節のポーズ＋有効フラグ）を取得し60Hz以上で送信、レイヤーが差し替えて返す。
   - 受け入れ基準: モッククライアントの合成ジョイントが `xrLocateHandJointsEXT` の結果に反映される自動E2E。OVRPlugin/Meta XR SDKがハンドトラッキングに追加で要求するMeta拡張は実測し、提供可否と制約を docs/notes.md に記録する。
3. **パススルー**: クライアント側でQuestのPassthrough underlayを有効化するモードを追加する。Mac側はアプリの environment blend mode / プロジェクションレイヤーのアルファ設定を検出し、アルファ付き映像伝送（方式は実装時に実測で選定: HEVC with alpha、アルファ別チャンネル伝送、等）でQuest側合成する。フル対応が困難な場合は「passthrough underlay + 映像の黒抜き近似」で開始してよいが、選定理由と限界をREADMEに記録する。
   - 受け入れ基準: アルファ（または近似方式）の伝達を検証する自動E2E。モック視聴クライアントでアルファ値/キー色の復元を確認する。
4. **引き続きスコープ外**: シーンアンカー・空間メッシュ・アイ/フェイストラッキング等（プロトコルに拡張余地のみ残す）。READMEの対応状況表に明記する。

**共通受け入れ基準**: Phase 0〜7 の全検証がリグレッションなしで通ること。READMEに「Quest機能対応状況」の表を追加すること。

# 確認済みの環境事実（信頼してよい。再調査不要）

| 項目 | 事実 |
|---|---|
| ハード/OS | Apple M4 Pro, macOS 26.4.1 |
| Xcode | 26.2（`xcodebuild` 利用可） |
| cmake | 4.3.1 `/opt/homebrew/bin/cmake` |
| adb | 1.0.41 `/opt/homebrew/bin/adb`（実機は現在未接続） |
| Unity | 6000.3.6f1 と 2022.3.44f1 が `/Applications/Unity/Hub/Editor/` に有り。ターゲットは 6000.3.6f1（Android Build Support 同梱有無は実装時に確認） |
| Meta XR Simulator | **未インストール**。Unityパッケージ（com.meta.xr.simulator、Metaのスコープドレジストリ経由）またはMeta配布の standalone として導入できる。導入方法の現行仕様は実装時に確認せよ |
| Quest Link | Windows専用（Metaの公式見解）。macOS版は存在しない。これの移植は目標ではない |
| Meta XR SDK | v66+ が macOS + Unity OpenXR Plugin 1.13+（macOSローダー対応版）で動作する、がMetaの公式仕様 |
| com.unity.webrtc | Unity 6 で非推奨。**使わない**（映像伝送は自前のTCP+VideoToolbox/MediaCodec） |
| リポジトリ | 空。git初期化済み |

# 実装時に必ず実測で確認すること（憶測で書かない。結果は docs/notes.md に記録）

- macOS用 OpenXR ローダーの APIレイヤー探索パスと implicit layer の有効化方法（Khronosローダーのドキュメント/ソースで確認）。→ Phase 1
- Metal対応OpenXRの正確な拡張名とスワップチェーンイメージ型（OpenXR仕様の Metal enable 拡張を確認。hello_xr の Metal グラフィックスプラグインが参考実装）。→ Phase 1
- OVRPlugin が macOS で要求する OpenXR 拡張一覧（ローダーのログ/シミュレータ起動ログで実測）。レイヤーが拡張関数を横取りする必要があるかを判断する。→ Phase 1〜3
- Quest 3 の Touch Plus コントローラのインタラクションプロファイルパス。→ Phase 3
- MediaCodec low-latency（`KEY_LOW_LATENCY`）が Quest 3 の H.264/HEVC デコーダで有効か（logcatで実測、無効なら通常モード+最小バッファで代替）。→ Phase 4
- ヘッドセット無装着でアプリを起動・稼働させ続けるためのadbコマンド（近接センサー無効化のbroadcast等。Horizon OSの現行バージョンで実測）。→ Phase 4
- UnityのUPMパッケージにネイティブdylibを同梱した際のロード挙動と、tarball導入時のquarantine属性の付き方（実測して配布手順に反映）。→ Phase 7
- OVRPlugin/Meta XR SDK がハンドトラッキングで要求する拡張（`XR_EXT_hand_tracking` のみで足りるか、`XR_FB_hand_tracking_*` 系が必要か）。→ Phase 8
- Questクライアント側 Passthrough underlay とオーバーレイ映像のアルファ合成の実挙動（premultiplied か否か、HEVC with alpha のMediaCodecデコード可否）。→ Phase 8

# 制約と判断基準

- 追加ハードウェア・有料ソフト・Macへの常駐デーモンを要求しない。ユーザー操作は「パッケージ導入 → APKインストール → 再生ボタン」以下に収める。
- レイテンシ目標: USB接続で motion-to-photon 80ms以下を目指す。Link同等（〜40ms）は必須ではないが、再投影モードで頭部回転の体感遅延を隠すこと。
- ハプティクス・ハンドトラッキング・パススルーは Phase 8 で対応する（コアのPhase 0〜7を優先し、Phase 7完了前に手を出さない）。ただしプロトコルv1のメッセージタイプ予約（ハプティクスイベント・ハンドジョイント・アルファモード制御）は早期に入れてよい。シーンアンカー・空間メッシュ・アイトラッキング等はスコープ外（プロトコルに拡張余地のみ）。READMEに対応状況を明記する。
- APIレイヤー方式が根本的に成立しない事実（例: macOSローダーがimplicit layerを一切サポートしない）が判明した場合のみ、フォールバックとして「シミュレータ映像のキャプチャ送信 + 入力はQuest→Unity Input System仮想デバイス注入」構成に切り替え、判断理由をREADMEに記録する。安易に切り替えない。
- 外部ツールの挙動がこのプランの記述と異なる場合は、観測された実挙動に合わせて実装し、相違点をREADMEに注記する。

# 出力

- コミット済みリポジトリ: `layer/`（APIレイヤー+ネイティブテスト）、`quest-client/`（Unityプロジェクト+ビルドスクリプト）、`editor-package/`（UPMパッケージ、ビルド済みdylib同梱）、`samples/`（検証用最小シーン）、`scripts/`（`e2e_device.sh` / `build_release.sh` / `doctor.sh`）、`dist/`（配布物: UPM tarball・APK・チェックサム）、`docs/`（`progress.md` = フェーズ進捗、`notes.md` = 実測記録）。
- `README.md`（日本語）: 自分用セットアップと**他のMacユーザ向けインストール手順**（要件・導入・Gatekeeper対処）、アーキテクチャ図、既知の制約、レイテンシ実測値、トラブルシューティング。
- 最終レポート（日本語）: できたこと／できていないこと、各フェーズの受け入れ基準の達成状況（検証コマンドの結果）、ユーザーにしかできない残作業の正確な手順。

# 停止規則

- 全フェーズの受け入れ基準が満たされるまで停止しない。途中で確認を求めず、判断が必要な点は本プランの方針に従って自分で決める。
- 例外（ユーザーにしかできない操作）: Quest 3 のUSB接続とUSBデバッグ許可ダイアログ、ヘッドセットを**装着しての体感確認**（無装着の自動E2Eはユーザー不要で実施してよい）、Metaアカウントでのログインが必要な操作、GitHubリポジトリの公開・push・GitHub Actionsシークレット設定、Apple Developer証明書での公証。これらでブロックされた場合は、それ以外をすべて完成・検証した上で、残りの手動手順を正確に列挙した日本語レポートで終了する。
- 中断・再開時は `docs/progress.md` を読み、完了済みフェーズの受け入れ基準を再実行で確認してから未完了フェーズを続行する。
