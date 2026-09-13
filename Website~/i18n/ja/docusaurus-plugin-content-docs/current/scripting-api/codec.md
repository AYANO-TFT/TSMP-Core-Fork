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

## ランタイムのデコード準備

`PrepareDecode(Texture source, Material material)` は、decoder が入力サイズ、サンプル数、バイト数、レイアウトを設定した後、各 header/payload バイトパスの直前に呼ばれます。バイトパスと同じ入力スナップショットを受け取ります。GPU の準備処理が必要な場合にオーバーライドし、最初に `base.PrepareDecode(source, material)` を呼んで前の LUT キーワードを解除してください。既存のコーデックはオーバーライド不要です。

protected メソッド `GetDecodeSampleSize(material)` は、デコードシェーダーと同じ規則で自動サンプリングを解決し、ブロックサイズ以内に制限します。`PrepareCalibrationLut(source, material, width)` は `calibrationMaterial` で再利用可能な1行の linear `ARGBFloat` テクスチャを描画します。バイトマテリアルのプロパティを準備マテリアルにコピーし、結果を `_CalibrationLut` に設定して local `TSMP_CALIBRATION_LUT` キーワードを有効にします。Player ビルドにも両方の経路を含めるため、シェーダーで `#pragma multi_compile_local _ TSMP_CALIBRATION_LUT` を宣言してください。

準備マテリアルはコーデックのプレハブに指定します。準備シェーダーは全 LUT ピクセルを書き込み、書き込み中の LUT を読み取らないでください。リソース不足、Float32 の作成失敗、空の出力、追加パスが遅くなるモードでは従来のデコード経路を維持します。Half や8-bitへの量子化はデコード結果を変える可能性があります。LUT はフレーム間で使い回さず、各パスで更新します。

基底クラスは無効化と破棄時に LUT を解放します。これらのライフサイクルメソッドをオーバーライドする場合は基底実装も呼んでください。準備フックを直接呼ぶ場合は decoder と同じマテリアルプロパティを設定し、直後にバイト blit を実行してください。
