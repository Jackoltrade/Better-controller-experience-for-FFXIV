# Better controller experience for FFXIV

[English](README.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

FFXIVのコントローラーに、好みのライトと振動を。スキルや敵の効果音、画面の揺れを振動で感じながら、ゲーム標準の機能も使えます。

**Windows x64 · Dalamud API 15**

## 主な機能

- **ライト：** RGBアニメーション、効果音連動、下側LEDのアニメーションとLB表示。プレビューで確認できます。
- **戦闘振動：** 自分・ボス・通常の敵・被ダメージ時の振動を個別に切り替え、方向と距離の設定を共有します。
- **画面エフェクト：** カメラの揺れなどの視覚効果に合わせて振動します。
- **足音の強化：** USB DualSenseの標準の足音振動を強められます。

表示言語は日本語・英語・簡体字中国語。設定は自動保存されます。

## インストール

XIVLauncherでDalamudを有効にして、ゲームを起動してください。

1. `/xlsettings`を実行し、「Experimental」タブを開きます。
2. 「Custom Plugin Repositories」に次のURLを貼り付け、**+**で追加します。追加した項目を有効にして、「Save and Close」で保存します。

   ```text
   https://raw.githubusercontent.com/Jackoltrade/Better-controller-experience-for-FFXIV/main/repo.json
   ```

3. `/xlplugins`で **Better controller experience for FFXIV** を検索し、「Install」を選びます。
4. `/bcef`でプラグインの設定を開きます。

更新は`/xlplugins`の「Update plugins」から行えます。開発用DLLを読み込んでいた場合は、そのコピーを無効にしてからリポジトリ版をインストールしてください。

すべての表示言語で同じURLを使います。インストーラーの説明文は英語（中国語版ランチャーでは中国語）ですが、プラグインの画面は日本語にも対応しています。

従来のリリース配布用リポジトリURLも引き続き使えます。メンテナー向けの手順は[Rawフィードの設定と更新](docs/RELEASING.md#raw-feed-setup-and-updates)をご覧ください。

## 振動のカーブ

自分や敵の効果音は音の強さに応じて振動し、画面エフェクトは小さな変化も感じやすいように調整しています。**画面エフェクトのカーブには、弱い効果を意図的に強める補正が表れています。** 数値はデジタル信号の目標値で、実際の振動の強さを測定したものではありません。

![初期設定の振動カーブ](docs/assets/haptics-curves.svg)

[**操作できるカーブを開く →**](https://jackoltrade.github.io/Better-controller-experience-for-FFXIV/)

カーブを選び、下のスライダーを動かすと、設定による違いを確認できます。グラフにカーソルを合わせるかタップすると、数値も表示されます。ページは英語で、ダウンロードせずにブラウザーで使えます。プラグインの設定は変更しません。

## 対応コントローラー

| 接続 | ライト | 追加の振動 |
| --- | --- | --- |
| DualSense / DualSense Edge、USB | RGB・下側LED | 精細なハプティクス、戦闘・画面効果、足音の強化 |
| DualSense / DualSense Edge、Bluetooth | RGB・下側LED | 戦闘・画面効果の通常振動 |
| Xbox互換XInputコントローラー | — | 戦闘・画面効果の通常振動 |

精細なDualSenseの振動とコントローラースピーカーには **USB** が必要です。FFXIV側でもコントローラーの振動を有効にしてください。DirectInput専用機器にはXInput変換ソフトが必要です。

## コマンドとマクロ

| コマンド | 動作 |
| --- | --- |
| `/bcef` | 設定を開く |
| `/bcef help` | コマンド一覧 |
| `/bcef info` | 使い方を開く |
| `/bcef trace` | 診断記録を開始・停止（開発用として読み込んだ場合のみ） |

チャットやマクロで `/bcef <機能名> on`・`off`・`toggle`（オン・オフ・切り替え）を使えます。

```text
/bcef haptics on
/bcef combathaptics toggle
/bcef lighting off
```

機能名：`lighting`、`haptics`、`combathaptics`、`playersfx`、`footsteps`、`spatialhaptics`、`distancefade`、`followimpact`。

**以前のマクロの `/ds5` は `/bcef` に変更してください。** コマンドでの変更も自動保存されます。

## 困ったとき・お問い合わせ

診断タブと `/bcef trace` は、開発用プラグインとして読み込んだ場合のみ利用できます。通常のインストールでは「このプラグインについて → GitHub」から問題をご報告ください。開発用では、まず診断タブで接続や出力を確認してください。反応しない効果や意図しない振動は、直前に `/bcef trace` を実行し、再現後にもう一度実行して記録を止めます。音声は録音せず、診断情報の自動送信もしません。共有前に内容を確認してください。容量・保存先の確認や、確認画面を経ての削除も診断タブから行えます。

クライアント・パッチ・コントローラーによって対応状況が異なる場合があります。[ユーザーガイド](docs/USER-GUIDE.ja.md)、[対応範囲](docs/ENEMY-HAPTICS-COVERAGE.md)、[QAレポート](docs/QA-REPORT.md)をご覧ください。

[Issue](https://github.com/Jackoltrade/Better-controller-experience-for-FFXIV/issues/new)には、プラグイン／クライアントのバージョン、機種、接続方法、再現手順を添えてください。効果音の分類に関する問題は、短い記録があると調べやすくなります。

このプラグインは完全無料で使えます。[Ko-fi](https://ko-fi.com/K7C122MEWU) · [愛発電](https://afdian.com/a/jackoltrade)

## 開発・ライセンス

[ビルドとリリース](docs/RELEASING.md) · [公式リポジトリへの登録](docs/OFFICIAL-LISTING.md) · [開発ノート](docs/DEVELOPMENT-NOTES.md) · [変更履歴](CHANGELOG.md)

**GPL-3.0-only**。Dalamudを基盤に、DS4Windows、FFXIVClientStructs、LuminaSupplementalを参考にしています。[ライセンス](LICENSE.txt)、[第三者ライセンス](THIRD-PARTY-NOTICES.txt)、[データの出典](data/README.md)をご確認ください。Sony、Square Enixとは関係のないコミュニティプロジェクトです。
