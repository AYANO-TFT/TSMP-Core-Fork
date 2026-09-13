---
title: TransSync
---

# TransSync

Namespace: `K13A.TSMP`

`TransSyncAttribute` marks fields that TSMP should encode into variable state messages.

```csharp
[TransSync("example.value")]
public int syncedValue;
```

The field must be discoverable by `TSMPSetup`. After adding or removing a `[TransSync]` field, run `Apply Setup`.

## Properties

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `SentEvent` | `string` | `null` | Public parameterless method called after successful local output of this field. Not a receiver acknowledgement. |
| `Key` | `string` | `null` | Stable identifier used to calculate the variable hash. If omitted, TSMP uses the field name. Sender and receiver fields must use the same key, network ID, and value type to match. |
| `Direction` | `NetworkSyncDirection` | `SendReceive` | Controls whether the setup process puts this field in the encoder binding table, the decoder binding table, or both. Use this to separate source fields from receive/display fields. |
| `Priority` | `int` | `0` | Higher numbers are considered first across all automatic TransSync fields in this encoder. Fields that do not fit are deferred, not truncated. |
| `SendOnChange` | `bool` | `true` | Sends the first sample, then samples whose encoded contents differ from the last successful output. Unchanged-value refresh is controlled by the encoder. `false` sends whenever the minimum interval permits. |
| `MinSendInterval` | `float` | `0` | Minimum seconds between successful outputs for this field, using unscaled real time. `0` adds no interval limit; negative or non-finite values are treated as `0`. |
| `EnabledBy` | `string` | `null` | Name of a `bool` field or property on the same component. When `Apply Setup` builds bindings and the member is false, this field is omitted from the generated binding table. |

Use a clear, stable key such as `transform.packed`, `animator.bytes`, or `counter.value`. Do not use a key that changes at runtime.

### `Key`

The hash includes the component's fully qualified C# type name and the key. Different field names on the same component type can therefore share an identity, but the same key on different component types does not produce the same hash.

```csharp
public class ChatChannel : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.SendOnly)]
    public string outgoingText;

    [TransSync("chat.text", Direction = NetworkSyncDirection.ReceiveOnly)]
    public string incomingText;
}
```

Use the same component type and matching Network ID on both ends. Keep the key stable, and regenerate bindings after changing it.

### `Direction`

`Direction` decides which side of the generated binding table can use the field.

| Value | Typical use |
| --- | --- |
| `SendReceive` | Simple mirrored state where the same field can be encoded and also receive decoded values. Good for one-way sender and receiver objects that are not the same local field. |
| `SendOnly` | Source/input fields. The encoder can read the field, but the decoder will not write back into this field. Use this for local UI input, local tracking state, or self-loop tests. |
| `ReceiveOnly` | Display/output fields. The decoder can write into the field, but the encoder will not read it. Use this for text labels, proxy avatars, remote-only state, or delayed loopback display. |

For a loopback test such as `instance A encoder -> stream -> instance A decoder`, prefer `SendOnly` on the input field and `ReceiveOnly` on a separate output field. If one `SendReceive` field is used for both, delayed frames can overwrite the current local value with older received data.

### `Priority`, `SendOnChange`, and `MinSendInterval`

These options schedule automatic variable writes in both ordinary Unity and Udon. They do not change the receiver's interpolation or apply to manual Writer calls and RPCs.

```csharp
[TransSync("status", Priority = 10, SendOnChange = true, MinSendInterval = 0.1f)]
public string status;

[TransSync("meter", SendOnChange = false, MinSendInterval = 0.05f)]
public float meter;
```

- `status` sends its initial value immediately, then sends changed contents at most 10 times per second. Several changes during a waiting interval are coalesced into the latest value.
- `meter` sends at most 20 times per second even when unchanged. The encoder frame rate and available payload capacity may reduce that rate further.
- Change detection compares serialized bytes, including array elements. Editing a `byte[]` in place is detected; replacing it with an equal array is not a change. Floating-point values use encoded precision, not an epsilon.
- Higher priorities are scheduled first across components. Equal priorities rotate after successful frames. Persistent high-priority traffic can still starve lower priorities; priority does not create more capacity.
- A queued RPC is written before automatic variables. A field that cannot fit is counted in **Deferred Variables** and retried with its current value in a later frame. An individual field is never split or truncated.
- Snapshots and interval clocks advance only after successful output. Serialization failures, failed codec writes and deferred fields do not consume their send state.
- When no field is due and no RPC is queued, no new frame is written. The existing output texture remains displayed, without a no-data error.

#### Refresh and video loss

The encoder's **Trans Sync Refresh Interval** (`transSyncRefreshInterval`) defaults to **1 second**. It resends unchanged values so a lost final update or a newly connected receiver can recover. A field's `MinSendInterval` still applies; a 2-second minimum is never bypassed by a 1-second refresh.

Set refresh to `0` for strict change-only sending. In that mode, a lost update or late join can leave a value unavailable until it changes again. Refresh is best-effort retransmission, not an acknowledgement or delivery guarantee.

To retain the previous every-encode sending behavior for a field, use `SendOnChange = false, MinSendInterval = 0`. Use an RPC for events that must not be coalesced as state.

### `SentEvent`

`SentEvent` is an optional `string` property (default `null`). Set it to the name of a public, parameterless `void` method on the same component. The encoder calls it only after a frame containing this automatic TransSync field has been written successfully. Deferred fields, unchanged fields and failed outputs do not trigger it. The default adds no event calls.

Use it to commit a captured delta baseline, not to capture the next sample. Keep the callback short; do not call `EncodeNow` or rebuild bindings inside it. It confirms local texture output, **not delivery to a receiver**. Manual Writer calls do not trigger this event. Changes to `SentEvent` require regenerated encoder bindings, just like the scheduling options.

```csharp
[TransSync("delta.packed", SentEvent = nameof(CommitDelta))]
public byte[] packedBytes;

```

The built-in VRChat avatar synchronizer uses this event to commit root-pose and player keepalive records. Capturing a pose or failing to fit it into a frame no longer consumes that state.

### `EnabledBy`

`EnabledBy` is best for setup-time optional fields:

```csharp
public bool includeVelocity = true;

[TransSync("velocity", EnabledBy = nameof(includeVelocity))]
public Vector3 velocity;
```

When `Apply Setup` runs, TSMP checks `includeVelocity`. If it is false, the field is not added to the generated binding table. For uploaded Udon worlds, treat this as a binding-generation option, not as a high-frequency runtime toggle. If you need to change what data is sent every frame, keep the binding present and encode that choice inside a packed `byte[]`.

## NetworkSyncDirection

| Value | Meaning |
| --- | --- |
| `SendReceive` | Field can be sent and received. |
| `SendOnly` | Field is encoded but not applied on receive. |
| `ReceiveOnly` | Field is applied on receive but not encoded. |

Direction is resolved during setup. Re-run setup after changing it.

## Supported value types

- `bool`
- `int`
- `float`
- `Vector2`
- `Vector3`
- `Quaternion`
- `string`
- `byte[]`
- `bool[]`
- `int[]`
- `float[]`
- `Vector2[]`
- `Vector3[]`
- `Quaternion[]`
- `string[]`

For high-frequency data, prefer packed `byte[]` fields.

## Binding rebuilds

`Key`, `Direction`, value type, `EnabledBy`, `Priority`, `SendOnChange`, and `MinSendInterval` affect generated bindings. After changing any of them, run `Apply Setup` on `TSMPSetup`. In uploaded VRChat worlds, TSMP uses those generated tables instead of runtime reflection.

## Payload advice

Each separate field has message overhead. A few scalar fields are fine, but repeated high-frequency values should be packed.

Prefer:

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

over many individual bone, blend shape, or transform fields.
