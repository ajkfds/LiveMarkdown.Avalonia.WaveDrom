# LiveMarkdown.Avalonia.WaveDrom

`LiveMarkdown.Avalonia` の拡張パッケージで、Markdown のコードフェンス内に記述した
[WaveDrom](https://wavedrom.com/) のタイミング図（デジタル波形図）を、
**JavaScript ランタイムや WebView を使わず** Avalonia のコントロールとしてネイティブに描画します。

公式 WaveDrom の描画ロジック（レイアウト・ブリックスキン・マーカー・矢印など）を C# に移植し、
`DrawingContext` による直接描画でレンダリングします。

## 特徴

- 🔌 **Markdown と統合** — ` ```wavedrom ` フェンスを検出し、波形図として描画。
- 🧩 **ネイティブ描画** — JS / WebView 不要。純粋な C# + Avalonia のみで動作。
- 🎨 **公式スキン準拠** — 公式 default スキンのブリック定義（`pclk`/`nclk`/`vvv-N`/`xm*` 等）や
  テキストクラス色（`info`/`muted`/`error`/`warning`/`success`、見出し `h1`〜`h6`）に対応。
- 🔄 **ストリーミング対応** — フェンスが開いている間はソーステキストを表示し、確定後に波形図へ切り替え。
- 🧮 **充実した要素** — signal / data / node / edge（矢印）/ group / gaps（縦線・ブラケット）/
  head / foot（tick/tock/every、tspan リッチテキスト）/ config（hscale, marks, hbounds, arcFontSize）。

## インストール

```bash
dotnet add package LiveMarkdown.Avalonia.WaveDrom
```

`LiveMarkdown.Avalonia` 本体が必要です（本パッケージが依存しています）。

## LiveMarkdown への追加方法

WaveDrom を有効にするには、次の **3 つ**を行います。

### 1. パイプラインに拡張を登録

`MarkdownRenderer.ConfigurePipeline` に `UseWaveDrom()` を追加します。

```csharp
using LiveMarkdown.Avalonia;

MarkdownRenderer.ConfigurePipeline += pipeline => pipeline
    .UseWaveDrom();
```

> [!NOTE]
> `UseMermaid()` や `UseExtendedMathematics()` など他の拡張と併用できます。

### 2. ノードファクトリを登録

`WaveDromBlockNode`（フェンスを実際のコントロールに変換するノード）を登録します。

```csharp
MarkdownNode.Edit(builder => builder
    .Register<WaveDromBlockNode>()
);
```

複数のノードをまとめて登録する場合:

```csharp
MarkdownNode.Edit(builder => builder
    .Register<MermaidBlockNode>()
    .Register<WaveDromBlockNode>()
);
```

### 3. スタイルを読み込む

アプリの `App.axaml`（または該当の `Styles`）に、同梱の `Styles.axaml` を読み込みます。

```xml
<Application.Styles>
    <StyleInclude Source="avares://LiveMarkdown.Avalonia/Styles.axaml"/>
    <StyleInclude Source="avares://LiveMarkdown.Avalonia.WaveDrom/Styles.axaml"/>
</Application.Styles>
```

### まとめて設定する例（`App.axaml.cs`）

```csharp
public override void Initialize()
{
    MarkdownRenderer.ConfigurePipeline += pipeline => pipeline
        .UseMermaid()
        .UseWaveDrom();

    MarkdownNode.Edit(builder => builder
        .Register<MermaidBlockNode>()
        .Register<WaveDromBlockNode>());

    AvaloniaXamlLoader.Load(this);
}
```

## 使い方

Markdown 内に ` ```wavedrom ` フェンスで WaveDrom の JSON（JavaScript 風オブジェクトリテラル）を書きます。
公式 WaveDrom の「緩い JSON」記法（プロパティ名のクォート省略、シングルクォート、末尾カンマ）にも対応しています。

````markdown
```wavedrom
{ signal: [
  { name: "clk",  wave: "P......" },
  { name: "bus",  wave: "x.==.=x", data: ["head", "body", "tail", "data"] },
  { name: "wire", wave: "0.1..0." }
]}
```
````

### 対応している主な記法

| 機能 | 例 |
| --- | --- |
| 信号波形 | `wave: "01.zx=ud.23.456789"`, `"p"`, `"n"`, `"P"`, `"N"`, `"h"`, `"l"` |
| バスデータ | `wave: "x.345x"`, `data: ["A", "B", "C"]` |
| クロック | `pclk` / `nclk` / `Pclk`（矢印付き）/ `Nclk` |
| グループ | `['Master', [...]]` / `{ name, signal: [...] }` |
| スペーサー | `wave: "p.....|..."`（`|` はリピート／スペーサー） |
| head / foot | `text` / `tick` / `tock` / `every` / tspan リッチテキスト |
| 矢印・エッジ | `node: '.a..j'` + `edge: ['a~b t1', 'c-~>d time 3', ...]` |
| gaps 図形 | `gaps: "....|...."`（縦線・ブラケット `[`/`]`/`(`/`)` など） |
| config | `hscale`, `marks`, `hbounds`, `arcFontSize` |

### 矢印（edge）の例

````markdown
```wavedrom
{ signal: [
  { name: 'A', wave: '01........0....', node: '.a........j' },
  { name: 'B', wave: '0.1.......0.1..', node: '..b.......i' }
  ],
  edge: [ 'a~b t1', 'a-~>b time 3' ]
}
```
````

## カスタマイズ

`WaveDromPresenter` はスタイル可能なプロパティを公開しています。`Styles.axaml` のセレクタ
`md|WaveDromPresenter` でテーマに合わせて上書きできます。

```xml
<Style Selector="md|WaveDromPresenter">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{DynamicResource ForegroundColor}"/>
    <Setter Property="MarkerBrush" Value="{DynamicResource SecondaryCardBackgroundColor}"/>
</Style>
```

| プロパティ | 説明 |
| --- | --- |
| `Text` | 描画する WaveDrom の JSON 文字列。 |
| `Foreground` | 波形・テキストの基本色。 |
| `FontFamily` / `FontSize` | 図中のテキストのフォント。 |
| `BackgroundBrush` | 図全体の背景（既定は透明）。 |
| `MarkerBrush` | データボックスの塗り（既定はテーマ色）。 |

## 制限事項

一部に未対応／近似の項目があります（詳細はリポジトリの `.agents/wavedrom-todo.md` 参照）。

- スレーブ信号（`wave: "s..."`）
- `config.skin`（narrow / dark 等の他スキン。ブリック寸法が default と異なる）
- default 以外のスキンの塗り色
- 画面描画（ピクセル）の自動検証

## ライセンス

Apache-2.0

## 参照

- リポジトリ: https://github.com/DearVa/LiveMarkdown.Avalonia
- 公式 WaveDrom: https://wavedrom.com/
