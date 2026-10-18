# LiveMarkdown.Avalonia.WaveDrom

An extension package for `LiveMarkdown.Avalonia` that natively renders
[WaveDrom](https://wavedrom.com/) timing diagrams (digital waveform diagrams),
written in Markdown code fences, as Avalonia controls **without a JavaScript
runtime or WebView**.

The official WaveDrom rendering logic (layout, brick skins, markers, arrows,
etc.) has been ported to C# and is drawn directly via `DrawingContext`.

## Features

- 🔌 **Markdown integration** — Detects ` ```wavedrom ` fences and renders them as waveform diagrams.
- 🧩 **Native rendering** — No JS / WebView required. Runs on pure C# + Avalonia only.
- 🎨 **Official skin compliant** — Supports the official default skin's brick definitions
  (`pclk`/`nclk`/`vvv-N`/`xm*`, etc.) and text class colors
  (`info`/`muted`/`error`/`warning`/`success`, headings `h1`–`h6`).
- 🔄 **Streaming support** — Shows the source text while the fence is open, and switches
  to the rendered waveform diagram once it is closed.
- 🧮 **Rich feature set** — signal / data / node / edge (arrows) / group / gaps
  (vertical lines and brackets) / head / foot (tick/tock/every, tspan rich text) /
  config (hscale, marks, hbounds, arcFontSize).

## Installation

```bash
dotnet add package LiveMarkdown.Avalonia.WaveDrom
```

The `LiveMarkdown.Avalonia` core package is required (this package depends on it).

## Adding to LiveMarkdown

To enable WaveDrom, perform the following **three** steps.

### 1. Register the extension in the pipeline

Add `UseWaveDrom()` to `MarkdownRenderer.ConfigurePipeline`.

```csharp
using LiveMarkdown.Avalonia;

MarkdownRenderer.ConfigurePipeline += pipeline => pipeline
    .UseWaveDrom();
```

> [!NOTE]
> It can be used together with other extensions such as `UseMermaid()` and
> `UseExtendedMathematics()`.

### 2. Register the node factory

Register `WaveDromBlockNode` (the node that converts a fence into an actual control).

```csharp
MarkdownNode.Edit(builder => builder
    .Register<WaveDromBlockNode>()
);
```

To register multiple nodes at once:

```csharp
MarkdownNode.Edit(builder => builder
    .Register<MermaidBlockNode>()
    .Register<WaveDromBlockNode>()
);
```

### 3. Load the styles

Include the bundled `Styles.axaml` in your app's `App.axaml` (or the relevant `Styles`).

```xml
<Application.Styles>
    <StyleInclude Source="avares://LiveMarkdown.Avalonia/Styles.axaml"/>
    <StyleInclude Source="avares://LiveMarkdown.Avalonia.WaveDrom/Styles.axaml"/>
</Application.Styles>
```

### Combined configuration example (`App.axaml.cs`)

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

## Usage

Write WaveDrom JSON (JavaScript-style object literals) inside a ` ```wavedrom `
fence in your Markdown. The official WaveDrom "loose JSON" notation (unquoted
property names, single quotes, trailing commas) is also supported.

````markdown
```wavedrom
{ signal: [
  { name: "clk",  wave: "P......" },
  { name: "bus",  wave: "x.==.=x", data: ["head", "body", "tail", "data"] },
  { name: "wire", wave: "0.1..0." }
]}
```
````

### Main supported notation

| Feature | Example |
| --- | --- |
| Signal waveform | `wave: "01.zx=ud.23.456789"`, `"p"`, `"n"`, `"P"`, `"N"`, `"h"`, `"l"` |
| Bus data | `wave: "x.345x"`, `data: ["A", "B", "C"]` |
| Clock | `pclk` / `nclk` / `Pclk` (with arrows) / `Nclk` |
| Groups | `['Master', [...]]` / `{ name, signal: [...] }` |
| Spacers | `wave: "p.....|..."` (`|` is a repeat / spacer) |
| head / foot | `text` / `tick` / `tock` / `every` / tspan rich text |
| Arrows / edges | `node: '.a..j'` + `edge: ['a~b t1', 'c-~>d time 3', ...]` |
| gaps shapes | `gaps: "....|...."` (vertical lines, brackets `[`/`]`/`(`/`)`, etc.) |
| config | `hscale`, `marks`, `hbounds`, `arcFontSize` |

### Arrow (edge) example

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

## Customization

`WaveDromPresenter` exposes styleable properties. You can override them to match
your theme through the `Styles.axaml` selector `md|WaveDromPresenter`.

```xml
<Style Selector="md|WaveDromPresenter">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="{DynamicResource ForegroundColor}"/>
    <Setter Property="MarkerBrush" Value="{DynamicResource SecondaryCardBackgroundColor}"/>
</Style>
```

| Property | Description |
| --- | --- |
| `Text` | The WaveDrom JSON string to render. |
| `Foreground` | Base color for waveforms and text. |
| `FontFamily` / `FontSize` | Font for text in the diagram. |
| `BackgroundBrush` | Background of the whole diagram (transparent by default). |
| `MarkerBrush` | Fill for data boxes (theme color by default). |

## Limitations

Some items are unsupported or approximate (see `.agents/wavedrom-todo.md` in the
repository for details).

- Slave signals (`wave: "s..."`)
- `config.skin` (other skins such as narrow / dark; brick dimensions differ from default)
- Fill colors for skins other than default
- Automated verification of on-screen rendering (pixels)

## License

Apache-2.0

## References

- Repository: https://github.com/DearVa/LiveMarkdown.Avalonia
- Official WaveDrom: https://wavedrom.com/
