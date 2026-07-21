# Contributing to MetalQuestLink

MetalQuestLink welcomes bug reports, compatibility results, documentation improvements, and focused
pull requests. Discussion and reports may be written in English or Japanese; code identifiers and
commit subjects should be in English.

## Before opening a pull request

1. Search existing issues and pull requests.
2. For a large behavior or protocol change, open an issue first and describe the problem, user,
   compatibility impact, and proposed acceptance criteria.
3. Keep changes focused. Do not combine unrelated renames or cleanup.
4. Never commit Unity `Library/`, `Temp/`, `Logs/`, local credentials, device identifiers, or
   signing material.

## Local setup

Required for Editor-package work:

- Apple Silicon Mac
- Unity 2022.3 LTS or newer
- Meta XR Simulator v201+
- Android platform-tools (`adb`)

Native-layer or release work additionally needs CMake 3.25+, AppleClang, and the Unity Android
toolchain. End users do not need these build tools.

## Tests

Run the smallest relevant checks, then broaden them in proportion to the change:

```sh
scripts/test_unity_matrix.sh   # Editor package/version compatibility
scripts/test_phase3.sh         # native streaming/input protocol
scripts/test_phase5.sh         # Unity/Simulator integration
scripts/test_phase7.sh         # release package structure
scripts/test_phase8.sh         # full regression (device E2E is optional without a Quest)
```

If you cannot run a hardware or licensed-Unity check, state that explicitly in the pull request.
Do not report an unexecuted test as passing.

## Documentation contract

Keep implementation and documentation in the same pull request:

- Behavior, constraints, compatibility: `docs/spec.md`
- User-facing history: `CHANGELOG.md`
- Detailed engineering history: `docs/log.md`
- Measurements, rejected options, or deferred work: `docs/notes.md`
- Setup or operation changes: English `README.md` and Japanese `README.ja.md`

See [docs/README.md](docs/README.md) for document ownership and update rules.

## Pull request checklist

- Explain the problem and the chosen solution.
- List changed behavior and compatibility impact.
- Add or update tests.
- Update relevant documentation.
- Include exact test commands and results.
- Preserve existing public behavior unless the change is intentional and documented.

By contributing, you agree that your contribution is licensed under Apache-2.0.

## Getting help

Use GitHub Discussions for setup questions and design discussion once Discussions is enabled. Use
Issues for reproducible bugs and focused feature requests. See [SUPPORT.md](SUPPORT.md) for scope
and response expectations.
