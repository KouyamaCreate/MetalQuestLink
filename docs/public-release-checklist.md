# Public repository release checklist

GitHub repositoryをPublicへ変更する直前・直後に確認する項目。credentialや個人情報はこの文書へ
記録しない。

## Repository identity

- [x] Repository owner / nameを`KouyamaCreate/MetalQuestLink`にする。
- [x] Descriptionとtopics（`unity`, `openxr`, `meta-quest`, `xr`, `developer-tools`, `apple-silicon`）を設定する。
- [ ] Default branchを確認し、保護ルールまたはrulesetでPull Requestと必要なCIを要求する。
- [x] `v0.2.0` tagとGitHub Releaseを、同じcommitのUPM tarball / APK / `SHA256SUMS`付きで公開する。

## Community and security

- [x] Issuesを有効にし、Issue Formを配置する。
- [x] Discussionsを有効にし、setup質問をIssueから分離する。
- [x] **Settings > Security > Private vulnerability reporting**を有効にする。
- [ ] Security form、Code of Conduct、Contributing、Support、Licenseのリンクを未ログイン状態で確認する。
- [ ] Branch protectionの管理者例外とforce pushを必要最小限にする。

## Actions and secrets

- [ ] Forkからのworkflow権限をread-onlyにし、承認なしにsecretsを渡さない。
- [ ] Unity APK workflowを使う場合だけGitHub Actions secretsを登録する。
- [ ] `native.yml`が成功することを確認する。
- [ ] DependabotのGitHub Actions更新PRが作成できることを確認する。

## Public-content audit

- [ ] `git status --ignored`で`demo-video/`, build cache, Unity `Library/`, `.claude/`, `.codex/`,
  `.agents/`, `.DS_Store`が公開対象外であることを確認する。
- [ ] tracked / staged filesにcredential、Unity license、device serial、個人絶対pathがないことを確認する。
- [x] Release checksumを検証し、ad-hoc署名・未公証であることをRelease notesに明記する。
- [ ] READMEの英語・日本語導線と、fresh cloneからの相対リンクを確認する。
