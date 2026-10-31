Decoder受信処理の回帰検証

このフォルダの位置づけ
  このフォルダは、このフォークで独自に追加した検証コードです。
  TSMPオリジナル由来のファイルではありません。
  本体のPackagesとは分離し、通常のUnityインポート・ビルドから除外される
  Validation~配下に置いています。検証するときだけAssetsへ一時コピーします。

目的
  受信先の重複状態確認を削減しても、値の適用、通知回数・順序、受信先の有効判定が
  従来どおりであることを確認します。今後の受信処理の変更でも再利用する検証です。
  DecoderVariableRuntime.ApplyVariableValueと、状態確認付きのpublicメソッド
  DecoderVariableDispatcher.Applyを直接呼び、TSMP本体の処理を通します。
  Unity-onlyではC#として、VRCではPlay中の実際のUdonとして実行します。

ファイル構成
  Runtime/DecoderDispatchProbe.cs
    テスト用入力と受信先を使い、動作検証と反復性能測定を実行します。
  Editor/DecoderDispatchValidation.cs
    リクエストファイルを読み、検証オブジェクトの準備・実行・片付けを行います。
    通常のメニューや本体の実行時計測機能は追加しません。

導入・実行・撤去
  既存の開いているUnityプロジェクトを使用します。一時プロジェクトは作成しません。
  1. Runtime/DecoderDispatchProbe.csをAssets/TSMPDispatchValidation/へコピーします。
     Editor/DecoderDispatchValidation.csをAssets/Editor/DispatchGuard/へコピーします。
     VRCプロジェクトでは、通常の手順でUdonSharp Program Assetも生成します。
  2. ExternalCompileBridgeが導入済みのプロジェクトでTemp/ExternalCompile.requestを作成し、
     Temp/ExternalCompile.result.jsonのprojectPathが対象プロジェクトと一致し、
     state=finished、success=trueであることを確認します。
  3. Temp/DispatchGuard.requestへ、次のコマンドを1つずつ書き込みます。
       prepare   Edit modeで、実行役と受信先2個を一時作成します。
       checks    動作検証を実行します。Unity-onlyはEdit/Play、VRCはPlay中の実Udonが必要です。
       benchmark checksと同じ実行条件で、標準では400回×受信先2個の適用を測定します。
       cleanup   Edit modeで、__TSMP_DispatchValidation階層だけを削除します。
     各回、リクエストが消費されるのを待ち、Temp/DispatchGuard.result.jsonを確認します。
  4. 検証オブジェクトは作業中のシーンに保存しません。Playを終了してからcleanupします。
     一時コピーしたスクリプトと生成された検証用Program Asset等を撤去し、
     再度コンパイルを要求して成功を確認します。

入力と合否条件
  入力は手作りのlittle-endian Int32値42（42, 0, 0, 0）、network ID 7、variable hash 1234です。
  受信先1～2個への正常適用、値・通知回数・通知時のハッシュ、型/ID/hash不一致、対象欠落、
  復号できない値長、Behaviour/GameObjectの無効化、受信None/Continuousへの変更を確認します。
  受信イベントが次の対象や自身を無効化するケースも確認します。
  特に、先の受信イベントによる状態変化を次の対象の有効判定に反映できることを確認します。
  public Applyの範囲外index、配列欠落、null値も確認します。

  checksの合格条件: checks=21、failures=0、errorなし。
    適用対象への通知は1回ずつで、非対象への余計な適用・通知がないことを確認します。
  benchmarkの合格条件: benchmarkApplied=iterations*2。
    時間は変更前後を比較するための記録であり、特定時間以下になることを合格条件にしません。
    比較時は同じ検証コードとEditorの条件を使用します。
    小さな整数値と簡単な受信先の測定なので、実シーン全体の処理時間を代表するものではありません。

既存の環境差について
  Udon側のBindingTable.IsUdonTargetActiveはGameObject.activeInHierarchyを確認しますが、
  UdonBehaviour.enabledは確認しません。Unity-onlyのIsComponentTargetActiveは両方を確認します。
  どちらもreceiveInterpolation=Noneの対象は除外します。
  この検証ではそれぞれの既存仕様を維持します。enabledの扱いの変更は今回の最適化の対象外です。

保管方針と検証範囲
  このフォルダには再利用する検証コードと手順だけを残します。
  測定CSV、ログ、スクリーンショット、その回限りの操作スクリプトは、UserSettings/Diagnosticsや
  Tempなどのgitignore対象に、実施日ごとのフォルダを作って保存します。
  Spoutを含む実シーン全体の動作・性能と、VRChatクライアントでの確認は別途必要です。
  C#での成功だけを、Udonでの成功と同一視しないでください。
