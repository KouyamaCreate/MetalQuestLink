# OpenAI Build Week demo plan (under 3 minutes)

## Required coverage

- Show the working project, not slides only.
- Voiceover must explain MetalQuestLink, Codex, and GPT-5.6.
- Upload as a public YouTube video and keep the final duration below 3:00.

## Shot list and draft narration

| Time | Visual | Narration goal |
|---:|---|---|
| 0:00-0:20 | Unity scene, Quest, and the repeated build pain | State the real iteration problem and audience |
| 0:20-0:45 | Add the UPM tarball; open MetalQuestLink | Show no source rebuild and the Quick Setup flow |
| 0:45-1:15 | Press Play; connection/FPS diagnostics appear | Explain OpenXR interception, H.264 streaming, and prebuilt client |
| 1:15-1:50 | Head/controller movement and object interaction | Prove bidirectional HMD/Touch input |
| 1:50-2:10 | Hand visualization and passthrough | Show 26-joint hands and state the alpha approximation honestly |
| 2:10-2:35 | Compatibility matrix/tests and docs | Show 2022.3/6000.x version-tolerant package work |
| 2:35-2:55 | Codex task and relevant diff/test result | Explain where Codex and GPT-5.6 accelerated implementation |
| 2:55-3:00 | Project name and repository | Close with the developer-tool impact |

## Recording checklist

- [ ] Quest mirror and Unity window are readable at 1080p.
- [ ] No tokens, email addresses, device serials, or private paths are visible.
- [ ] The demo uses the release tarball that judges receive.
- [ ] Voiceover explicitly says both “Codex” and “GPT-5.6”.
- [ ] Final YouTube visibility is Public, not Unlisted or Private.
- [ ] Final duration is below 3:00.

## Production assets

Video source assets, device captures, licensed reference models, generated frames, audio models,
and render intermediates are intentionally not part of the public OSS repository. They have a
different redistribution boundary and contain local production paths. The public repository keeps
this factual shot plan and recording-safety checklist only.

Before submission, upload the reviewed final master to public YouTube and record that URL in
`docs/devpost-submission.md`. Do not commit raw device captures or local production configuration to
the source repository.
