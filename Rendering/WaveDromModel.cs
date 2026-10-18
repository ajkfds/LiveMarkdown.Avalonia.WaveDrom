using System.Text.Json;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// A styled run of text. Plain head/foot <c>text</c> strings produce a single
/// span with default styling; tspan arrays (e.g. <c>['tspan', {...}, 'x']</c>)
/// produce multiple spans carrying per-tspan fill/class/size options.
/// </summary>
public sealed class WaveDromTextSpan
{
    public required string Text { get; init; }

    /// <summary>Explicit fill color (e.g. "pink"), if any.</summary>
    public string? Fill { get; init; }

    /// <summary>Skin/heading class list (e.g. "error h1"), if any.</summary>
    public string? ClassName { get; init; }

    /// <summary>Font size override (e.g. 25), if any.</summary>
    public double? FontSize { get; init; }

    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Overline { get; init; }
    public bool Underline { get; init; }

    /// <summary>True for a superscript (baseline-shift: super).</summary>
    public bool Superscript { get; init; }

    /// <summary>True for a subscript (baseline-shift: sub).</summary>
    public bool Subscript { get; init; }

    /// <summary>Vertical shift in px from tspan dy (positive = up in SVG).</summary>
    public double Dy { get; init; }
}

/// <summary>
/// Parsed WaveDrom timing diagram model, ported from the official
/// WaveDrom JS pipeline (parse-wave-lanes / parse-wave-lane / gen-brick).
/// </summary>
public sealed class WaveDromDiagram
{
    public required WaveDromLane[] Lanes { get; init; }
    public required WaveDromGroup[] Groups { get; init; }
    public required WaveDromTextSpan[] HeadTextSpans { get; init; }
    public required WaveDromTextSpan[] FootTextSpans { get; init; }

    /// <summary>Plain-text projection of <see cref="HeadTextSpans"/>.</summary>
    public string? HeadText => HeadTextSpans.Length > 0 ? string.Concat(HeadTextSpans.Select(s => s.Text)) : null;

    /// <summary>Plain-text projection of <see cref="FootTextSpans"/>.</summary>
    public string? FootText => FootTextSpans.Length > 0 ? string.Concat(FootTextSpans.Select(s => s.Text)) : null;
    public required string?[] HeadTick { get; init; }
    public required string?[] HeadTock { get; init; }
    public required string?[] FootTick { get; init; }
    public required string?[] FootTock { get; init; }
    public required bool ShowMarks { get; init; }
    public required int HScale { get; init; }
    public required int XMax { get; init; }

    /// <summary>
    /// Raw <c>edge</c> entries (e.g. <c>"a~b t1"</c>) from the top-level
    /// <c>edge</c> array. Rendered as arcs/arrows between signal nodes.
    /// </summary>
    public required string[] Edges { get; init; }

    /// <summary>Arc/edge label font size (config.arcFontSize), default 11.</summary>
    public required double ArcFontSize { get; init; }

    /// <summary>
    /// Raw <c>gaps</c> string (e.g. <c>"....|..|"</c>) drawn by render-gaps.js.
    /// </summary>
    public required string? Gaps { get; init; }

    /// <summary>Raw per-lane wave strings, needed by render-gaps.js ('s' marker).</summary>
    public required string[] LaneWaves { get; init; }
}

/// <summary>
/// One signal lane. <see cref="Bricks"/> holds half-brick identifiers
/// (e.g. "1m0", "pclk", "vvv-2") matching the official WaveDrom skin ids.
/// </summary>
public sealed class WaveDromLane
{
    public string Name { get; init; } = string.Empty;
    public int Period { get; init; } = 1;
    public double Phase { get; init; }

    /// <summary>Raw wave string (e.g. "p...."), used by gaps 's' markers.</summary>
    public string Wave { get; init; } = string.Empty;

    /// <summary>
    /// Effective phase in half-bricks including any config.hbounds xmin offset
    /// (official lane.phase = phase*2 + xmin_cfg). May be fractional.
    /// </summary>
    public double PhaseBricks { get; init; }

    /// <summary>
    /// X translation units for the lane draw group (official render-wave-lane.js
    /// tmp0[1] = phase + xmin_cfg/2).
    /// </summary>
    public double PhaseUnits { get; init; }
    public required string[] Bricks { get; init; }
    public required string?[] Labels { get; init; }

    /// <summary>
    /// Raw <c>node</c> string (one char per half-brick position, e.g. ".a..j").
    /// Non-dot characters name event anchor points referenced by <c>edge</c>.
    /// </summary>
    public string? Node { get; init; }
    public int GroupLevel { get; init; }
    public int GroupStartRow { get; init; }
    public int GroupHeight { get; init; }

    /// <summary>
    /// Accumulated rec.js group x offset for this lane (0 for top-level
    /// signals, 25 for the first nesting level, etc.). Used to size the
    /// left gutter so group brackets clear the signal names.
    /// </summary>
    public int XOffset { get; init; }
}

public sealed class WaveDromGroup
{
    public required string? Name { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Height { get; init; }
}

public static class WaveDromParser
{
    // Lane geometry constants from the official WaveDrom default skin.
    internal const double Xs = 20;   // half-cycle width
    internal const double Ys = 20;   // lane waveform height
    internal const double Yo = 30;   // lane pitch
    internal const double Y0 = 5;    // first lane offset

    public static WaveDromDiagram Parse(string source)
    {
        // WaveDrom's relaxed grammar (JavaScript object literals) also allows
        // trailing commas, which strict JSON rejects.
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };
        using var document = JsonDocument.Parse(NormalizeSource(source), options);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("WaveDrom source must be a JSON object.");
        }

        var config = root.TryGetProperty("config", out var configEl) && configEl.ValueKind == JsonValueKind.Object
            ? configEl
            : default;

        var hScale = 1;
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("hscale", out var hscale) && hscale.ValueKind == JsonValueKind.Number)
        {
            // Official parse-config.js: round, clamp to [1, 100].
            hScale = Math.Clamp((int)Math.Round(hscale.GetDouble()), 1, 100);
        }

        var showMarks = true;
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("marks", out var marks) &&
            (marks.ValueKind == JsonValueKind.False ||
             (marks.ValueKind == JsonValueKind.String && marks.GetString() == "false")))
        {
            showMarks = false;
        }

        // config.hbounds -> xmin/2, xmax/2 limits (in ticks). Applied per lane as
        // a phase offset (xmin) and a maximum brick count (xmax_cfg - xmin_cfg).
        var xMinCfg = 0;
        var xMaxCfg = int.MaxValue;
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("hbounds", out var hbounds) &&
            hbounds.ValueKind == JsonValueKind.Array &&
            hbounds.GetArrayLength() == 2)
        {
            var lo = hbounds[0].ValueKind == JsonValueKind.Number ? (int)Math.Floor(hbounds[0].GetDouble()) : 0;
            var hi = hbounds[1].ValueKind == JsonValueKind.Number ? (int)Math.Ceiling(hbounds[1].GetDouble()) : 0;
            if (lo < hi)
            {
                xMinCfg = 2 * lo;
                xMaxCfg = 2 * hi;
            }
        }

        var arcFontSize = 11d;
        if (config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("arcFontSize", out var arcSize) && arcSize.ValueKind == JsonValueKind.Number)
        {
            arcFontSize = arcSize.GetDouble();
        }

        var lanes = new List<WaveDromLane>();
        var groups = new List<WaveDromGroup>();

        if (root.TryGetProperty("signal", out var signal) && signal.ValueKind == JsonValueKind.Array)
        {
            ParseSignal(signal, lanes, groups, hScale, xMinCfg, xMaxCfg);
        }

        var xMax = Math.Max(lanes.Count > 0 ? lanes.Max(l => l.Bricks.Length) : 1, 1);

        // Official render-marks.js derives tick/tock counts from the wave
        // length (xmax / (2*hscale)), not the lane count.
        WaveDromTextSpan[] headTextSpans = [], footTextSpans = [];
        string?[] headTick = [], headTock = [], footTick = [], footTock = [];
        if (root.TryGetProperty("head", out var head) && head.ValueKind == JsonValueKind.Object)
        {
            headTextSpans = ParseText(head);
            (headTick, headTock) = GetTicks(head, xMax, hScale);
        }

        if (root.TryGetProperty("foot", out var foot) && foot.ValueKind == JsonValueKind.Object)
        {
            footTextSpans = ParseText(foot);
            (footTick, footTock) = GetTicks(foot, xMax, hScale);
        }

        var edges = root.TryGetProperty("edge", out var edgeElement) &&
                    edgeElement.ValueKind == JsonValueKind.Array
            ? edgeElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .ToArray()
            : [];

        return new WaveDromDiagram
        {
            Lanes = [.. lanes],
            Groups = [.. groups],
            HeadTextSpans = headTextSpans,
            FootTextSpans = footTextSpans,
            HeadTick = headTick,
            HeadTock = headTock,
            FootTick = footTick,
            FootTock = footTock,
            ShowMarks = showMarks,
            HScale = hScale,
            XMax = xMax,
            Edges = edges,
            ArcFontSize = arcFontSize,
            Gaps = root.TryGetProperty("gaps", out var gapsEl) && gapsEl.ValueKind == JsonValueKind.String
                ? gapsEl.GetString()
                : null,
            LaneWaves = [.. lanes.Select(l => l.Wave)]
        };
    }

    /// <summary>
    /// Normalizes WaveDrom's relaxed JSON dialect (JavaScript-style object
    /// literals) into strict JSON that <see cref="JsonDocument"/> can parse.
    /// The official WaveDrom grammar allows unquoted property names
    /// (e.g. <c>{ signal: [...] }</c>) and single-quoted strings, which
    /// strict JSON rejects.
    /// </summary>
    internal static string NormalizeSource(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length + 16);
        var inString = false;
        var quoteChar = '"';
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (inString)
            {
                if (c == '\\' && i + 1 < source.Length)
                {
                    builder.Append(c);
                    builder.Append(source[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == quoteChar)
                {
                    inString = false;
                    builder.Append('"');
                    i++;
                    continue;
                }

                builder.Append(c);
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                inString = true;
                quoteChar = c;
                builder.Append('"');
                i++;
                continue;
            }

            // Detect an unquoted property name: a run of identifier chars that
            // is followed (after optional whitespace) by a colon.
            if (IsIdentifierStart(c))
            {
                var start = i;
                i++;
                while (i < source.Length && IsIdentifierPart(source[i]))
                {
                    i++;
                }

                var name = source[start..i];
                var lookahead = i;
                while (lookahead < source.Length && char.IsWhiteSpace(source[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead < source.Length && source[lookahead] == ':')
                {
                    builder.Append('"').Append(name).Append('"');
                }
                else
                {
                    builder.Append(name);
                }

                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }

    private static bool IsIdentifierStart(char c)
        => c == '_' || c == '$' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c)
        => c == '_' || c == '$' || char.IsLetterOrDigit(c);

    /// <summary>
    /// Parses a head/foot <c>text</c> property. Accepts a plain string or the
    /// official tspan tree form (e.g. <c>['tspan', {...}, 'x', ['tspan', ...]]</c>)
    /// and flattens it into styled spans.
    /// </summary>
    private static WaveDromTextSpan[] ParseText(JsonElement element)
    {
        if (!element.TryGetProperty("text", out var text))
        {
            return [];
        }

        var spans = new List<WaveDromTextSpan>();
        ParseTextNode(text, spans, new TextStyle());
        return [.. spans];
    }

    private sealed class TextStyle
    {
        public string? Fill;
        public string? ClassName;
        public double? FontSize;
        public bool Bold;
        public bool Italic;
        public bool Overline;
        public bool Underline;
        public bool Superscript;
        public bool Subscript;
        public double Dy;

        public TextStyle Clone() => (TextStyle)MemberwiseClone();
    }

    private static void ParseTextNode(JsonElement node, List<WaveDromTextSpan> spans, TextStyle style)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                spans.Add(BuildSpan(node.GetString() ?? string.Empty, style));
                return;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                spans.Add(BuildSpan(node.ToString(), style));
                return;
            case JsonValueKind.Array:
                break;
            default:
                return;
        }

        // Array form: either ['tspan', {...}, children...] or a bare child list.
        var items = node.EnumerateArray().ToArray();
        var index = 0;
        if (items.Length > 0 && items[0].ValueKind == JsonValueKind.String &&
            items[0].GetString() == "tspan")
        {
            index = 1;
            // Optional attribute object immediately after the tag.
            if (index < items.Length && items[index].ValueKind == JsonValueKind.Object)
            {
                var childStyle = style.Clone();
                ApplyTextAttributes(items[index], childStyle);
                style = childStyle;
                index++;
            }
        }

        for (; index < items.Length; index++)
        {
            ParseTextNode(items[index], spans, style);
        }
    }

    private static WaveDromTextSpan BuildSpan(string text, TextStyle style) => new()
    {
        Text = text,
        Fill = style.Fill,
        ClassName = style.ClassName,
        FontSize = style.FontSize,
        Bold = style.Bold,
        Italic = style.Italic,
        Overline = style.Overline,
        Underline = style.Underline,
        Superscript = style.Superscript,
        Subscript = style.Subscript,
        Dy = style.Dy,
    };

    private static void ApplyTextAttributes(JsonElement attributes, TextStyle style)
    {
        foreach (var prop in attributes.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "fill":
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        style.Fill = prop.Value.GetString();
                    }

                    break;
                case "class":
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        style.ClassName = prop.Value.GetString();
                    }

                    break;
                case "font-size":
                    style.FontSize = ParseDouble(prop.Value);
                    break;
                case "font-weight":
                    style.Bold = prop.Value.ValueKind == JsonValueKind.String &&
                                 prop.Value.GetString() is "bold" or "700" or "800" or "900";
                    break;
                case "font-style":
                    style.Italic = prop.Value.ValueKind == JsonValueKind.String &&
                                   prop.Value.GetString() == "italic";
                    break;
                case "text-decoration":
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var v = prop.Value.GetString();
                        style.Overline = v?.Contains("overline", StringComparison.Ordinal) == true;
                        style.Underline = v?.Contains("underline", StringComparison.Ordinal) == true;
                    }

                    break;
                case "baseline-shift":
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var v = prop.Value.GetString();
                        style.Superscript = v == "super";
                        style.Subscript = v == "sub";
                    }

                    break;
                case "dy":
                    // SVG dy is positive downward; WaveDrom uses negative to raise.
                    var dy = ParseDouble(prop.Value);
                    if (dy is { } value)
                    {
                        style.Dy = value;
                    }

                    break;
            }
        }
    }

    private static double? ParseDouble(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(
            value.GetString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) => d,
        _ => null,
    };

    /// <summary>
    /// Port of the official render-marks.js ticktock(): expands tick/tock into
    /// per-position labels, applies the head/foot <c>every</c> stride, and
    /// leaves filtered positions null so the renderer skips them.
    /// </summary>
    private static (string?[] tick, string?[] tock) GetTicks(JsonElement element, int xMax, int hScale)
    {
        // marks = xmax / (2*hscale); ticks are marks+1, tocks are marks.
        var marks = xMax / (2 * hScale);
        var tickCount = marks + 1;
        var tockCount = marks;

        var every = element.TryGetProperty("every", out var everyElement) &&
                    everyElement.ValueKind == JsonValueKind.Number
            ? Math.Max(0, everyElement.GetInt32())
            : 0;

        var tick = new string?[Math.Max(tickCount, 0)];
        var tock = new string?[Math.Max(tockCount, 0)];

        if (element.TryGetProperty("tick", out var tickElement))
        {
            tick = ExpandTick(tickElement, tickCount, every);
        }

        if (element.TryGetProperty("tock", out var tockElement))
        {
            tock = ExpandTick(tockElement, tockCount, every);
        }

        return (tick, tock);
    }

    private static string?[] ExpandTick(JsonElement element, int length, int every)
    {
        if (length <= 0)
        {
            return [];
        }

        var result = new string?[length];
        // Numeric start offset, used both for label generation and the
        // (i + offset) % every stride check in the official code.
        var offset = 0;
        var hasNumericOffset = false;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
            {
                offset = element.GetInt32();
                hasNumericOffset = true;
                for (var i = 0; i < length; i++)
                {
                    result[i] = (i + offset).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                break;
            }
            case JsonValueKind.String:
            {
                var parts = element.GetString()?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (parts.Length == 1 && int.TryParse(parts[0], out var o))
                {
                    offset = o;
                    hasNumericOffset = true;
                    for (var i = 0; i < length; i++)
                    {
                        result[i] = (i + offset).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    for (var i = 0; i < length && i < parts.Length; i++)
                    {
                        result[i] = parts[i];
                    }
                }

                break;
            }
            case JsonValueKind.Array:
            {
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (i >= length) break;
                    result[i++] = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                }

                break;
            }
        }

        // Apply the every stride (official: skip when (i + offset) % every != 0).
        if (every > 0)
        {
            var baseOffset = hasNumericOffset ? offset : 0;
            for (var i = 0; i < length; i++)
            {
                if ((i + baseOffset) % every != 0)
                {
                    result[i] = null;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Mutable layout cursor mirroring the official rec.js <c>state</c> object.
    /// <see cref="X"/> is the running x offset built up from per-group deltas
    /// (25 for named groups, 10 otherwise); <see cref="Xx"/> is the value a
    /// completed (sub)group reports as its bracket x.
    /// </summary>
    private sealed class RecState
    {
        public int X;
        public int Xx;
        public int Y;
        public int Depth;
        public string? Name;
        public int HScale;
        public int XMinCfg;
        public int XMaxCfg;
    }

    private static void ParseSignal(
        JsonElement signal,
        List<WaveDromLane> lanes,
        List<WaveDromGroup> groups,
        int hScale,
        int xMinCfg,
        int xMaxCfg)
    {
        var state = new RecState
        {
            HScale = hScale,
            XMinCfg = xMinCfg,
            XMaxCfg = xMaxCfg
        };
        Rec(signal.EnumerateArray().ToArray(), null, false, state, lanes, groups, hScale);
    }

    /// <summary>
    /// Port of the official rec.js. Walks a signal array, emitting lanes and
    /// groups. The group x coordinate is accumulated from <c>deltaX</c>
    /// (25 when the array/group has a leading name, else 10), so nested groups
    /// step 25px apart exactly like the reference renderer.
    /// </summary>
    private static void Rec(
        IReadOnlyList<JsonElement> items,
        string? explicitName,
        bool hasExplicitName,
        RecState state,
        List<WaveDromLane> lanes,
        List<WaveDromGroup> groups,
        int hScale)
    {
        var deltaX = 10;
        string? name = null;
        var startIndex = 0;

        if (hasExplicitName)
        {
            name = explicitName;
            deltaX = 25;
        }
        else if (items.Count > 0 &&
                 items[0].ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            name = items[0].ValueKind == JsonValueKind.String
                ? items[0].GetString()
                : items[0].ToString();
            deltaX = 25;
            startIndex = 1;
        }

        state.X += deltaX;
        state.Depth++;

        for (var i = startIndex; i < items.Count; i++)
        {
            var item = items[i];

            // Nested group: array form ['Name', ...] or object form
            // { name, signal: [...] } (the object form is a convenience
            // extension; the official grammar uses the array form).
            if (item.ValueKind == JsonValueKind.Array)
            {
                var oldY = state.Y;
                Rec(item.EnumerateArray().ToArray(), null, false, state, lanes, groups, hScale);
                groups.Add(new WaveDromGroup
                {
                    Name = state.Name,
                    X = state.Xx,
                    Y = oldY,
                    Height = state.Y - oldY
                });
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("signal", out var nested) &&
                nested.ValueKind == JsonValueKind.Array)
            {
                var oldY = state.Y;
                var groupName = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()
                    : null;
                Rec(nested.EnumerateArray().ToArray(), groupName, true, state, lanes, groups, hScale);
                groups.Add(new WaveDromGroup
                {
                    Name = state.Name,
                    X = state.Xx,
                    Y = oldY,
                    Height = state.Y - oldY
                });
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            lanes.Add(ParseLane(item, state.Depth, state.X, state));
            state.Y += 1;
        }

        state.Xx = state.X;
        state.X -= deltaX;
        state.Depth--;
        state.Name = name;
    }

    private static WaveDromLane ParseLane(JsonElement entry, int level, int xOffset, RecState state)
    {
        var period = entry.TryGetProperty("period", out var periodElement) &&
                     periodElement.ValueKind == JsonValueKind.Number
            ? Math.Max(1, periodElement.GetDouble())
            : 1;
        var phase = entry.TryGetProperty("phase", out var phaseElement) &&
                    phaseElement.ValueKind == JsonValueKind.Number
            ? phaseElement.GetDouble()
            : 0;

        var wave = entry.TryGetProperty("wave", out var waveElement) &&
                   waveElement.ValueKind == JsonValueKind.String
            ? waveElement.GetString() ?? string.Empty
            : string.Empty;

        var data = entry.TryGetProperty("data", out var dataElement)
            ? ParseData(dataElement)
            : [];

        // Official parse-wave-lanes.js:
        //   lane.phase  = phase*2 + xmin_cfg   (half-brick offset incl. hbounds)
        //   tmp0[1]     = phase + xmin_cfg/2   (draw-group x translation units)
        //   extra       = period*hscale - 1
        var phaseBricks = phase * 2 + state.XMinCfg;
        var phaseUnits = phase + state.XMinCfg / 2.0;
        var (bricks, unseenMarkers) = ParseWaveLane(wave, (int)(period * state.HScale - 1), phaseBricks);

        // render-wave-lane.js clamps visible bricks to xmax_cfg - xmin_cfg.
        var visibleMax = state.XMaxCfg == int.MaxValue
            ? int.MaxValue
            : Math.Max(0, state.XMaxCfg - state.XMinCfg);
        if (bricks.Length > visibleMax)
        {
            bricks = bricks[..visibleMax];
        }

        var labels = data.Skip(unseenMarkers).ToArray();

        return new WaveDromLane
        {
            Name = entry.TryGetProperty("name", out var nameElement) &&
                   nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : string.Empty,
            Period = (int)period,
            Phase = phase,
            Wave = wave,
            PhaseBricks = phaseBricks,
            PhaseUnits = phaseUnits,
            Bricks = bricks,
            Labels = [.. labels],
            GroupLevel = level,
            XOffset = xOffset,
            Node = entry.TryGetProperty("node", out var nodeElement) &&
                   nodeElement.ValueKind == JsonValueKind.String
                ? nodeElement.GetString()
                : null
        };
    }

    private static string[] ParseData(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? string.Empty)
                .ToArray(),
            JsonValueKind.String => (element.GetString() ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries),
            _ => []
        };
    }

    // Port of gen-brick.js
    private static string[] GenBrick(string[] texts, int extra, int times)
    {
        var result = new List<string>();
        if (texts.Length == 4)
        {
            for (var j = 0; j < times; j++)
            {
                result.Add(texts[0]);
                for (var i = 0; i < extra; i++) result.Add(texts[1]);
                result.Add(texts[2]);
                for (var i = 0; i < extra; i++) result.Add(texts[3]);
            }

            return [.. result];
        }

        if (texts.Length == 1)
        {
            texts = [texts[0], texts[0]];
        }

        result.Add(texts[0]);
        var count = times * 2 * (extra + 1) - 1;
        for (var i = 0; i < count; i++)
        {
            result.Add(texts[1]);
        }

        return [.. result];
    }

    // Port of gen-first-wave-brick.js
    private static string[] GenFirstWaveBrick(char text, int extra, int times)
    {
        string[] brick = text switch
        {
            'p' => ["pclk", "111", "nclk", "000"],
            'n' => ["nclk", "000", "pclk", "111"],
            'P' => ["Pclk", "111", "nclk", "000"],
            'N' => ["Nclk", "000", "pclk", "111"],
            'l' or 'L' or '0' => ["000"],
            'h' or 'H' or '1' => ["111"],
            '=' => ["vvv-2"],
            '2' => ["vvv-2"],
            '3' => ["vvv-3"],
            '4' => ["vvv-4"],
            '5' => ["vvv-5"],
            '6' => ["vvv-6"],
            '7' => ["vvv-7"],
            '8' => ["vvv-8"],
            '9' => ["vvv-9"],
            'd' => ["ddd"],
            'u' => ["uuu"],
            'z' => ["zzz"],
            _ => ["xxx"]
        };
        return GenBrick(brick, extra, times);
    }

    // Port of gen-wave-brick.js
    private static string[] GenWaveBrick(string text, int extra, int times)
    {
        if (text.Length != 2)
        {
            return GenBrick(["xxx"], extra, times);
        }

        var a = text[0];
            var b = text[1];

        var x1 = new Dictionary<char, string>
        {
            ['p'] = "pclk", ['n'] = "nclk", ['P'] = "Pclk", ['N'] = "Nclk",
            ['h'] = "pclk", ['l'] = "nclk", ['H'] = "Pclk", ['L'] = "Nclk"
        };

        var x2 = new Dictionary<char, string>
        {
            ['0'] = "0", ['1'] = "1", ['x'] = "x", ['d'] = "d", ['u'] = "u", ['z'] = "z",
            ['='] = "v", ['2'] = "v", ['3'] = "v", ['4'] = "v", ['5'] = "v",
            ['6'] = "v", ['7'] = "v", ['8'] = "v", ['9'] = "v"
        };

        var x3 = new Dictionary<char, string>
        {
            ['='] = "-2", ['2'] = "-2", ['3'] = "-3", ['4'] = "-4", ['5'] = "-5",
            ['6'] = "-6", ['7'] = "-7", ['8'] = "-8", ['9'] = "-9"
        };

        var y1 = new Dictionary<char, string>
        {
            ['p'] = "0", ['n'] = "1", ['P'] = "0", ['N'] = "1",
            ['h'] = "1", ['l'] = "0", ['H'] = "1", ['L'] = "0",
            ['0'] = "0", ['1'] = "1", ['x'] = "x", ['d'] = "d", ['u'] = "u", ['z'] = "z",
            ['='] = "v", ['2'] = "v", ['3'] = "v", ['4'] = "v", ['5'] = "v",
            ['6'] = "v", ['7'] = "v", ['8'] = "v", ['9'] = "v"
        };

        var y2 = new Dictionary<char, string>
        {
            ['='] = "-2", ['2'] = "-2", ['3'] = "-3", ['4'] = "-4", ['5'] = "-5",
            ['6'] = "-6", ['7'] = "-7", ['8'] = "-8", ['9'] = "-9"
        };

        var x4 = new Dictionary<char, string>
        {
            ['p'] = "111", ['n'] = "000", ['P'] = "111", ['N'] = "000",
            ['h'] = "111", ['l'] = "000", ['H'] = "111", ['L'] = "000",
            ['0'] = "000", ['1'] = "111", ['x'] = "xxx", ['d'] = "ddd", ['u'] = "uuu", ['z'] = "zzz",
            ['='] = "vvv-2", ['2'] = "vvv-2", ['3'] = "vvv-3", ['4'] = "vvv-4", ['5'] = "vvv-5",
            ['6'] = "vvv-6", ['7'] = "vvv-7", ['8'] = "vvv-8", ['9'] = "vvv-9"
        };

        var x5 = new Dictionary<char, string>
        {
            ['p'] = "nclk", ['n'] = "pclk", ['P'] = "nclk", ['N'] = "pclk"
        };

        var x6 = new Dictionary<char, string>
        {
            ['p'] = "000", ['n'] = "111", ['P'] = "000", ['N'] = "111"
        };

        var xclude = new Dictionary<string, string>
        {
            ["hp"] = "111", ["Hp"] = "111", ["ln"] = "000", ["Ln"] = "000",
            ["nh"] = "111", ["Nh"] = "111", ["pl"] = "000", ["Pl"] = "000"
        };

        if (x1.TryGetValue(b, out var tmp1))
        {
            if (xclude.TryGetValue(text, out var excluded))
            {
                tmp1 = excluded;
            }

            var tmp0 = x4[b];
            if (x5.TryGetValue(b, out var tmp5))
            {
                // pnPN: clock bricks.
                return GenBrick([tmp1, tmp0, tmp5, x6[b]], extra, times);
            }

            // hlHL: level bricks.
            return GenBrick([tmp1, tmp0], extra, times);
        }

        if (x2.TryGetValue(b, out var tmp2))
        {
            if (!y1.TryGetValue(a, out var tmp3))
            {
                return GenBrick(["xxx"], extra, times);
            }

            // Soft curves.
            x3.TryGetValue(b, out var suffix);
            return GenBrick([tmp3 + "m" + tmp2 + y2.GetValueOrDefault(a, string.Empty) + suffix, x4[b]], extra, times);
        }

        return GenBrick(["xxx"], extra, times);
    }

    // Port of parse-wave-lane.js
    private static (string[] bricks, int unseenMarkers) ParseWaveLane(string src, int extra, double phase)
    {
        if (string.IsNullOrEmpty(src))
        {
            return ([], 0);
        }

        var stack = new Queue<char>(src);
        var next = stack.Dequeue();

        var repeats = 1;
        while (stack.Count > 0 && (stack.Peek() == '.' || stack.Peek() == '|'))
        {
            stack.Dequeue();
            repeats++;
        }

        var bricks = new List<string>();
        bricks.AddRange(GenFirstWaveBrick(next, extra, repeats));

        var subCycle = false;
        while (stack.Count > 0)
        {
            var top = next;
            next = stack.Dequeue();
            if (next == '<')
            {
                subCycle = true;
                if (stack.Count == 0) break;
                next = stack.Dequeue();
            }

            if (next == '>')
            {
                subCycle = false;
                if (stack.Count == 0) break;
                next = stack.Dequeue();
            }

            repeats = 1;
            while (stack.Count > 0 && (stack.Peek() == '.' || stack.Peek() == '|'))
            {
                stack.Dequeue();
                repeats++;
            }

            if (subCycle)
            {
                bricks.AddRange(GenWaveBrick(top.ToString() + next, 0, Math.Max(0, repeats - 1)));
            }
            else
            {
                bricks.AddRange(GenWaveBrick(top.ToString() + next, extra, repeats));
            }
        }

        // Shift out unseen bricks due to phase shift.
        var unseen = new List<string>();
        for (var i = 0; i < phase && bricks.Count > 0; i++)
        {
            unseen.Add(bricks[0]);
            bricks.RemoveAt(0);
        }

        var unseenMarkers = 0;
        if (unseen.Count > 0)
        {
            unseenMarkers = FindLaneMarkers(unseen).Length;
            if (FindLaneMarkers([unseen[^1]]).Length == 1 &&
                bricks.Count > 0 &&
                FindLaneMarkers([bricks[0]]).Length == 1)
            {
                unseenMarkers--;
            }
        }

        return ([.. bricks], unseenMarkers);
    }

    // Port of find-lane-marker.js — returns half-brick positions of data label centers.
    // Positions are half-brick units and may be fractional (e.g. a two-half-brick bus
    // centers at x.5), so the half-step must not be truncated.
    internal static double[] FindLaneMarkers(IReadOnlyList<string> bricks)
    {
        var result = new List<double>();
        var gcount = 0;
        var lcount = 0;

        foreach (var brick in bricks)
        {
            if (brick.StartsWith("vvv-", StringComparison.Ordinal))
            {
                lcount++;
            }
            else
            {
                if (lcount != 0)
                {
                    result.Add(gcount - ((lcount + 1) / 2.0));
                    lcount = 0;
                }
            }

            gcount++;
        }

        if (lcount != 0)
        {
            result.Add(gcount - ((lcount + 1) / 2.0));
        }

        return [.. result];
    }
}
