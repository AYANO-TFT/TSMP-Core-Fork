---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

名前空間: `K13A.TSMP.Udon`

TSMP 同期動作の基本タイプ。

コンポーネントが TSMP メッセージ ルーティングに参加する必要がある場合にこれを使用します。ネットワーク ID、受信補間ポリシー、ライフサイクル フック、および `SendTransRPC` を提供します。

## フィールド

| 分野 | タイプ | 使用 |
| --- | --- | --- |
| `networkId` | `ushort` | ペイロード メッセージをこの動作にルーティングします。 |
| `sendMode` | `SendMode` | 自動フィールドの変更検出をコンポーネント単位で上書きします。既定値は `Default` です。 |
| `receiveInterpolation` | `ReceiveInterpolationMode` | コントロールは動作を受け取ります。 |
| `continuousInterpolationRate` | `float` | 連続受信モードの平滑化率。 |
| `transRpcEncoder` | `TSMPEncoder` | `SendTransRPC` によって使用されるエンコーダー。セットアップによって割り当てられます。 |
| `lastVariableHash` | `uint` | 最後に受信した変数のハッシュ。 |
| `lastRpcHash` | `uint` | 最後に受信した RPC ハッシュ。 |
| `lastRpcNetworkId` | `ushort` | 最後に受信した RPC のネットワーク ID。 |
| `lastRpcArgumentCount` | `int` | 最後に受信した RPC の引数の数。 |
| `lastRpcMethodName` | `string` | 最後に受信した RPC のメソッド名。 |

通常のシーンでは `transRpcEncoder` を手動で割り当てないでください。 `TSMPSetup` に割り当ててください。

## SendMode

名前空間: `K13A.TSMP.Udon`。インスペクター上の表示: **Send Mode**。

| 値 | 数値 | 実際に適用する `SendOnChange` |
| --- | --- | --- |
| `Default` | `0` | 各フィールドの属性設定を使用します。 |
| `OnChange` | `1` | このコンポーネントの全自動フィールドに `true` を適用します。 |
| `Always` | `2` | このコンポーネントの全自動フィールドに `false` を適用します。 |

```csharp
sendMode = SendMode.Always;
```

エンコード時に値を読むため、バインディングの再生成は不要です。既存のスナップショットと出力成功時刻を維持するので、モードの切り替えで `MinSendInterval` を回避することはありません。未知の enum 値は `Default` として扱います。このフィールドがない既存のシーンやプレハブも `Default` を使用します。

属性メタデータ、優先度、送受信方向、キャプチャ処理、RPC、手動 Writer 呼び出しは変更しません。`OnChange` でもエンコーダーの定期再送が適用され、`Always` も最小間隔と容量制限に従います。使用例は [Send Mode](../components/network-behaviour.md#send-mode) を参照してください。

## 受信補間モード

| 価値 | 意味 |
| --- | --- |
| `None` | 受信した値を無視します。 |
| `Discrete` | 受け取った値を直接適用します。 |
| `Continuous` | ターゲットを保存し、サポートされている場合は補間します。 |

`Continuous` の使用方法は組み込みコンポーネントによって決まります。カスタム コンポーネントは、受信したデータを適用する前に `receiveInterpolation` をチェックする必要があります。

## ライフサイクルメソッド

| 方法 | いつ呼び出されるか |
| --- | --- |
| `TSMPBeforeEncode()` | エンコーダーが `[TransSync]` フィールドを読み取る前。 |
| `OnTSMPVariableReceived()` | デコーダが同期フィールドを適用した後。 |
| `OnTSMPVariableChanged(uint variableHash)` | 基本受信ハンドラーから。 |
| `OnTSMPRpcReceived()` | デコーダが TSMP RPC をディスパッチした後。 |
| `OnTSMPRpc(uint rpcHash)` | 基本 RPC 受信ハンドラーから。 |

典型的なカスタム コンポーネント フロー:

```csharp
public override void TSMPBeforeEncode()
{
    packedBytes = BuildPacket();
}

public override void OnTSMPVariableReceived()
{
    if (receiveInterpolation == ReceiveInterpolationMode.None)
        return;

    ApplyPacket(packedBytes);
    OnTSMPVariableChanged(lastVariableHash);
}
```

## ユーティリティメソッド

| 方法 | 使用 |
| --- | --- |
| `IsTSMPActive()` | 有効かつ階層内でアクティブな状態を返します。 |
| `GetReceiveInterpolationStep()` | クランプされた `0..1` 補間ステップを返します。 |
| `SendTransRPC(string methodName, RPCTarget target)` | TSMP を通じて RPC イベントをキューに入れます。 |

## RPCターゲット

| 価値 | 行動 |
| --- | --- |
| `Local` | ローカルのみで実行します。 |
| `Remote` | 受信者専用のキュー。 |
| `All` | ローカルで実行し、受信者を待ちます。 |

`SendTransRPC` はメソッド名をハッシュし、割り当てられたエンコーダーを通じてキューに入れます。受信側はデコード後メソッド名でディスパッチします。

## RPC の例

```csharp
public override void Interact()
{
    SendTransRPC(nameof(ToggleObject), RPCTarget.All);
}

public void ToggleObject()
{
    target.SetActive(!target.activeSelf);
}
```
