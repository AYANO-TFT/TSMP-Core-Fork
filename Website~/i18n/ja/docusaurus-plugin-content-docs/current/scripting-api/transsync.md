---
title: トランスシンク
---

# トランスシンク

名前空間: `K13A.TSMP`

`TransSyncAttribute` は、TSMP が変数状態メッセージにエンコードするフィールドをマークします。

```csharp
[TransSync("example.value")]
public int syncedValue;
```

フィールドは `TSMPSetup` によって検出可能である必要があります。 `[TransSync]` フィールドを追加または削除した後、`Apply Setup` を実行します。

## プロパティ

| プロパティ | 型 | 既定値 | できること |
| --- | --- | --- | --- |
| `SentEvent` | `string` | `null` | このフィールドのローカル出力成功後に呼ぶ、引数なしの public メソッド名です。受信確認の応答ではありません。 |
| `Key` | `string` | `null` | 変数ハッシュの計算に使う安定した識別子です。省略した場合はフィールド名が使われます。送信側と受信側を一致させるには、同じ key、network ID、value type を使う必要があります。 |
| `Direction` | `NetworkSyncDirection` | `SendReceive` | `Apply Setup` がこのフィールドを encoder binding table、decoder binding table、またはその両方に入れるかを決めます。入力用フィールドと表示用フィールドを分けるときに使います。 |
| `Priority` | `int` | `0` | 同じエンコーダーの自動 TransSync フィールド全体で、数値が大きいものを優先します。容量に収まらないフィールドは切り詰めず、後の送信に回します。 |
| `SendOnChange` | `bool` | `true` | 初回と、最後に出力に成功した内容から変化した値を送信します。変化のない値の再送はエンコーダーの設定に従います。`false` は最小間隔ごとに送信します。 |
| `MinSendInterval` | `float` | `0` | このフィールドの出力成功間に設ける最小秒数です。timeScale に依存しない実時間を使います。`0` は間隔制限なしで、負数や非有限値も `0` として扱います。 |
| `EnabledBy` | `string` | `null` | 同じ component にある `bool` field または property の名前です。`Apply Setup` が binding を作るとき、その member が false の場合、この field は generated binding table から除外されます。 |

`transform.packed`、`animator.bytes`、`counter.value` などの明確で安定したキーを使用します。実行時に変更されるキーは使用しないでください。

`SendOnChange` はフィールド作者が定める既定値です。ユーザーはインスペクターの [Send Mode](../components/network-behaviour.md#send-mode) で、コンポーネント単位の上書きができます。`Default` は属性設定に従い、`On Change` は `true`、`Always` は `false` として扱います。`MinSendInterval` や他のプロパティは上書きしません。

### `Key`

ハッシュはコンポーネントの完全修飾 C# 型名と key から計算します。同じコンポーネント型では異なるフィールド名に同じ key を使えますが、異なる型は key だけを揃えても同じハッシュにはなりません。

```csharp
public class ChatChannel : TSMPNetworkBehaviour
{
    [TransSync("chat.text", Direction = NetworkSyncDirection.SendOnly)]
    public string outgoingText;

    [TransSync("chat.text", Direction = NetworkSyncDirection.ReceiveOnly)]
    public string incomingText;
}
```

両側で同じコンポーネント型と一致する Network ID を使ってください。Key を維持し、変更後はバインディングを再生成してください。

### `Direction`

`Direction` は、generated binding table で field がどちら側に使われるかを決めます。

| 値 | 主な用途 |
| --- | --- |
| `SendReceive` | 同じ field を encode し、decoded value も受け取れる単純な mirrored state。自己 loopback の同一 local field でなければ最も簡単です。 |
| `SendOnly` | Source/input field。Encoder は読めますが、decoder はこの field に書き戻しません。Local UI input、local tracking state、self-loop test に使います。 |
| `ReceiveOnly` | Display/output field。Decoder は書き込めますが、encoder は読みません。Text label、proxy avatar、remote-only state、delayed loopback display に使います。 |

`instance A encoder -> stream -> instance A decoder` のような loopback test では、入力 field を `SendOnly`、別の出力 field を `ReceiveOnly` にするのが安全です。1 つの `SendReceive` field を両方に使うと、遅延した過去 frame が現在の local value を上書きすることがあります。

### `Priority`, `SendOnChange`, `MinSendInterval`

通常の Unity と Udon の両方で、自動変数送信に適用されます。受信補間、手動 Writer 呼び出し、RPC のポリシーは変更しません。以下の例は、コンポーネントの Send Mode が `Default` の場合です。

```csharp
[TransSync("status", Priority = 10, SendOnChange = true, MinSendInterval = 0.1f)]
public string status;

[TransSync("meter", SendOnChange = false, MinSendInterval = 0.05f)]
public float meter;
```

- `status` は初期値をすぐに送り、その後は変化した値を毎秒最大 10 回送信します。待機中に複数回変化した場合、中間値ではなく最新値を送ります。
- `meter` は変化がなくても毎秒最大 20 回送ります。エンコーダーのフレームレートや容量によって、実際の頻度はさらに低くなることがあります。
- 配列要素を含むシリアライズ後のバイト列を比較します。同じ `byte[]` の内容変更も検出し、内容が同じ新しい配列は変化とみなしません。浮動小数点は許容誤差ではなくエンコード精度で比較します。
- 優先順位はコンポーネントをまたいで適用します。同順位では出力成功後に順序を巡回します。高優先度のデータが常に容量を埋める場合、低優先度の送信は遅れ続けることがあります。
- 待機中の RPC を自動変数より先に書き込みます。収まらなかったフィールドは **Deferred Variables** に計上し、後の試行で最新値を再送します。フィールド単位の分割や切り詰めは行いません。
- 比較用スナップショットと最小間隔の基準時刻は、出力成功後に更新します。シリアライズ失敗、コーデック出力失敗、容量不足は送信済みとしません。
- 送信対象のフィールドも RPC もない場合、新しいフレームを書かずに現在の出力テクスチャを維持します。データなしのエラーにはなりません。

#### 再送と映像の欠落

エンコーダーの **Trans Sync Refresh Interval** (`transSyncRefreshInterval`) は既定で **1 秒**です。値が変わらなくても再送し、最後の更新を失った受信機や途中参加した受信機が復旧できる機会を設けます。フィールドの `MinSendInterval` は引き続き適用されます。最小間隔が 2 秒なら、再送設定が 1 秒でも 2 秒より早くは送りません。

再送間隔を `0` にすると、実際の `SendOnChange` が `true` のフィールドは変化した場合だけ送ります。この場合、欠落や途中参加後は値が再び変化するまで復旧しないことがあります。再送は受信確認や到達保証ではありません。`Always` や `SendOnChange = false` のフィールドの送信を制限する設定ではありません。

スクリプトを編集せず、コンポーネント全体を毎エンコードの送信対象にするには **Send Mode: Always** を選びます。Send Mode が `Default` のとき、個別のフィールドだけを対象にするには `SendOnChange = false, MinSendInterval = 0` を指定します。いずれも、実際の送信はエンコーダーの頻度、最小間隔、優先度、容量の制限を受けます。中間状態をまとめてはならないイベントには RPC を使います。

### `SentEvent`

`SentEvent` は既定値が `null` の任意の `string` プロパティです。同じコンポーネントの引数なし `public void` メソッド名を指定すると、その自動 TransSync フィールドを含むフレームの出力に成功したときだけ呼ばれます。容量不足による延期、変更なしによる省略、出力失敗では呼ばれません。既定値ではイベント呼び出しのコストはありません。

`[TransSync("delta.packed", SentEvent = nameof(CommitDelta))]` のように指定します。コールバックは直前に取得した差分の基準値を確定するために使い、処理を短くしてください。ここから `EncodeNow` を呼んだり、バインディングを再生成したりしないでください。これは **ローカルのテクスチャ出力成功** の通知であり、受信側への到達保証ではありません。手動 Writer 呼び出しには適用されません。設定変更時は、他のスケジューリング設定と同様にエンコーダーのバインディングを再生成してください。

標準の VRChat アバター同期は、このイベントでルート姿勢とプレイヤーの keepalive 記録を確定します。姿勢を取得してもフレームに含められなければ、その記録を消費しません。

### `EnabledBy`

`EnabledBy` は setup-time optional field に向いています。

```csharp
public bool includeVelocity = true;

[TransSync("velocity", EnabledBy = nameof(includeVelocity))]
public Vector3 velocity;
```

`Apply Setup` の実行時に、TSMP は `includeVelocity` を確認します。false の場合、この field は generated binding table に入りません。アップロード済みの Udon world では、これは高頻度 runtime toggle ではなく binding-generation option として扱ってください。毎 frame 送るデータを変えたい場合は、binding は残し、packed `byte[]` の中に flag を入れる方式が安全です。

## ネットワーク同期方向

| 価値 | 意味 |
| --- | --- |
| `SendReceive` | フィールドの送受信が可能です。 |
| `SendOnly` | フィールドはエンコードされていますが、受信時には適用されません。 |
| `ReceiveOnly` | フィールドは受信時に適用されますが、エンコードされません。 |

方向はセットアップ中に解決されます。変更後、セットアップを再実行してください。

## サポートされている値の型

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

高頻度データの場合は、パックされた `byte[]` フィールドを優先します。

## バインディング再生成

`Key`、`Direction`、value type、`EnabledBy`、`Priority`、`SendOnChange`、`MinSendInterval` は generated binding に影響します。これらを変更した後は、`TSMPSetup` で `Apply Setup` を実行してください。アップロード済みの VRChat world では、TSMP は runtime reflection ではなく generated table を使います。

コンポーネントの `sendMode` だけを変更する場合、バインディングの再生成は不要です。生成済みの属性設定は変更せず、実行時にモードを読み取ります。

## ペイロードに関するアドバイス

個別のフィールドにはそれぞれメッセージ オーバーヘッドがあります。いくつかのスカラー フィールドは問題ありませんが、繰り返される高頻度の値はパックする必要があります。

好む：

```csharp
[TransSync("pose.packed")]
public byte[] poseBytes;
```

多くの個々のボーン、ブレンド シェイプ、またはトランスフォーム フィールドにわたって。
