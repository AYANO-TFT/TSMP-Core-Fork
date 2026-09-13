# Receive Policy Regressions

These fixtures exercise the real Core decoder dispatch/cache helpers and encoder. They do not replace the receive implementation. Use dedicated projects referencing this checkout and the real Luma4 package.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
$results = 'F:/Unity/TSMP/Validation-Results/receive-policies'
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-NoSDK' -Results $results -Step ReceivePolicies
& './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-VRC' -Results $results -Step ReceivePoliciesVm
```

The SDK-free step runs C# in the Editor. The SDK step compiles all Udon programs for the client, enters Play Mode, recompiles client bytecode after SDK editor compilation, and interprets fresh program heaps with real UdonBehaviour receivers. It checks both VM return codes and error logs. This is not an uploaded VRChat test or IL2CPP validation.

## Issue #6

The existing eligibility check now rejects `ReceiveInterpolationMode.None` before decoding into cached arrays and before dispatch. No TransSync field, previous array contents, last-variable hash or receive callback changes. Outgoing TransSync and RPC behavior are unchanged. Components without the TSMP receive mode remain supported. SDK edit-mode dispatch reads the proxy; client Udon checks the heap field type before reading an optional mode. Native SDK execution uses the non-logging `TryGetProgramVariable` API, which is not exposed to Udon.

2026-09-13: Unity 2022.3.22f1; SDK-free C# and Worlds 3.10.4-beta.2 / bundled UdonSharp; real Luma4 0.0.3; RTX 4090 / D3D11.

- Before: scalar and eight array cases reproduced unwanted application. The initial outgoing fixture also lacked a native codec assignment; that fixture was corrected to instantiate the real Luma4 prefab.
- After: 11 behavior cases passed in C# and in the client Udon VM; all 27 validation-project Udon programs compiled successfully.
- Cases cover scalar/string preservation, all eight supported array families, reused-array identity/content, same-size/resized/empty incoming arrays, None/Discrete/Continuous transitions, callback/hash state, ordinary non-network components, and actual outgoing frame encoding while None.
- Initial SDK testing found error logging on an optional missing field; the final run asserts no error logs. An intermediate attempt to use `TryGetProgramVariable` in Udon failed compilation, so only native SDK execution uses that API.

Evidence root: `F:/Unity/TSMP/Validation-Results/issue6-20260913`.
- Reproduction: `before/20260913-131007-ReceivePolicies.log`.
- Final C#: `final/20260913-131506-ReceivePolicies.log`.
- Final Udon: `final-udon/20260913-131415-ReceivePoliciesVm.log`.
- Matching `-result.txt` files contain case-by-case results.

No packet format, receive interpolation algorithm, RPC retry budget, package version or release tag changes are involved.
