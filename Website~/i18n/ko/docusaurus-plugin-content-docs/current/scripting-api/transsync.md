---
title: TransSync
---

# TransSync

Namespace: `K13A.TSMP`

`TransSyncAttribute`는 TSMP가 variable state message로 encode할 field를 표시합니다.

```csharp
[TransSync("example.value")]
public int syncedValue;
```

Field는 `TSMPSetup`이 discover할 수 있어야 합니다. `[TransSync]` field를 추가하거나 제거한 뒤에는 `Apply Setup`을 실행하세요.

## Properties

| Property | Type | 기본값 | 역할 |
| --- | --- | --- | --- |
| `SentEvent` | `string` | `null` | 해당 필드의 로컬 출력 성공 후 호출할 인자 없는 public 메서드 이름입니다. 수신 확인 응답은 아닙니다. |
| `Key` | `string` | `null` | Variable hash를 계산하는 stable identifier입니다. 생략하면 field 이름을 사용합니다. Sender와 receiver field가 매칭되려면 같은 key, network ID, value type을 사용해야 합니다. |
| `Direction` | `NetworkSyncDirection` | `SendReceive` | `Apply Setup`이 이 field를 encoder binding table, decoder binding table, 또는 둘 다에 넣을지 결정합니다. Source field와 receive/display field를 분리할 때 사용합니다. |
| `Priority` | `int` | `0` | 같은 인코더의 모든 자동 TransSync 필드 중 숫자가 큰 필드를 먼저 전송합니다. 용량에 들어가지 않는 필드는 자르지 않고 다음 전송으로 미룹니다. |
| `SendOnChange` | `bool` | `true` | 최초 값과 마지막 출력 성공 시점의 직렬화 결과에서 변경된 값을 전송합니다. 변경 없는 값의 재전송은 인코더 설정을 따릅니다. `false`면 최소 간격이 지날 때마다 전송합니다. |
| `MinSendInterval` | `float` | `0` | 해당 필드의 출력 성공 사이에 둘 최소 시간(초)입니다. timeScale과 무관한 실제 시간을 사용합니다. `0`은 간격 제한 없음이며 음수·비유한 값도 `0`으로 처리합니다. |
| `EnabledBy` | `string` | `null` | 같은 component의 `bool` field 또는 property 이름입니다. `Apply Setup`이 binding을 만들 때 해당 member가 false면 이 field는 generated binding table에서 제외됩니다. |

`transform.packed`, `animator.bytes`, `counter.value`처럼 명확하고 stable한 key를 사용하세요. Runtime에 바뀌는 key를 사용하지 마세요.

`SendOnChange`는 필드 작성자가 정한 기본값입니다. 사용자는 인스펙터의 [Send Mode](../components/network-behaviour.md#send-mode)에서 컴포넌트 단위로 재정의할 수 있습니다. `Default`는 어트리뷰트 설정을 따르고, `On Change`는 `true`, `Always`는 `false`로 처리합니다. `MinSendInterval`이나 다른 속성은 재정의하지 않습니다.

### `Key`

해시는 컴포넌트의 전체 C# 타입 이름과 key로 계산합니다. 같은 컴포넌트 타입에서 서로 다른 필드 이름에 같은 key를 사용할 수 있지만, 서로 다른 타입은 같은 key만으로 동일한 해시가 되지 않습니다.

```csharp
public class ChatChannel : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.SendOnly)]
    public string outgoingText;

    [TransSync("chat.text", Direction = NetworkSyncDirection.ReceiveOnly)]
    public string incomingText;
}
```

양쪽에서 같은 컴포넌트 타입과 일치하는 Network ID를 사용하세요. Key를 안정적으로 유지하고 변경한 뒤에는 바인딩을 다시 생성하세요.

### `Direction`

`Direction`은 generated binding table에서 field가 어느 쪽에 사용될지 결정합니다.

| Value | 주 사용처 |
| --- | --- |
| `SendReceive` | 같은 field가 encode도 되고 decoded value도 받을 수 있는 단순 mirrored state. 같은 local field를 loopback하는 경우가 아니라면 가장 간단합니다. |
| `SendOnly` | Source/input field. Encoder는 읽을 수 있지만 decoder는 이 field에 write하지 않습니다. Local UI input, local tracking state, self-loop test에 사용하세요. |
| `ReceiveOnly` | Display/output field. Decoder는 write할 수 있지만 encoder는 읽지 않습니다. Text label, proxy avatar, remote-only state, delayed loopback display에 사용하세요. |

`instance A encoder -> stream -> instance A decoder` 같은 loopback 테스트에서는 입력 field를 `SendOnly`, 별도 출력 field를 `ReceiveOnly`로 두는 편이 안전합니다. 하나의 `SendReceive` field를 양쪽에 쓰면 지연된 과거 frame이 현재 local value를 다시 덮어쓸 수 있습니다.

### `Priority`, `SendOnChange`, `MinSendInterval`

일반 Unity와 Udon 모두 자동 변수 송신에 이 옵션들을 적용합니다. 수신 보간이나 수동 Writer 호출, RPC의 정책을 변경하는 옵션은 아닙니다. 아래 예시는 컴포넌트의 Send Mode가 `Default`일 때의 동작입니다.

```csharp
[TransSync("status", Priority = 10, SendOnChange = true, MinSendInterval = 0.1f)]
public string status;

[TransSync("meter", SendOnChange = false, MinSendInterval = 0.05f)]
public float meter;
```

- `status`는 최초 값을 바로 보내고, 이후 변경된 값을 최대 초당 10회 전송합니다. 대기 중 여러 번 변경되면 중간 값은 합쳐지고 가장 최신 값이 전송됩니다.
- `meter`는 변경되지 않아도 최대 초당 20회 전송합니다. 인코더 프레임레이트와 가용 용량에 따라 실제 빈도는 더 낮아질 수 있습니다.
- 변경 감지는 배열 원소를 포함한 직렬화 바이트를 비교합니다. 같은 `byte[]`의 내부 수정도 감지하지만 내용이 같은 새 배열은 변경으로 보지 않습니다. 실수는 오차 허용치가 아닌 인코딩 정밀도로 비교합니다.
- 우선순위는 컴포넌트 경계를 넘어 적용하고, 같은 우선순위에서는 프레임 출력 성공 후 순서를 순환합니다. 높은 우선순위의 데이터가 계속 용량을 채우면 낮은 우선순위는 계속 밀릴 수 있습니다.
- 대기 중인 RPC를 자동 변수보다 먼저 기록합니다. 용량에 못 들어간 필드는 **Deferred Variables**에 집계하고 다음 시도에서 최신 값으로 재시도합니다. 필드 하나를 분할하거나 자르지는 않습니다.
- 비교 기준값과 최소 간격의 기준 시간은 출력에 성공해야 갱신됩니다. 직렬화 실패, 코덱 출력 실패, 용량 부족으로 생략한 값은 전송 완료로 처리하지 않습니다.
- 전송할 필드와 RPC가 없으면 새 프레임을 쓰지 않고 기존 출력 텍스처를 유지합니다. 이 경우에는 데이터 없음 오류를 띄우지 않습니다.

#### 재전송과 영상 손실

인코더의 **Trans Sync Refresh Interval** (`transSyncRefreshInterval`) 기본값은 **1초**입니다. 값이 그대로여도 다시 보내므로 마지막 변경 프레임을 놓쳤거나 뒤늦게 접속한 수신기가 값을 복구할 기회를 얻습니다. 필드의 `MinSendInterval`은 계속 지킵니다. 최소 간격이 2초면 재전송 설정이 1초여도 2초보다 빨리 보내지 않습니다.

재전송 간격을 `0`으로 설정하면 실제 `SendOnChange`가 `true`인 필드는 변경된 값만 보냅니다. 이때는 유실이나 늦은 접속 후 값이 다시 변경될 때까지 수신 값이 복구되지 않을 수 있습니다. 재전송은 수신 확인이나 전달 보장을 의미하지 않습니다. `Always`나 `SendOnChange = false`인 필드의 송신을 제한하지는 않습니다.

스크립트를 수정하지 않고 컴포넌트 전체를 매 인코딩마다 전송 대상으로 삼으려면 **Send Mode: Always**를 선택하세요. Send Mode가 `Default`일 때 개별 필드만 그렇게 하려면 `SendOnChange = false, MinSendInterval = 0`을 지정합니다. 두 경우 모두 실제 송신은 인코더 빈도, 최소 간격, 우선순위와 용량 제한을 받습니다. 중간 상태가 합쳐지면 안 되는 이벤트는 RPC로 보내세요.

### `SentEvent`

`SentEvent`는 기본값이 `null`인 선택적 `string` 속성입니다. 같은 컴포넌트의 인자 없는 `public void` 메서드 이름을 지정하면, 해당 자동 TransSync 필드가 포함된 프레임의 출력에 성공했을 때만 호출됩니다. 용량 부족으로 미뤄진 필드, 변경되지 않아 생략된 필드, 출력 실패에는 호출되지 않습니다. 기본값에서는 이벤트 호출 비용이 없습니다.

`[TransSync("delta.packed", SentEvent = nameof(CommitDelta))]`처럼 지정합니다. 콜백은 방금 캡처한 차분의 기준값을 확정하는 용도로 짧게 작성하세요. 여기서 `EncodeNow`를 호출하거나 바인딩을 재생성하지 마세요. 이 이벤트는 **로컬 텍스처 출력 성공**을 의미하며 수신 측 전달을 보장하지 않습니다. 수동 Writer 호출에는 적용되지 않습니다. 속성을 변경하면 다른 스케줄링 옵션과 마찬가지로 인코더 바인딩을 재생성해야 합니다.

기본 VRChat 아바타 동기화 컴포넌트는 이 이벤트에서 루트 포즈와 플레이어 keepalive 기록을 확정합니다. 포즈를 캡처했더라도 프레임에 담지 못했다면 기록을 소비하지 않습니다.

### `EnabledBy`

`EnabledBy`는 setup-time optional field에 적합합니다.

```csharp
public bool includeVelocity = true;

[TransSync("velocity", EnabledBy = nameof(includeVelocity))]
public Vector3 velocity;
```

`Apply Setup`을 실행할 때 TSMP는 `includeVelocity`를 확인합니다. false면 이 field는 generated binding table에 들어가지 않습니다. 업로드된 Udon world에서는 이것을 고빈도 runtime toggle이 아니라 binding-generation option으로 취급하세요. 매 frame마다 보낼 데이터를 바꾸고 싶다면 binding은 유지하고 packed `byte[]` 안에 flag를 넣는 방식이 더 안전합니다.

## NetworkSyncDirection

| Value | 의미 |
| --- | --- |
| `SendReceive` | Field를 send/receive할 수 있습니다. |
| `SendOnly` | Field를 encode하지만 receive 적용은 하지 않습니다. |
| `ReceiveOnly` | Receive 적용은 하지만 encode하지 않습니다. |

Direction은 setup 중에 해석됩니다. 변경 후 setup을 다시 실행하세요.

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

고빈도 데이터에는 packed `byte[]` field를 선호하세요.

## 바인딩 재생성

`Key`, `Direction`, value type, `EnabledBy`, `Priority`, `SendOnChange`, `MinSendInterval`은 generated binding에 영향을 줍니다. 이 값을 바꾼 뒤에는 `TSMPSetup`에서 `Apply Setup`을 실행하세요. 업로드된 VRChat world에서 TSMP는 runtime reflection 대신 generated table을 사용합니다.

컴포넌트의 `sendMode`만 바꾸는 경우에는 바인딩을 다시 만들 필요가 없습니다. 생성된 어트리뷰트 설정을 수정하지 않고 런타임에 모드를 읽습니다.

## Payload advice

각 field에는 message overhead가 있습니다. 몇 개의 scalar field는 괜찮지만, 반복되는 고빈도 값은 pack하는 것이 좋습니다.

선호:

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

많은 개별 bone, blend shape, transform field보다 위 방식이 좋습니다.
