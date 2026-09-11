# Decoder Array Ownership Validation

Run date: 2026-09-11. This run covers review finding R02 (High 2), not the other review findings.

## Implementation

The decoder previously reused one array per value type across all bindings, except for byte arrays. Two same-length values could overwrite each other's fields. All eight array types now use typed caches indexed by the receiving binding. Inner arrays are allocated lazily and reused only within their binding when the length is unchanged. Multiple recipients of the same variable receive independent arrays.

The wire format, component configuration and interpolation behavior are unchanged. The decoder's private Udon field metadata was regenerated. Low-level `DecoderValueRuntime`, `DecoderVariableRuntime`, `DecoderBindingRuntime` and `DecoderValueCache` helper signatures changed with their callers; the primitive `NetworkValueReader` API is unchanged.

## Environment

- Unity 2022.3.22f1 on Windows.
- Core release worktree based on `1e016ab6a82d136743c0d3c8d27dfa9d0e73d3a3`, with this fix applied; package version remains `0.2.0-beta.1`.
- Luma4 `0.0.3-beta.3`, commit `469a57f612b5227a9e7b7e810eba0d1a1297ed5b`.
- SDK-free project: `F:\Unity\TSMP\Validation-NoSDK`.
- SDK project: `F:\Unity\TSMP\Validation-VRC`, VRChat Worlds `3.10.4-beta.2` with its bundled UdonSharp compiler.
- Both projects reference the actual Core and Luma4 worktrees through local package dependencies.
- GPU loopback: NVIDIA GeForce RTX 4090, Direct3D11, graphics-enabled Editor Play Mode.

## Results

| Step | Result | Coverage |
| --- | --- | --- |
| `ArrayCache` | PASS | Actual decoder payload parsing and native field dispatch for eight array types; separate fields, multiple recipients, same-length reuse, resizing, empty values, multi-entry frames |
| `UdonArrays` | PASS | Full installed-program UdonSharp client compilation followed by actual Editor Udon VM execution of the array helpers; eight array types, separate bindings, reuse, resizing and empty values |
| `Play` | PASS | Six-frame Luma4 GPU loopback; Transform and animated humanoid pose, int/Unicode string fields, RPC duplicate suppression and blank-input rejection |

The VM test uses client bytecode, not a Mono proxy invocation. It does not run cross-Udon component dispatch or an uploaded VRChat client session. The native array test starts with a built NetworkFrame payload rather than a texture. The separate Play test covers the existing GPU path but not every array type through a texture.

No new standalone Player, IL2CPP or SDK world build was run for this fix. No commit, push or release was performed by these validation steps.

## Reproduce and Evidence

Run `Validation~/Run-Validation.ps1` with `-Step ArrayCache` in the SDK-free project, `-Step UdonArrays` in the SDK project, then `-Step Play` in the SDK-free project. The README contains complete command examples. Keep these projects isolated from working scenes.

Raw logs and result files are in `F:\Unity\TSMP\Validation-Results\array-cache-20260911`:

- `20260911-085459-ArrayCache.log` and `20260911-085459-ArrayCache-result.txt`.
- `20260911-085530-UdonArrays.log` and `20260911-085530-UdonArrays-result.txt`.
- `20260911-085649-Play.log` and `20260911-085649-Play-result.txt`.

Udon compilation can rewrite program asset references to the validation project's generated assets. Those unrelated reference changes were discarded; only the decoder's updated private field metadata was retained.
