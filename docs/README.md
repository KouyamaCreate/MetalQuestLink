# MetalQuestLink documentation

[English README](../README.md) | [日本語README](../README.ja.md)

The product-facing entry point is the English root README. The files below preserve the current
specification, verification evidence, engineering decisions, and release history. Most detailed
engineering records are currently written in Japanese; English issues and pull requests are
welcome.

## Current project documents

- [plan.md](plan.md): Phase 0〜8 の実装計画と受け入れ基準
- [progress.md](progress.md): 各 Phase の実施状態と検証結果
- [spec.md](spec.md): 現在の実装仕様
- [log.md](log.md): 変更履歴
- [notes.md](notes.md): 実測結果、未決定事項、採用しなかった案
- [compatibility.md](compatibility.md): Unity / XR packageの互換性方針と検証matrix
- [migration-0.2.md](migration-0.2.md): MetalQuestLink 0.2.0全面改名の移行手順
- [public-release-checklist.md](public-release-checklist.md): GitHub Public公開時の最終設定確認

## OpenAI Build Week records

- [devpost-submission.md](devpost-submission.md): 提出項目の事実確認用worksheet
- [demo-script.md](demo-script.md): 3分未満demo動画の構成と確認項目

作業を再開するときは、最初に `progress.md` を確認する。

## 更新ルール

- 挙動・制約・互換性を変えたPRは`spec.md`とroot `CHANGELOG.md`を同時更新する。
- 実装判断と検証結果は`log.md`、実測・不採用案・保留は`notes.md`へ残す。
- 導入や操作が変わる場合は英語`README.md`と日本語`README.ja.md`を同時更新する。
- Devpost用文面は実装の正本にせず、上記docsから事実を反映する提出worksheetとして扱う。
