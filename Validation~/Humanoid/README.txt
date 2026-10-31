Humanoid受信の回帰検証（fork独自）

このフォルダはTSMPオリジナルの製品コードとは区別した、継続利用の検証コードです。
Validation~はUnityの通常のインポート対象外で、プレイヤー／ワールドのビルドには含まれません。
検証時だけ既存プロジェクトのEditorフォルダへコピーします。常設のツールバーメニューは追加しません。
コピーの追加・撤去後は、そのプロジェクトのExternalCompileBridgeでコンパイルを確認してください。

交互更新の設定
- TSMPNetworkHumanoidPoseSyncのInspectorで Alternate Hand Updates をONにします。既定値はOFFです。
- Include WristsをOFFにすると指のみ、ONにすると指とLeftHand/RightHand（手首）が対象です。
- 正常に受信した姿勢ごとに左→右を交互更新します。画面の偶数／奇数フレームには依存しません。
- 初回、受信リセット後、再有効化後、モード／補間方式の変更後の最初の姿勢は両側を更新します。
- Continuousでは休む側も以前のローカル目標へ毎フレーム補間します。Discreteではその側のローカル姿勢を保持します。
- 腕・胴体・脚・ルートは毎回更新します。送信処理、通信形式、パケット全体の検証は変更しません。
- 手指の最新値の反映は最大で受信1回分遅れます。速い動きや手の接触精度を優先する場面ではOFFを推奨します。
- 受信時の復号／目標変換を減らす機能です。補間コスト、通信量、描画負荷まで半分になる機能ではありません。

検証の実行
Editor/HumanoidReceiveValidation.cs:
  Unity-only: HumanoidReceiveValidation.Run(go => go.AddComponent<TSMPNetworkHumanoidPoseSync>())
  VRCのC#経路: HumanoidReceiveValidation.Run(go => go.AddUdonSharpComponent<TSMPNetworkHumanoidPoseSync>())
VRCのfactoryにはUdonSharpEditorが必要です。Edit modeで実行してください。
54ケースで、座標空間、Continuous/Discrete、中間Transform、ルート形式v2～v4、
受信後のAnimator移動、不正パケット、欠損ターゲット、リセット後のスナップを確認します。

Editor/HumanoidAlternatingReceiveValidation.cs:
  HumanoidAlternatingReceiveValidation.Run(
      go => HumanoidAlternatingReceiveValidation.CSharp(go.AddComponent<TSMPNetworkHumanoidPoseSync>()))
VRCのC#経路では上のAddComponentをAddUdonSharpComponentに置き換えます。
39ケースでOFF／指のみ／手首込み、Continuous/Discrete、3座標空間、中間Transform、
左右交互更新、逆順レコード、不正パケットの全体拒否、休止側の補間、実行中の設定変更、
リセット、片側だけの疎なパケット、欠損ターゲットを確認します。
入力は34ボーンの一時Transform階層と、ケースごとに異なる回転・位置を持つv4バイト列です。
更新側をOFFの参照経路と比較し、休む側は前回のローカル姿勢／目標が維持されることを確認します。
Runは成功したケース名の配列を返し、不一致があれば例外を投げます。
合否条件は角度誤差0.08度未満（従来54ケースは0.05度未満）、位置誤差0.0001未満です。

C#とUdon VMの区別
CSharpアダプターの結果だけではUdon実行の確認になりません。
同じDriverインターフェースをUdonBehaviour.GetProgramVariable/SetProgramVariableと
SendCustomEventで実装すれば、同じ交互更新ケースをコンパイル済みUdonでも実行できます。
ReceiveはposeBytes設定後にOnTSMPVariableReceived、Interpolateは_postLateUpdate（Udonのイベント名）、ResetはResetReceiveStateを呼びます。
Udon heapへenumを設定するときは、enumオブジェクトを直接渡さず基底型のintに変換してください。
既存のプログラムを割り当てた一時UdonホストをEdit modeで用意し、Play modeで実行してください。
検証用Rigは破棄されます。ホストも実行後に撤去し、元のSceneを保存しないでください。
実機の見た目、Spout全経路、VRChat実クライアント、FPSの変化は別途確認が必要です。

保守上の前提
ApplyPoseは全レコードのID・境界を検証し、ID参照配列を準備してから復号します。
親ボーンのキャッシュは検証済みパケット全体を基準にし、左右を休ませるたびには作り直しません。
目標変換では今回復号しなかった祖先を飛ばし、受信済み祖先と中間Transformから親回転を求めます。
キャッシュの値は-1または有効なHumanoid IDで、親関係は循環しないことを前提とします。
