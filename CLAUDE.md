# CLAUDE.md — 賽ノ道（すごろくローグライク）

このファイルは Claude Code が毎回最初に読むプロジェクトのルールです。

## プロジェクト概要

- サイコロを育てて出目で道を選ぶ、すごろく型のローグライク（仮タイトル「賽ノ道」）。
- 移動と戦闘で同じダイスを取り合う「使用済みサイクル」がゲームの中心。
- 仕様の正本は `Docs/spec.md`。仕様と食い違う実装をしたいときは、先に理由を説明して確認を取ること。
- 今のフェーズと作業内容は `Docs/tasks/` を見ること（現在：`Docs/tasks/phase0-prototype.md`）。

## 開発者と環境

- 開発者は日本語で話す。返答・説明・コミットメッセージは日本語。コード内の識別子は英語、コメントは日本語でよい。
- Windows PC。シェルの例を出すときは PowerShell を前提にする。
- Unity 6（6000.x）を想定。正確なバージョンは `ProjectSettings/ProjectVersion.txt` を確認すること。
- 2D プロジェクト。UI は uGUI ＋ TextMeshPro。
- Unity の操作には MCP の **isuzu-unity**（`jp.shiranui-isuzu.unity-mcp`）を使う。コンソールの確認・コンパイル確認・テスト実行・シーン操作はこれで行う。CoplayDev 版 MCP for Unity も入っているが、基本は使わない。

## フォルダ構成

```
Assets/_Project/
  Scripts/
    Core/        … 乱数、共通の型、ゲーム全体の状態遷移
    Dice/        … DiceData, DiceInstance, Face, DicePouch
    Board/       … TileNode, BoardGenerator, ReachCalculator
    Battle/      … BattleState, BattleResolver, EnemyAI, Intent
    Effects/     … Trigger, EffectSO, EffectContext, EffectBus（フェーズ1以降）
    Run/         … RunState, セーブ（フェーズ2以降）
    UI/          … MonoBehaviour の表示クラス
  Data/          … ScriptableObject のアセット（ダイス、敵など）
  Scenes/
  Tests/EditMode/
```

- アセンブリ定義：`SaiNoMichi.Runtime`（Scripts 全体）、`SaiNoMichi.Tests.EditMode`（テスト）。
- 名前空間は `SaiNoMichi` 以下にフォルダ名を続ける（例：`SaiNoMichi.Battle`）。

## 設計のルール

1. **ロジックと表示を分ける。** ダイス・盤面・戦闘のルールは MonoBehaviour に依存しない純粋な C# クラスに書き、EditMode テストで検証できるようにする。UI クラスはロジックを呼んで結果を表示するだけにする。
2. **データは ScriptableObject。** ダイスの面、敵の HP や行動パターンなど、数値はコードに直書きせず SO アセットに置く。
3. **乱数はシード付き。** `UnityEngine.Random` ではなく `System.Random` をシードから作る。マップ生成・戦闘・報酬で別々の乱数を使う（仕様書 第14章）。
4. **効果は後から足せる形に。** レリックや刻印はフェーズ1から `Trigger` ＋ `EffectSO` のイベントフック方式で作る（仕様書 第14章）。フェーズ0では不要。
5. **小さく作って動かす。** 1回の作業は1つの機能まで。動いたら次へ進む。

## Claude Code への作業ルール

- `.meta` ファイルを手で編集・削除しない。ファイルを移動するときは .meta も一緒に動かす。
- `ProjectSettings/` と `Packages/manifest.json` は、頼まれたとき以外は変更しない。パッケージを追加したいときは先に相談する。
- シーン（.unity）やプレハブ（.prefab）の YAML を直接書き換えない。シーンの組み立ては MCP for Unity で行うか、エディタ上の手順を箇条書きで開発者に伝える。可能なら、シーンを組み立てる Editor スクリプト（メニューから1回実行するもの）を用意する。
- コードを変えたら、コンパイルエラーがないことを確認する（MCP があればコンソールを読む）。ロジックを変えたら EditMode テストを実行する。
- 作業の区切りごとに、何を変えたか・どう確かめればよいかを短く報告し、コミットを提案する。コミットは開発者の了承を得てから行う。
- 仕様書に書かれていない数値やルールが必要になったら、仮の値を決めて `// TODO(仕様):` コメントを残し、報告の中で挙げる。

## 用語の対応表（識別子はこれに揃える）

| 日本語 | 英語の識別子 |
| --- | --- |
| ダイス／賽 | Dice |
| 面 | Face |
| ポーチ | Pouch |
| 使用可能／使用済み／封印 | Available / Used / Sealed |
| リフレッシュ | Refresh |
| マス | Tile |
| 盤面 | Board |
| 分岐／合流 | Branch / Merge |
| 層 | Layer |
| ラウンド（戦闘）／ターン（移動） | Round / Turn |
| 予告（インテント） | Intent |
| 攻撃値／防御値 | Attack / Block |
| 筋力・弱体・脆弱・毒・堅守・縛り | Strength / Weak / Vulnerable / Poison / Fortify / Bind |
| 刻印 | Engraving |
| 鍛冶 | Forge |
| レリック | Relic |
| お守り | Charm |
| 休憩 | Rest |
| 旅人 | Traveler |
| 段位 | Ascension |

## テスト

- テストは `Assets/_Project/Tests/EditMode/` に置き、Unity Test Framework（NUnit）を使う。
- 仕様書に具体例がある計算（例：第6章のダメージ計算の例）は、そのままテストケースにする。
