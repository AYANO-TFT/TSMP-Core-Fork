BlendShapeキャッシュの回帰検証（fork独自）

このフォルダはTSMPオリジナルとは区別した継続利用の検証コードです。
Validation~はUnityの通常インポート・ビルド対象外です。検証するときだけ
Editor/BlendShapeCacheValidation.csを開いている既存プロジェクトのEditorフォルダへコピーします。
常設のツールバーメニューは追加しません。コピー追加・撤去後はExternalCompileBridgeで確認します。

設定
Validate Blend Shape Cache On Receiveは既定でONです。受信時と補間時に対象一覧を全検証します。
OFFでは一覧が固定である前提でキャッシュを使います。キャラクターの有効・無効切替、
Renderer/共有Mesh/配列参照の差し替え、配列長とMeshのBlendShape数の変化は引き続き検出します。
既存blendShapeIndices配列の要素を実行中に直接書き換える場合は、直後に
_InvalidateBlendShapeCache()を呼びます。ONへ戻すと全検証が再開します。
送信側のTSMPBeforeEncodeはトグルに関係なく従来どおり全検証します。

実行方法
Unity-onlyのC#:
  BlendShapeCacheValidation.Run(go => BlendShapeCacheValidation.CSharp(go.AddComponent<TSMPNetworkBlendShapesSync>()))
VRCのC# proxy:
  上のAddComponentをUdonSharpEditorのAddUdonSharpComponentに置き換えます。
実Udon:
  DriverをGetProgramVariable/SetProgramVariable/SendCustomEventへ接続します。
  ApplyContinuousBlendShapesは_postLateUpdateへ読み替えます。
  Play中の使い捨て受信先を使用し、開始時にトグルをtrueへ戻してください。

入力は3頂点・3BlendShapeの一時Meshと、index/weightを指定したv1受信バイト列です。
例えばshape 0へweight 10を送る入力は[1,1,0,0,10]です。
正常適用、ON/OFF、配列の直接編集・差替・null、明示的無効化、Renderer/Mesh差替、
無効化と復帰、Continuous補間、リセット後スナップ、送信側の全検証を通します。
合否はRendererの実weight、キャッシュ参照、生成された送信バイト列で判定し、失敗は例外にします。
Runの戻り値は成功した17項目の名前です。
C#の成功と実Udonの成功は別に記録してください。実シーンの映像受信と性能は別途確認します。
その回限りの操作・計測コードと結果はUserSettings/Diagnosticsに残し、コミット対象にしません。
