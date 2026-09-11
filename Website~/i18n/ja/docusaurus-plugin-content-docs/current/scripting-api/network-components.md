---
title: 組み込みネットワークコンポーネントAPI
---

# 内蔵ネットワークコンポーネント

TSMP には、一般的な VRChat 世界同期ケースをカバーするいくつかの `TSMPNetworkBehaviour` コンポーネントが含まれています。これらはすべて `networkId` を共有し、補間、アクティブ状態チェック、およびセットアップ バインディングを受信します。

## よくある行動

| 特徴 | 意味 |
| --- | --- |
| `networkId` | 送信側コンポーネントと受信側コンポーネントを照合するために使用される安定した ID。 |
| `receiveInterpolation` | `None`、`Discrete`、または `Continuous` 受信値の処理。 |
| アクティブ状態 | 無効化されたコンポーネントまたは非アクティブなゲームオブジェクトは参加しません。 |
| `TSMPBeforeEncode()` | エンコーダが `[TransSync]` フィールドを読み取る前にコンポーネントの状態をキャプチャします。 |
| `OnTSMPVariableReceived()` | フレームの受信後にデコードされた状態を適用します。 |

## コンポーネントの概要

| 成分 | 同期するもの |
| --- | --- |
| `TSMPNetworkTransformSync` | 位置、回転、スケール、圧縮範囲、およびオプションの Rigidbody 状態を変換します。 |
| `TSMPNetworkHumanoidPoseSync` | 選択された人型のボーンとルート モーションの位置。 |
| `TSMPNetworkBlendShapesSync` | 1 つの `SkinnedMeshRenderer` に対して選択されたブレンドシェイプ ウェイト。 |
| `TSMPNetworkVrchatAvatarPoseSync` | VRChat アバター プール再生用のプレーヤー トラッキング ポーズ。 |
| `TSMPNetworkAnimatorSync` | ランタイム パスでサポートされるアニメーター パラメーターとレイヤー関連の状態。 |
| `TSMPNetworkTimelineSync` | Udon でサポートされている PlayableDirector のタイムライン時間/再生状態。 |
| `TSMPNetworkGameObjectToggle` | TSMP RPC を介してイベントを切り替えます。 |
| `TSMPDebugCanvas` | フレーム カウンター、ビットレート、損失、ヘッダー メタデータのテキスト UI。 |

## Timeline 制御 {#timeline-controls}

`TSMPNetworkTimelineSync` は引数なしのメソッドを四つ公開しています。Udon custom event としても呼び出せます。

| メソッド | 動作 |
| --- | --- |
| `Play()` | Director を再生して Playing を記録します。 |
| `Pause()` | Director を一時停止して Paused を記録します。時間が 0 の場合も同様です。 |
| `Resume()` | Director の再生を再開して Playing を記録します。 |
| `Stop()` | Director を停止して Stopped を記録します。 |

これらの命令はローカル再生を制御し、Encoder が次の取得時に状態を送信します。通常の Unity では `director.state` と graph の有効性を読むため、外部スクリプトの操作も反映されます。Udon では命令を追跡するので、このコンポーネントを通して制御し、シーケンス終了時にも明示的に命令を送ってください。Director の直接操作や自動終了は確実には検出できません。Director を交換すると、新しい Director の `playOnAwake` を基準に状態を初期化します。

## コンポーネントの選択

データ モデルがオブジェクトと一致する場合は、組み込みコンポーネントを使用します。次の場合にカスタム `TSMPNetworkBehaviour` を書き込みます。

- 別のパックされたバイト形式が必要です。
- 複数のフィールドを 1 つのパケットとして同期する必要があります。
- カスタムの受信補間ポリシーが必要です。
- TSMP RPC からロジックを実行する必要があります。

## バインドルール

ネットワーク コンポーネントを追加または削除した後、`Apply Setup` を実行します。エンコーダとデコーダは、ランタイム ワールドでフレームごとにフィールドを検出しません。セットアップでは、パフォーマンスと Udon の互換性のために明示的なバインディング テーブルを作成します。
