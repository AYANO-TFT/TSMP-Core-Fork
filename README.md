<p align="center">
  <img src=".github/assets/tsmp-banner.png" alt="TSMP Core — Trans Sync Media Protocol" width="100%">
</p>

<p align="center">
  <a href="https://github.com/kibalab/TSMP-Core/blob/main/LICENSE.md"><img src="https://img.shields.io/github/license/kibalab/TSMP-Core?style=flat-square&amp;label=license&amp;color=555&amp;labelColor=171717" alt="License"></a>
  <a href="https://github.com/kibalab/TSMP-Core/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/kibalab/TSMP-Core/release.yml?event=push&amp;label=release%20build&amp;style=flat-square&amp;labelColor=171717" alt="Release build"></a>
  <a href="https://github.com/kibalab/TSMP-Core/releases/latest"><img src="https://img.shields.io/github/v/release/kibalab/TSMP-Core?label=release&amp;style=flat-square&amp;color=555&amp;labelColor=171717" alt="Latest stable release"></a>
  <a href="https://github.com/kibalab/TSMP-Core/releases"><img src="https://img.shields.io/github/downloads/kibalab/TSMP-Core/total?label=asset%20downloads&amp;style=flat-square&amp;color=555&amp;labelColor=171717" alt="Release asset downloads"></a>
</p>

<p align="center">
  <a href="https://github.com/kibalab/TSMP-Core/blob/main/Packages/com.kibalab.tsmp.core/package.json"><img src="https://img.shields.io/badge/Unity-2022.3-555?style=flat-square&amp;logo=unity&amp;logoColor=white&amp;labelColor=171717" alt="Unity 2022.3"></a>
  <a href="https://github.com/kibalab/TSMP-Core/blob/main/Packages/com.kibalab.tsmp.core/package.json"><img src="https://img.shields.io/badge/VRChat%20Worlds-%3E%3D3.9.0-555?style=flat-square&amp;labelColor=171717" alt="VRChat Worlds SDK 3.9.0 or newer"></a>
  <a href="https://vpm.kiba.red/"><img src="https://img.shields.io/badge/VPM-install-555?style=flat-square&amp;labelColor=171717" alt="Install with VPM"></a>
  <a href="https://kibalab.github.io/TSMP-Core/"><img src="https://img.shields.io/badge/docs-read-555?style=flat-square&amp;labelColor=171717" alt="Documentation"></a>
</p>

[한국어](README.ko.md) | [English](README.en.md) | **日本語**

# TSMP Core

TSMP(Trans Sync Media Protocol) は、VRChat ワールド内でネットワーク状態、RPC、アバターのポーズ、Animator、Timeline などのランタイム データをテクスチャ ストリームで送るためのオープンソース パッケージです。

Core パッケージには、シーンに配置する基本ランタイムとセットアップ機能が含まれます。実際のピクセル エンコード方式は codec パッケージが担当します。標準構成では Luma4 codec を一緒にインストールしてください。

## インストール

VRChat Creator Companion で VPM リポジトリを追加します。

```text
https://vpm.kiba.red/
```

その後、`TSMP Core` と `TSMP Codec Luma4` をインストールします。

## クイック スタート

VRCSDK のない通常の Unity では、通常の Unity 対応を含む Core と Luma4 を UPM の **Add package from disk** でインストールしてください。両環境で下記の同じプレハブを使い、コンポーネントとバインディングは自動準備されます。`Assets/TSMPGenerated` をシーンと一緒に管理してください。Windows x64 Mono を検証済みです。リフレクションと stripping の制限はインストールガイドを参照してください。

1. `Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab` をシーンに配置します。
2. 同期したいオブジェクトに必要な `TSMPNetwork*` コンポーネントを追加します。
3. `TSMPSetup` で `Refresh Codecs` を押し、使用する codec を選択します。
4. Setup で入出力とコーデック設定を確認します。コンポーネントとバインディングは自動更新されます。
5. Encoder の出力 RenderTexture を配信し、同じ TSMP 映像を Decoder の入力 RenderTexture に入れます。

## 主な機能

- TSMP Encoder / Decoder
- TSMPSetup シーン設定ツール
- `[TransSync]` によるフィールド同期
- `SendTransRPC(methodName, target)` による TSMP RPC
- Transform、Rigidbody、Humanoid Pose、VRChat Avatar Pose、Animator、Timeline、BlendShape 同期コンポーネント
- codec パッケージの自動検出と選択 UI
- カスタム codec パッケージ用の共通ランタイム、shader include、catalog asset

## ドキュメント

ユーザー ガイドと開発者向けドキュメントはこちらです。

https://kibalab.github.io/TSMP-Core/

## リリース状態

TSMP Core 0.2.0 はベータではない正式リリースです。リリースタグは `v0.x.y` 形式を使用し、1.0 より前は公開 API が変更される場合があります。

## ライセンス

MIT License. Copyright (c) 2026 KIBA_Labs.
