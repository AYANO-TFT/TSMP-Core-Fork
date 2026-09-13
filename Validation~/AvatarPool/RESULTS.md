# Avatar pool lifetime (#14)

Validated with Unity 2022.3.22f1 on Windows, 2026-09-13.

- Before: the SDK-free regression fails at `Shrink without an incoming packet`.
- After: supplied and generated pools pass eight shrink/regrow/rejoin cycles, capacity clamps, object/rig identity, active/allocated counts and duplicate-object checks.
- Full client UdonSharp compilation passes with SDK 3.10.4-beta.2. The actual avatar-sync Udon bytecode passes eight pool-only resize/reuse cycles in the Editor VM, including IDs, timestamps and slot-cache invalidation. These VM fixtures intentionally have no animation rigs; they test pool lifetime, not pose playback.
- Existing decoder snapshot and frame-window VM tests also pass.

Max Players limits active assignments, not retained allocation. Overflow avatars are deactivated before assignments are removed. Their object and rig references remain available for reuse, whether supplied by the user or instantiated from the template. Nothing is destroyed on shrink. Pool Size therefore includes inactive retained objects. An initialized receiver processes limit changes on its next update even without another packet.

## Reproduction

Use an isolated Unity project referencing this Core and its real codec packages. Copy `Editor/AvatarPoolValidation.cs` into an Editor folder. Set `TSMP_VALIDATION_RESULT` to an absolute result filename and run Unity with:

```text
-batchmode -quit -force-d3d11 -projectPath <project> -executeMethod AvatarPoolValidation.Run -logFile <log>
```

The compiled-Udon coverage is included in `../DecoderSnapshot/Editor/UdonSnapshotValidation.cs`; run the existing DecoderSnapshot runner with `-Mode Udon` against an isolated SDK project containing the modified package.

Local evidence: `F:/Unity/TSMP/Validation-Results/issues14-17-18/avatar-pool-{before,native}.{txt,log}` and `issue14-udon/Udon{.txt,-editor.log}`. No live VRChat client or world upload was performed.
