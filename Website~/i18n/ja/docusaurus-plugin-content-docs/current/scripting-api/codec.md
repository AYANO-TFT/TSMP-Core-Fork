---
title: TSMPCodec
---

# TSMPCodec

名前空間: `K13A.TSMP`

コーデック ハンドラーの基本タイプ。

コーデック ハンドラーは、エンコーダーにペイロード バイトをピクセルに書き込む方法を指示し、デコーダーにペイロード バイトを回復するためにどのマテリアル/オプションを使用する必要があるかを指示します。

## パブリックフィールド

| 分野 | タイプ | 使用 |
| --- | --- | --- |
| `codecId` | `ushort` | フレームヘッダーに書き込まれる安定したコーデック ID。 |
| `displayName` | `string` | 編集者向けの名前。 |
| `codecOptionBytes` | `byte[]` | フレームヘッダーからコピーされたデコーダ側のオプションバイト。 |
| `selectedDecodeMaterial` | `Material` | バイトデコードに使用されるマテリアル。 |
| `payloadStartRow` | `int` | デコード用の最初のペイロード行。 |
| `payloadBlockCount` | `int` | デコードするペイロード ブロックの数。 |
| `byteCount` | `int` | 要求されたペイロードのバイト数。 |

ランタイム エンコーダーのクエリ結果フィールドは、Udon イベント ブリッジで使用するためにパブリックです。これらは `OnTSMPEncoderQuery()` と `OnTSMPEncoderWritePayload()` によって割り当てられます。

## ランタイムエンコードメソッド

| 方法 | 使用 |
| --- | --- |
| `GetEncoderSymbolMode()` | フレームヘッダーのシンボルモードを返します。 |
| `GetEncoderPayloadStartRow(width, blockSize)` | 最初のペイロード行を返します。 |
| `GetEncoderPayloadCapacityBytes(width, height, blockSize)` | ペイロードのバイト容量を返します。 |
| `GetEncoderCodecOptionByteCount()` | オプションのバイト数を返します (最大 5)。 |
| `GetEncoderCodecOptionByte(index)` | 1 つのオプション バイトを返します。 |
| `WriteEncoderPayload(...)` | ペイロード バイトをエンコーダ ピクセルに書き込みます。 |
| `ApplyDecodeOptions()` | ヘッダー/オプションからデコーダーの状態を適用します。 |

コーデックを VRChat 内で実行する必要がある場合、これらのメソッドは UdonSharp と互換性がある必要があります。

## ヘルパー メソッド

| 方法 | 使用 |
| --- | --- |
| `ReadCodecOptionByte(index, fallback)` | フォールバックを使用して 1 つのコーデック オプションを読み取ります。 |
| `ReadCodecOptionFlag(index, fallback)` | 1 つのオプションをブール値として読み取ります。 |
| `GetEncoderActiveWidthBlocks(width, blockSize)` | 書き込み可能なブロック幅を計算します。 |
| `GetEncoderActiveHeightBlocks(height, blockSize)` | 書き込み可能なブロックの高さを計算します。 |
| `WriteEncoderColorBlockAtIndex(...)` | 1 つのエンコードされたブロックを色で塗りつぶします。 |
| `ReadEncoderBits(...)` | ペイロードバイトから任意のビットを読み取ります。 |

## エディター/ネイティブ メソッド

`COMPILER_UDONSHARP` 以外でも利用可能:

| 方法 | 使用 |
| --- | --- |
| `SymbolMode` | ネイティブ シンボル モードのプロパティ。 |
| `TryWriteFrame(...)` | 完全なフレームを `Texture2D` に書き込みます。 |
| `TryWriteFrameBuffered(..., ref Color32[] pixels, out string error)` | エンコーダーが所有するラスターバッファーを再利用する任意のネイティブ writer です。既定では `TryWriteFrame` を呼ぶため、既存のコーデックもそのまま動作します。 |
| `SupportsGpuLuma4Encoding` | 既定 false の native オプトインです。標準 Luma4 のパレット・ヘッダー配置・バイト/ニブル対応を変更しないコーデックだけが true にして GPU writer を利用できます。 |
| `GetCodecOptionBytes()` | ネイティブ コーデック オプションのバイト配列。 |
| `DecodeMaterialCount` | デコードマテリアルの数。 |
| `GetDecodeMaterial(index)` | マテリアル ルックアップをデコードします。 |
| `DebugMaterialCount` | デバッグマテリアルの数。 |
| `GetDebugMaterial(index)` | デバッグマテリアルのルックアップ。 |
| `ConfigureMaterials(context)` | デコード/デバッグマテリアルを設定します。 |

## うどんイベントブリッジ

`TSMPCodec` は、エンコーダーによって使用されるパブリック イベント メソッドを公開します。

| 方法 | 目的 |
| --- | --- |
| `OnTSMPEncoderQuery()` | エンコーダのクエリ結果フィールドに値を入力します。 |
| `OnTSMPEncoderWritePayload()` | `WriteEncoderPayload` を呼び出し、結果を保存します。 |

エンコーダはこのブリッジを使用するため、コーデック クラスをハードコーディングせずにオプションのコーデック パッケージを呼び出すことができます。

Udon Encoder はエンコードの試行ごとに一度問い合わせます。同じコーデックのオプションを変更しても、容量、payload 開始行、header オプションが一緒に更新されます。Getter は軽量で副作用のない実装にしてください。結果はそのエンコード内でのみ再利用され、次のエンコードまで固定されません。

`OnTSMPEncoderQuery()` は再利用する `int[10]` の `encoderQueryValues` も更新します。順序は codec ID、symbol mode、payload 開始行、バイト容量、オプション数、五つのオプションバイトです。Bridge はこの配列を一度に取得します。次の問い合わせで変わる読み取り専用の結果として扱い、設定の保存には使用しないでください。既存の個別の結果フィールドも維持されます。カスタムコーデックは従来の Getter を実装すればよく、専用のキャッシュ無効化 API は不要です。

## ランタイムのデコード準備 {#runtime-decode-preparation}

`PrepareDecode(Texture source, Material material)` は、decoder が入力サイズ、サンプル数、バイト数、レイアウトを設定した後、各 header/payload バイトパスの直前に呼ばれます。`TSMPDecoder` は自身の入力スナップショットをこのフックと次のバイト Blit に渡すため、ヘッダーと payload は同じキャプチャ画像を読みます。渡されたテクスチャを変更・解放しないでください。`TSMPDecoder` 以外から呼ぶ場合は入力を自分でキャプチャまたは固定してください。GPU の準備処理が必要な場合にオーバーライドし、最初に `base.PrepareDecode(source, material)` を呼んで前の LUT キーワードを解除してください。既存のコーデックはオーバーライド不要です。

protected メソッド `GetDecodeSampleSize(material)` は、デコードシェーダーと同じ規則で自動サンプリングを解決し、ブロックサイズ以内に制限します。`PrepareCalibrationLut(source, material, width)` は `calibrationMaterial` で再利用可能な1行の linear `ARGBFloat` テクスチャを描画します。バイトマテリアルのプロパティを準備マテリアルにコピーし、結果を `_CalibrationLut` に設定して local `TSMP_CALIBRATION_LUT` キーワードを有効にします。Player ビルドにも両方の経路を含めるため、シェーダーで `#pragma multi_compile_local _ TSMP_CALIBRATION_LUT` を宣言してください。

準備マテリアルはコーデックのプレハブに指定します。準備シェーダーは全 LUT ピクセルを書き込み、書き込み中の LUT を読み取らないでください。リソース不足、Float32 の作成失敗、空の出力、追加パスが遅くなるモードでは従来のデコード経路を維持します。Half や8-bitへの量子化はデコード結果を変える可能性があります。LUT はフレーム間で使い回さず、各パスで更新します。

基底クラスは無効化と破棄時に LUT を解放します。これらのライフサイクルメソッドをオーバーライドする場合は基底実装も呼んでください。準備フックを直接呼ぶ場合は decoder と同じマテリアルプロパティを設定し、直後にバイト blit を実行してください。

| メンバー | 動作と条件 |
| --- | --- |
| `public Material calibrationMaterial` | コーデックのプレハブに設定する任意の準備マテリアルです。通常の Inspector では非表示です。 |
| `public virtual void PrepareDecode(Texture source, Material material)` | パスごとのフックで戻り値はありません。基底実装は LUT 使用を解除し、マテリアル変更時には以前の接続も解除します。 |
| `protected int GetDecodeSampleSize(Material material)` | null でないマテリアルが必要です。`_SampleSize > 0.5` なら切り捨てた値を使用します。自動値はブロックサイズ8以上で4、それ以外で3です。結果を `1..min(blockSize, 8)` に制限します。 |
| `protected void PrepareCalibrationLut(Texture source, Material material, int width)` | 先に基底フックを呼びます。`width` はバイト出力の画素数ではなく LUT 項目数です。`width x 1` の割り当て/再利用、プロパティコピー、準備描画の後に `_CalibrationLut` を接続します。テクスチャ生成の成功後にキーワードを有効にします。 |
| `protected virtual void OnDisable()` / `OnDestroy()` | 所有する LUT の接続を解除し、生成したテクスチャを解放・破棄します。指定されたマテリアルは破棄しません。 |

基底フックを呼んでいれば、入力不足、0以下の幅や `_ByteCount`、ARGBFloat でない実フォーマット、テクスチャ生成失敗では従来経路を維持します。Native は準備シェーダー対応と `SystemInfo.SupportsRenderTextureFormat` も確認しますが、Udon にはこの検査をコンパイルしません。実装が誤ったシェーダーまで自動復旧する保証ではありません。

マテリアル引数と `calibrationMaterial` は外部から渡す変更可能な参照であり、フックは複製しません。Setup は準備したマテリアルコピーを所有します。独自の組み込みでは共有と寿命を管理してください。割り当ての再利用と、パス間でのキャリブレーション値の再利用は別の意味です。

Luma4 フックは[実装ガイド](../developer/codec-implementation.md)、準備出力・キーワード variant・LUT 読み込みは[シェーダーガイド](../developer/codec-shaders.md)を参照してください。
