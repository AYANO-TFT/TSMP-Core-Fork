# Contributing

## Branches

- Use `main` as the development base and pull request target for fixes, features, documentation and tests.
- Reserve `release` for preparing and publishing releases. Merge tested changes from `main` when preparing a release; do not use `release` for ongoing development.
- Keep release version changes and release notes on `release`, then merge them back into `main` so future work starts from the published state.
- If an urgent fix must be made on `release`, merge it back into `main` immediately.
- Write commit messages in English.

## Issues

- Search for an existing issue before opening another. Add evidence to the existing issue when the cause is the same.
- Use one issue per independently fixable cause. Use a tracking issue only to link related work, not to duplicate its acceptance criteria.
- Identify the component, failure and triggering condition in the title. Avoid vague titles and local review codes such as R01 or G01.
- Include expected behavior, actual behavior, reproduction steps, relevant versions and source or log evidence. Say explicitly when a finding comes from source review and has not been reproduced.
- Separate C# compilation, UdonSharp compilation, Unity Editor execution, Standalone Player execution and VRChat client execution. Report only checks that were actually run.
- Refer to work by GitHub issue number: `#123` within this repository and `kibalab/TSMP-Core#123` across repositories.
- Open codec-specific issues in the corresponding codec repository. Put shared protocol, encoder, decoder, setup and network-component issues in TSMP-Core. Link cross-repository prerequisites.
- English, Japanese and Korean reports are welcome. Maintainer-authored review findings are written in Japanese, with direct technical descriptions.
- Remove credentials, access tokens, private stream URLs and personal data from logs and attachments. Do not post sensitive exploit details in a public issue; use GitHub's private vulnerability reporting when available.
- Triage does not schedule implementation or authorize a release. Deferred issues remain open until their scope is explicitly resumed.

### Templates

Use the same Markdown template for browser, CLI and API-created issues:

| Template | Use |
| --- | --- |
| [Bug report](.github/ISSUE_TEMPLATE/bug.md) | Incorrect behavior, including source-review findings that are not yet reproduced |
| [Improvement proposal](.github/ISSUE_TEMPLATE/improvement.md) | Features, performance and API changes |
| [Documentation or validation](.github/ISSUE_TEMPLATE/task.md) | Documentation, compatibility checks and release prerequisites |

The browser offers these templates through **New issue**. In GitHub CLI, use `gh issue create --template bug.md` (or the other template filename). The templates use Japanese section headings; contributors may write their answers in English, Japanese or Korean.

For issues created through the API, read the matching template first, omit its YAML front matter from the submitted body, follow its section structure and explicitly supply the appropriate labels. Replace instructional text with the findings; do not submit unfilled placeholders. State when a section is not applicable or a check was not run.

### Labels

Issue templates add `needs triage`. Maintainers remove it after checking scope, duplicates and evidence.

- Type: `bug`, `enhancement`, `documentation`, `validation` or `tracking`.
- Priority: `priority: high` for data loss, blocked workflows or release blockers; `priority: medium` for incorrect behavior with limited conditions; `priority: low` for non-blocking improvements. Performance impact requires measurements, not severity inferred from code size.
- Evidence: `evidence: source-review` means the code path was inspected, not executed; `evidence: reproduced` means a reproduction is recorded in the issue. Neither label implies every runtime has been tested.
- State: `status: deferred` means deliberately postponed, not fixed or rejected.
- Area: `area: encoder`, `area: decoder`, `area: network`, `area: streaming`, `area: codecs`, `area: tooling` or `area: distribution`.

### Pull requests

Link the issue in the pull request and explain the change, compatibility impact and validation results. Use `Fixes #123` only when the change completes the issue's acceptance criteria; otherwise use `Refs #123`. Cross-repository references must include the repository name. Do not close validation tasks on the strength of packaging workflow success alone.

## Validation

Validate runtime changes in both ordinary Unity and VRChat/UdonSharp projects when they affect shared code. C# compilation alone is not UdonSharp compilation. The repeatable validation harnesses and their recorded results are in `Validation~`.

Do not change protocol bytes, codec IDs or serialized asset GUIDs as part of unrelated cleanup. Preserve contributor changes when resolving merges and add focused regression tests for overlapping behavior.

## Publishing

Committing or merging code is not a request to publish a package. Create release tags or manually dispatch the release workflow only when a release is requested.

The existing website workflow automatically deploys documentation and downloadable package artifacts when matching changes are pushed to `main`. Check its triggers before pushing; a `main` push does not create a tagged VPM release.
