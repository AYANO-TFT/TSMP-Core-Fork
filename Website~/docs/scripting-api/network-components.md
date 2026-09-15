---
title: Built-in Network Components API
---

# Built-in network components

TSMP includes several `TSMPNetworkBehaviour` components that cover common VRChat world synchronization cases. They all share `networkId`, receive interpolation, active-state checks, and setup binding.

## Common behaviour

| Feature | Meaning |
| --- | --- |
| `networkId` | Stable ID used to match sender and receiver components. |
| `receiveInterpolation` | `None`, `Discrete`, or `Continuous` received-value handling. |
| Active state | Disabled components or inactive GameObjects do not participate. |
| `TSMPBeforeEncode()` | Captures component state before the encoder reads `[TransSync]` fields. |
| `OnTSMPVariableReceived()` | Applies decoded state after a frame is received. |

## Component overview

| Component | What it synchronizes |
| --- | --- |
| `TSMPNetworkTransformSync` | Transform position, rotation, scale, compression range, and optional Rigidbody state. |
| `TSMPNetworkHumanoidPoseSync` | Selected humanoid bones plus root motion position. |
| `TSMPNetworkBlendShapesSync` | Selected blendshape weights for one `SkinnedMeshRenderer`. |
| `TSMPNetworkVrchatAvatarPoseSync` | VRChat player tracking pose for avatar-pool playback. |
| `TSMPNetworkAnimatorSync` | Animator parameters and layer-related state supported by the runtime path. |
| `TSMPNetworkTimelineSync` | PlayableDirector timeline time/play state where supported by Udon. |
| `TSMPNetworkGameObjectToggle` | Toggle event through TSMP RPC. |
| `TSMPDebugCanvas` | Text UI for frame counters, bitrate, loss, and header metadata. |

## Timeline controls {#timeline-controls}

`TSMPNetworkTimelineSync` exposes four parameterless methods, also callable as Udon custom events:

| Method | Effect |
| --- | --- |
| `Play()` | Starts the assigned Director and records Playing. |
| `Pause()` | Pauses a playing Director, including at time zero. Does not create a graph when already stopped. |
| `Resume()` | Resumes paused playback, or starts a stopped Director. Like Play, does not restart an already playing Director. |
| `Stop()` | Stops the assigned Director and records Stopped. |

`Seek(float time)` evaluates the requested time without changing Playing or Paused state. From Stopped, it prepares a paused graph. Non-looping timelines clamp to their local duration; looping timelines wrap. Negative, NaN and infinite arguments are ignored. Call `Seek` from a script with its argument, not as a parameterless custom event. Explicit control calls cancel pending receive corrections; the next valid received packet can take control again.

The `timeline.packed` field keeps the v1 format: version byte, state byte (`0` Stopped, `1` Paused, `2` Playing), Float32 seconds and Float32 duration, both little-endian. It is exactly 10 bytes. Invalid lengths, versions, states, non-finite/negative times or durations, and times beyond the transmitted duration are rejected before playback changes. Warnings are limited to once per second and 16 per enable cycle. Missing or disabled sources clear the captured payload and `encodedTimelineBytes` rather than retransmitting an old sample. In native Unity, a missing Timeline asset also clears capture.

These calls control local playback; the Encoder transmits the resulting state on its next capture. Native Unity reads `director.state` and graph validity, including changes made by other scripts. Udon tracks the commands instead: route playback commands through this component and issue an explicit end-of-sequence command. Udon cannot reliably observe external Director commands or automatic completion. Replacing the Director resets tracked state from its `playOnAwake` setting.

## Choosing a component

Use built-in components when the data model matches your object. Write a custom `TSMPNetworkBehaviour` when:

- You need a different packed byte format.
- You need to synchronize several fields as one packet.
- You need a custom receive interpolation policy.
- You need to run logic from a TSMP RPC.

## Binding rule

After adding or removing network components, run `Apply Setup`. The encoder and decoder do not discover fields every frame in runtime worlds; setup creates explicit binding tables for performance and Udon compatibility.
