# Contributing

## Branches

- Use `main` as the development base and pull request target for fixes, features, documentation and tests.
- Reserve `release` for preparing and publishing releases. Merge tested changes from `main` when preparing a release; do not use `release` for ongoing development.
- Keep release version changes and release notes on `release`, then merge them back into `main` so future work starts from the published state.
- If an urgent fix must be made on `release`, merge it back into `main` immediately.
- Write commit messages in English.

## Validation

Validate runtime changes in both ordinary Unity and VRChat/UdonSharp projects when they affect shared code. C# compilation alone is not UdonSharp compilation. The repeatable validation harnesses and their recorded results are in `Validation~`.

Do not change protocol bytes, codec IDs or serialized asset GUIDs as part of unrelated cleanup. Preserve contributor changes when resolving merges and add focused regression tests for overlapping behavior.

## Publishing

Committing or merging code is not a request to publish a package. Create release tags or manually dispatch the release workflow only when a release is requested.

The existing website workflow automatically deploys documentation and downloadable package artifacts when matching changes are pushed to `main`. Check its triggers before pushing; a `main` push does not create a tagged VPM release.
