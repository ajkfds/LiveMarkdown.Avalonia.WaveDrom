using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Avalonia control that parses WaveDrom JSON source and renders timing diagrams
/// using native drawing APIs, following the official WaveDrom skin geometry
/// (20x20 half-bricks, 30px lane pitch).
/// </summary>
public class WaveDromPresenter : Control
{
    /// <summary>
    /// Defines the <see cref="Text"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<WaveDromPresenter, string?>(nameof(Text));

    /// <summary>
    /// WaveDrom diagram definition text (JSON).
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="Foreground"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<WaveDromPresenter>();

    /// <summary>
    /// Foreground brush for diagram text and waveform strokes.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="BackgroundBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<WaveDromPresenter, IBrush?>(nameof(BackgroundBrush), Brushes.Transparent);

    /// <summary>
    /// Background brush for the entire diagram. Default is Transparent.
    /// </summary>
    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="MarkerBrush"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<WaveDromPresenter, IBrush?>(nameof(MarkerBrush));

    /// <summary>
    /// Brush for data box fills. Default falls back to theme colors.
    /// </summary>
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="FontFamily"/> property.
    /// </summary>
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<WaveDromPresenter>();

    /// <summary>
    /// Font family for diagram text. Default is system default font.
    /// </summary>
    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="FontSize"/> property.
    /// </summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<WaveDromPresenter>();

    /// <summary>
    /// Font size for diagram text. Default is 12.0.
    /// </summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private WaveDromDiagram? _diagram;
    private Exception? _error;
    private Size _size;
    private readonly Dictionary<string, StreamGeometry> _brickGeometryCache = [];
    private readonly Dictionary<string, IPen> _brickPenCache = [];

    static WaveDromPresenter()
    {
        AffectsRender<WaveDromPresenter>(
            TextProperty,
            ForegroundProperty,
            BackgroundBrushProperty,
            MarkerBrushProperty,
            FontFamilyProperty,
            FontSizeProperty);
        AffectsMeasure<WaveDromPresenter>(TextProperty, FontSizeProperty);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _diagram = null;
            _error = null;
            _size = default;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_diagram is null && _error is null)
        {
            BuildState();
        }

        return _size;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext dc)
    {
        if (_diagram is null && _error is null)
        {
            BuildState();
        }

        if (BackgroundBrush is not null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }

        if (_error is not null)
        {
            DrawError(dc, _error);
            return;
        }

        if (_diagram is { } diagram)
        {
            RenderDiagram(dc, diagram);
        }
    }

    private void BuildState()
    {
        var text = Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _diagram = WaveDromParser.Parse(text);
            _size = MeasureDiagram(_diagram, new Typeface(FontFamily));
        }
        catch (Exception ex)
        {
            _error = ex;
            _size = MeasureError(ex);
        }
    }

    private Size MeasureDiagram(WaveDromDiagram diagram, Typeface typeface)
    {
        var xg = ComputeLeftGutter(diagram, typeface, FontSize);
        var waveWidth = diagram.XMax * WaveDromParser.Xs;
        var width = xg + waveWidth + 10;

        // Official insert-svg-template.js height:
        //   lanes*yo + yh0 + yh1 + yf0 + yf1.
        var yh0 = diagram.HeadTick.Length > 0 || diagram.HeadTock.Length > 0 ? 20 : 0;
        var yh1 = diagram.HeadTextSpans.Length > 0 ? 46 : 0;
        var yf0 = diagram.FootTick.Length > 0 || diagram.FootTock.Length > 0 ? 20 : 0;
        var yf1 = diagram.FootTextSpans.Length > 0 ? 46 : 0;
        var height = diagram.Lanes.Length * WaveDromParser.Yo + yh0 + yh1 + yf0 + yf1 + WaveDromParser.Y0 + 10;

        return new Size(Math.Max(width, 100), Math.Max(height, 40));
    }

    /// <summary>
    /// Computes the wave-area left origin (official renderSignal xg). The gutter
    /// must fit the widest signal name plus its rec.js group offset, so group
    /// brackets (absolute rec coordinates) sit just left of the names.
    /// </summary>
    private static double ComputeLeftGutter(WaveDromDiagram diagram, Typeface typeface, double fontSize)
    {
        // tgo = -10 in official lane.js (name anchor x relative to the wave origin).
        const double tgo = -10;
        var maxContent = 0d;
        foreach (var lane in diagram.Lanes)
        {
            var nameWidth = MeasureTextWidth(lane.Name, typeface, fontSize);
            maxContent = Math.Max(maxContent, nameWidth + lane.XOffset);
        }

        var xg = Math.Ceiling((maxContent - tgo) / WaveDromParser.Xs) * WaveDromParser.Xs;
        return Math.Max(xg, WaveDromParser.Xs);
    }

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);
        return formatted.Width;
    }

    private static Size MeasureError(Exception error)
    {
        var message = $"Failed to render WaveDrom diagram: {error.Message}";
        var width = Math.Clamp(message.Length * 6.5 + 16, 240, 520);
        var lines = Math.Max(1, Math.Ceiling(message.Length * 6.5 / Math.Max(width - 16, 1)));
        var height = lines * 17 + 16;
        return new Size(width, height);
    }

    private void DrawError(DrawingContext dc, Exception error)
    {
        var text = new FormattedText(
            $"Failed to render WaveDrom diagram: {error.Message}",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            FontSize,
            Foreground ?? Brushes.Red)
        {
            MaxTextWidth = Bounds.Width > 16 ? Bounds.Width - 16 : double.PositiveInfinity,
            TextAlignment = TextAlignment.Left
        };
        dc.DrawText(text, new Point(8, 8));
    }

    private void RenderDiagram(DrawingContext dc, WaveDromDiagram diagram)
    {
        var stroke = Foreground ?? Brushes.Black;
        // Mark lines use the official muted gray (stroke:#888, dash 1,3).
        var thinPen = new Pen(Brushes.Gray, 0.5, new DashStyle([1, 3], 0));
        var typeface = new Typeface(FontFamily);
        var hScale = diagram.HScale;
        var mmStep = 2 * hScale * WaveDromParser.Xs;
        // Official renderSignal xg: wave origin, sized so signal names and their
        // rec.js group offsets fit in the left gutter.
        var xg = ComputeLeftGutter(diagram, typeface, FontSize);

        var y = 4d;

        // Official render-marks.js: head/foot text is centered over the wave
        // area (xmax * xs / 2), not over the left gutter.
        var waveCenterX = xg + diagram.XMax * WaveDromParser.Xs / 2;

        // Official lane gaps (parse-config.js): yh0/yf0 = 20 when head/foot has
        // tick/tock; yh1/yf1 = 46 when head/foot has text. Lanes start after
        // the head gap, and foot content is placed at fixed offsets below the
        // wave area rather than by incremental stacking.
        var hasHeadNumbers = diagram.HeadTick.Length > 0 || diagram.HeadTock.Length > 0;
        var hasHeadText = diagram.HeadTextSpans.Length > 0;
        var hasFootNumbers = diagram.FootTick.Length > 0 || diagram.FootTock.Length > 0;
        var hasFootText = diagram.FootTextSpans.Length > 0;

        var yh0 = hasHeadNumbers ? 20 : 0;
        var yh1 = hasHeadText ? 46 : 0;
        var yf0 = hasFootNumbers ? 20 : 0;
        var yf1 = hasFootText ? 46 : 0;

        var diagramTop = y;
        var headTop = diagramTop;          // top of the head region
        var lanesTop = diagramTop + yh0 + yh1; // wave area origin
        var waveHeight = diagram.Lanes.Length * WaveDromParser.Yo;
        var footBaseTop = lanesTop + waveHeight;

        // Head text baseline (official captext head y = yh0 ? -33 : -13).
        if (hasHeadText)
        {
            var headTextY = lanesTop - (hasHeadNumbers ? 33 : 13);
            DrawTextSpans(dc, diagram.HeadTextSpans, typeface, waveCenterX, headTextY);
        }

        // Head tick/tock numbers (official y = -5 relative to lanes). class "muted".
        if (hasHeadNumbers)
        {
            var headNumberY = lanesTop - 5;
            for (var i = 0; i < diagram.HeadTick.Length; i++)
            {
                if (diagram.HeadTick[i] is { } tick)
                {
                    DrawMiddleAnchoredText(dc, tick, typeface, MutedBrush, xg + i * mmStep, headNumberY);
                }
            }

            for (var i = 0; i < diagram.HeadTock.Length; i++)
            {
                if (diagram.HeadTock[i] is { } tock)
                {
                    DrawMiddleAnchoredText(dc, tock, typeface, MutedBrush, xg + i * mmStep + mmStep / 2, headNumberY);
                }
            }
        }

        // Vertical mark lines.
        if (diagram.ShowMarks)
        {
            var markCount = diagram.XMax / (2 * hScale) + 1;
            for (var i = 0; i < markCount; i++)
            {
                var x = xg + i * mmStep;
                dc.DrawLine(thinPen, new Point(x, lanesTop), new Point(x, lanesTop + diagram.Lanes.Length * WaveDromParser.Yo));
            }
        }

        // Lanes.
        for (var i = 0; i < diagram.Lanes.Length; i++)
        {
            var lane = diagram.Lanes[i];
            var laneY = lanesTop + i * WaveDromParser.Yo + WaveDromParser.Y0;

            // Signal names use the official "info" class (#0041c4).
            if (lane.Name.Length > 0)
            {
                DrawRightAlignedText(dc, lane.Name, typeface, InfoBrush, xg - 10, laneY + 8);
            }

            // Official render-wave-lane.js x offset for the draw group:
            //   units = phase + xmin_cfg/2
            //   xoffset = units > 0 ? ceil(2*units) - 2*units : -2*units
            var units = lane.PhaseUnits;
            var xoffset = units > 0
                ? Math.Ceiling(2 * units) - 2 * units
                : -2 * units;
            var laneX = xg + xoffset * WaveDromParser.Xs;

            for (var b = 0; b < lane.Bricks.Length; b++)
            {
                DrawBrick(dc, lane.Bricks[b], laneX + b * WaveDromParser.Xs, laneY);
            }

            // Data labels at marker centers (official render-wave-lane.js:
            // x = marker * xs + xlabel, y = ym, text-anchor: middle).
            var markers = WaveDromParser.FindLaneMarkers(lane.Bricks);
            for (var m = 0; m < markers.Length && m < lane.Labels.Length; m++)
            {
                if (lane.Labels[m] is { } label)
                {
                    var labelX = laneX + markers[m] * WaveDromParser.Xs + WaveDromXlabel;
                    // Data labels use the default text fill (black).
                    DrawMiddleAnchoredText(dc, label, typeface, TextBrush, labelX, laneY + WaveDromYm);
                }
            }
        }

        // Gaps property (official render-gaps.js).
        if (!string.IsNullOrEmpty(diagram.Gaps))
        {
            DrawGaps(dc, diagram, xg, lanesTop);
        }

        // Group brackets (official render-groups.js):
        //   path: m (x+0.5),(y*yo+3.5) c -3,0 -5,2 -5,5 l 0,(height*yo-16) c 0,3 2,5 5,5
        // The bracket x is the accumulated rec.js offset (25/35/...), so it sits
        // just left of the signal names.
        var groupPen = new Pen(stroke, 1);
        foreach (var group in diagram.Groups)
        {
            var gx = group.X + 0.5;
            var gy = lanesTop + group.Y * WaveDromParser.Yo + 3.5;
            var runLength = group.Height * WaveDromParser.Yo - 16;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(gx, gy), false);
                // Top curl: 3,-0 5,2 5,5.
                ctx.CubicBezierTo(new Point(gx - 3, gy), new Point(gx - 5, gy + 2), new Point(gx - 5, gy + 5));
                // Vertical run.
                ctx.LineTo(new Point(gx - 5, gy + 5 + runLength));
                // Bottom curl: 0,3 2,5 5,5.
                ctx.CubicBezierTo(
                    new Point(gx - 5, gy + 5 + runLength + 3),
                    new Point(gx - 3, gy + 5 + runLength + 5),
                    new Point(gx, gy + 5 + runLength + 5));
                ctx.EndFigure(false);
            }

            dc.DrawGeometry(null, groupPen, geometry);

            if (group.Name is { } groupName)
            {
                var tx = group.X - 10;
                var ty = lanesTop + WaveDromParser.Yo * (group.Y + group.Height / 2d);
                // Group labels use the official "info" class (#0041c4).
                DrawRotatedText(dc, groupName, typeface, InfoBrush, tx, ty);
            }
        }

        // Arrows / edges (official render-arcs.js + arc-shape.js).
        if (diagram.Edges.Length > 0)
        {
            DrawEdges(dc, diagram, xg, lanesTop, typeface);
        }

        // Foot tick/tock numbers (official y = gy + 15 relative to lanes origin).
        if (hasFootNumbers)
        {
            var footNumberY = footBaseTop + 15;
            for (var i = 0; i < diagram.FootTick.Length; i++)
            {
                if (diagram.FootTick[i] is { } tick)
                {
                    DrawMiddleAnchoredText(dc, tick, typeface, MutedBrush, xg + i * mmStep, footNumberY);
                }
            }

            for (var i = 0; i < diagram.FootTock.Length; i++)
            {
                if (diagram.FootTock[i] is { } tock)
                {
                    DrawMiddleAnchoredText(dc, tock, typeface, MutedBrush, xg + i * mmStep + mmStep / 2, footNumberY);
                }
            }
        }

        // Foot text (official captext y = gy + (yf0 ? 45 : 25)).
        if (hasFootText)
        {
            var footTextY = footBaseTop + (hasFootNumbers ? 45 : 25);
            DrawTextSpans(dc, diagram.FootTextSpans, typeface, waveCenterX, footTextY);
        }
    }

    // Official lane.js defaults: xlabel = 6 (data label x offset from the marker),
    // ym = 15 (data label baseline y within the lane).
    private const double WaveDromXlabel = 6;
    private const double WaveDromYm = 15;

    /// <summary>
    /// Draws the top-level <c>gaps</c> string, ported from the official
    /// render-gaps.js: backdrops, vertical lines, square/round brackets, and the
    /// 's' signal gap markers. The wave is scaled by hscale * xs * 2 per char.
    /// </summary>
    private void DrawGaps(DrawingContext dc, WaveDromDiagram diagram, double xg, double lanesTop)
    {
        var gaps = diagram.Gaps!.Trim();
        if (gaps.Length == 0)
        {
            return;
        }

        var laneCount = diagram.Lanes.Length;
        var lanesHeight = laneCount * WaveDromParser.Yo;
        var scale = diagram.HScale * WaveDromParser.Xs * 2;
        var linePen = new Pen(Brushes.Black, 1);
        var backdropBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

        void DrawVLine(double x)
        {
            dc.DrawLine(linePen, new Point(x, lanesTop), new Point(x, lanesTop + lanesHeight));
        }

        void DrawBackdrop(double width)
        {
            dc.DrawRectangle(backdropBrush, null, new Rect(xg - width / 2, lanesTop, width, lanesHeight));
        }

        var circlePen = new Pen(Brushes.Black, 1);
        void DrawRect(double x, double y, double w, double h, double radius)
        {
            dc.DrawRectangle(null, linePen, new Rect(x, y, w, h), radius, radius);
        }

        for (var i = 0; i < gaps.Length; i++)
        {
            var c = gaps[i];
            if (c == '.')
            {
                continue;
            }

            var offset = char.IsLower(c) ? 0.5 : 0.0;
            var x = xg + scale * (i + offset);

            switch (c)
            {
                case '0':
                    DrawBackdrop(4);
                    break;
                case '1':
                case '|':
                    DrawBackdrop(4);
                    DrawVLine(x);
                    break;
                case '2':
                    DrawBackdrop(4);
                    DrawVLine(x - 2);
                    DrawVLine(x + 2);
                    break;
                case '3':
                    DrawBackdrop(6);
                    DrawVLine(x - 3);
                    DrawVLine(x);
                    DrawVLine(x + 3);
                    break;
                case '[':
                    // Square left: M 2 0 h -4 v (h-1) h 4
                    DrawRect(x - 2, lanesTop, 4, lanesHeight - 1, 0);
                    break;
                case ']':
                    DrawRect(x - 2, lanesTop, 4, lanesHeight - 1, 0);
                    break;
                case '(':
                    // Round left.
                    DrawRoundBracket(dc, circlePen, x, lanesTop, lanesHeight, rightSide: false, doubleSide: false, outerOnly: false);
                    break;
                case ')':
                    DrawRoundBracket(dc, circlePen, x, lanesTop, lanesHeight, rightSide: true, doubleSide: false, outerOnly: false);
                    break;
                case 's':
                    for (var idx = 0; idx < laneCount; idx++)
                    {
                        if (idx < diagram.LaneWaves.Length && diagram.LaneWaves[idx].Length > i)
                        {
                            DrawGapGlyph(dc, xg + 2, lanesTop + 5 + WaveDromParser.Yo * idx - WaveDromParser.Y0);
                        }
                    }
                    break;
            }
        }
    }

    /// <summary>Draws a rounded gap bracket (approximation of the SVG arc brackets).</summary>
    private static void DrawRoundBracket(
        DrawingContext dc, IPen pen, double x, double top, double height, bool rightSide, bool doubleSide, bool outerOnly)
    {
        // Approximate the official a4 4 arc brackets with a rounded rectangle side.
        var r = 4.0;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var sign = rightSide ? -1.0 : 1.0;
        var startX = x + sign * 2;
        ctx.BeginFigure(new Point(startX, top), false);
        ctx.CubicBezierTo(new Point(startX - sign * 2, top), new Point(startX - sign * 4, top + 2), new Point(startX - sign * 4, top + r));
        ctx.LineTo(new Point(startX - sign * 4, top + height - r));
        ctx.CubicBezierTo(
            new Point(startX - sign * 4, top + height - 2),
            new Point(startX - sign * 2, top + height),
            new Point(startX, top + height));
        ctx.EndFigure(false);
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>Draws a small 'gap' glyph (used by the 's' gaps marker).</summary>
    private void DrawGapGlyph(DrawingContext dc, double x, double y)
    {
        if (WaveDromSkin.Bricks.TryGetValue("gap", out var parts))
        {
            using var _ = dc.PushTransform(Matrix.CreateTranslation(x, y));
            foreach (var part in parts)
            {
                var geometry = GetCachedBrickGeometry("gap", part.Data);
                var (brush, pen) = ResolveStyle(part.Class);
                dc.DrawGeometry(brush, pen, geometry);
            }
        }
    }

    // Official render-arcs.js arrow color.
    private static readonly Color ArcColor = Color.FromRgb(0x00, 0x41, 0xC4);

    // Official default skin text classes:
    //   .muted -> #aaa (head/foot tick/tock numbers)
    //   .info  -> #0041c4 (signal names and group labels)
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x41, 0xC4));
    private static readonly IBrush TextBrush = Brushes.Black;

    /// <summary>
    /// Draws the <c>node</c>/<c>edge</c> arrows, ported from the official
    /// render-arcs.js (event resolution) and arc-shape.js (path geometry).
    /// Event anchors come from each lane's <c>node</c> string; edges connect
    /// them with straight or curved segments and optional arrowheads.
    /// </summary>
    private void DrawEdges(DrawingContext dc, WaveDromDiagram diagram, double xg, double lanesTop, Typeface typeface)
    {
        // Resolve node event anchors: char -> point (relative to wave origin).
        var events = new Dictionary<char, Point>();
        for (var i = 0; i < diagram.Lanes.Length; i++)
        {
            var lane = diagram.Lanes[i];
            if (lane.Node is not { Length: > 0 } node)
            {
                continue;
            }

            var period = lane.Period;
            // Official render-arcs.js uses lane.phase (= phase*2 + xmin_cfg).
            var phase2 = lane.PhaseBricks;
            var laneRowY = i * WaveDromParser.Yo + WaveDromParser.Y0 + WaveDromParser.Ys * 0.5;

            for (var pos = 0; pos < node.Length; pos++)
            {
                var c = node[pos];
                if (c == '.')
                {
                    continue;
                }

                var ex = WaveDromParser.Xs * (2 * pos * period * diagram.HScale - phase2) + WaveDromXlabel;
                events[c] = new Point(ex, laneRowY);
            }
        }

        var arcPen = new Pen(new SolidColorBrush(ArcColor), 1);
        var arcFontSize = diagram.ArcFontSize;

        foreach (var edge in diagram.Edges)
        {
            var spec = edge.Trim();
            var spaceIndex = spec.IndexOf(' ');
            var token = spaceIndex >= 0 ? spec[..spaceIndex] : spec;
            var label = spaceIndex >= 0 ? spec[(spaceIndex + 1)..] : null;

            if (token.Length < 2)
            {
                continue;
            }

            var fromChar = token[0];
            var toChar = token[^1];
            var shape = token.Length > 2 ? token[1..^1] : "-";

            if (!events.TryGetValue(fromChar, out var fromRel) ||
                !events.TryGetValue(toChar, out var toRel))
            {
                continue;
            }

            // Convert to absolute drawing coordinates.
            var from = new Point(xg + fromRel.X, lanesTop + fromRel.Y);
            var to = new Point(xg + toRel.X, lanesTop + toRel.Y);

            var arc = BuildArc(shape, from, to, label is not null);
            dc.DrawGeometry(null, arcPen, arc.Path);

            if (arc.Head)
            {
                DrawArrowMarker(dc, to, arc.EndDir, forward: true);
            }

            if (arc.Tail)
            {
                DrawArrowMarker(dc, from, arc.StartDir, forward: false);
            }

            if (arc.Tee)
            {
                // Official arc-shape.js '+' uses the SVG "tee" marker at both
                // ends: a short vertical bar perpendicular to the edge.
                DrawTeeMarker(dc, from, arc.StartDir);
                DrawTeeMarker(dc, to, arc.EndDir);
            }

            if (label is { Length: > 0 })
            {
                DrawArcLabel(dc, label, typeface, arc.Label, arcFontSize);
            }
        }

        // Lower-case event names are drawn as standalone labels (official behavior).
        foreach (var (name, rel) in events)
        {
            if (!char.IsLower(name))
            {
                continue;
            }

            var x = xg + rel.X;
            if (x > 0)
            {
                DrawArcLabel(dc, name.ToString(), typeface, new Point(x, lanesTop + rel.Y), arcFontSize);
            }
        }
    }

    private readonly record struct ArcResult(
        StreamGeometry Path,
        bool Head,
        bool Tail,
        bool Tee,
        double StartDir,
        double EndDir,
        Point Label);

    /// <summary>
    /// Builds an edge path following the official arc-shape.js geometry.
    /// Returns the geometry, arrow head/tail flags, entry/exit tangent angles,
    /// and the label anchor point.
    /// </summary>
    private static ArcResult BuildArc(string shape, Point from, Point to, bool hasLabel)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lx = (from.X + to.X) / 2;
        var ly = (from.Y + to.Y) / 2;

        // Arrow markers are '<' (start) and '>' (end) around a base shape.
        var head = shape.EndsWith('>');
        var tail = shape.StartsWith('<');
        var baseShape = shape.Trim('<', '>');
        var tee = baseShape == "+";

        var straightAngle = Math.Atan2(dy, dx);
        var startDir = straightAngle;
        var endDir = straightAngle;

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        // For '-' (and the arrow '-' variants) arc-shape.js emits no `d`, so
        // render-arcs.js falls back to a straight line from -> to.
        if (baseShape is "-" or "")
        {
            ctx.BeginFigure(from, false);
            ctx.LineTo(to);
            ctx.EndFigure(false);
            return new ArcResult(geometry, head, tail, tee, startDir, endDir, new Point(lx, ly));
        }

        switch (baseShape)
        {
            case "~":
            {
                var c1 = new Point(from.X + 0.7 * dx, from.Y);
                var c2 = new Point(from.X + 0.3 * dx, from.Y + dy);
                ctx.BeginFigure(from, false);
                ctx.CubicBezierTo(c1, c2, to);
                ctx.EndFigure(false);
                startDir = Math.Atan2(c1.Y - from.Y, c1.X - from.X);
                endDir = Math.Atan2(to.Y - c2.Y, to.X - c2.X);
                break;
            }
            case "-~":
            {
                var c1 = new Point(from.X + 0.7 * dx, from.Y);
                ctx.BeginFigure(from, false);
                ctx.CubicBezierTo(c1, to, to);
                ctx.EndFigure(false);
                startDir = Math.Atan2(c1.Y - from.Y, c1.X - from.X);
                endDir = Math.Atan2(to.Y - to.Y, to.X - to.X);
                if (hasLabel) lx = from.X + (to.X - from.X) * 0.75;
                break;
            }
            case "~-":
            {
                var c2 = new Point(from.X + 0.3 * dx, from.Y + dy);
                ctx.BeginFigure(from, false);
                ctx.CubicBezierTo(from, c2, to);
                ctx.EndFigure(false);
                startDir = Math.Atan2(from.Y - from.Y, from.X - from.X);
                endDir = Math.Atan2(to.Y - c2.Y, to.X - c2.X);
                if (hasLabel) lx = from.X + (to.X - from.X) * 0.25;
                break;
            }
            case "-|":
                ctx.BeginFigure(from, false);
                ctx.LineTo(new Point(from.X + dx, from.Y));
                ctx.LineTo(new Point(from.X + dx, from.Y + dy));
                ctx.EndFigure(false);
                startDir = 0;
                endDir = dy >= 0 ? Math.PI / 2 : -Math.PI / 2;
                if (hasLabel) lx = to.X;
                break;
            case "|-":
                ctx.BeginFigure(from, false);
                ctx.LineTo(new Point(from.X, from.Y + dy));
                ctx.LineTo(new Point(from.X + dx, from.Y + dy));
                ctx.EndFigure(false);
                startDir = dy >= 0 ? Math.PI / 2 : -Math.PI / 2;
                endDir = dx >= 0 ? 0 : Math.PI;
                if (hasLabel) lx = from.X;
                break;
            case "-|-":
                ctx.BeginFigure(from, false);
                ctx.LineTo(new Point(from.X + dx / 2, from.Y));
                ctx.LineTo(new Point(from.X + dx / 2, from.Y + dy));
                ctx.LineTo(new Point(from.X + dx, from.Y + dy));
                ctx.EndFigure(false);
                startDir = 0;
                endDir = dx >= 0 ? 0 : Math.PI;
                break;
            case "+":
                ctx.BeginFigure(from, false);
                ctx.LineTo(to);
                ctx.EndFigure(false);
                break;
            default:
                ctx.BeginFigure(from, false);
                ctx.LineTo(to);
                ctx.EndFigure(false);
                break;
        }

        return new ArcResult(geometry, head, tail, tee, startDir, endDir, new Point(lx, ly));
    }

    /// <summary>Draws a filled arrowhead/arrowtail marker at <paramref name="tip"/>.</summary>
    private void DrawArrowMarker(DrawingContext dc, Point tip, double direction, bool forward)
    {
        var dir = forward ? direction : direction + Math.PI;
        const double size = 8;
        var spread = Math.PI / 9;
        var left = new Point(tip.X - size * Math.Cos(dir - spread), tip.Y - size * Math.Sin(dir - spread));
        var right = new Point(tip.X - size * Math.Cos(dir + spread), tip.Y - size * Math.Sin(dir + spread));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(left);
            ctx.LineTo(right);
            ctx.EndFigure(true);
        }

        dc.DrawGeometry(new SolidColorBrush(ArcColor), null, geometry);
    }

    /// <summary>
    /// Draws the SVG "tee" marker: a short stroke perpendicular to the edge
    /// direction, used at both ends of a <c>+</c> edge.
    /// </summary>
    private void DrawTeeMarker(DrawingContext dc, Point tip, double direction)
    {
        // Official skin tee marker: viewBox "0 0 1 6" -> ~6px long, thin stroke,
        // so the bar reads as a hairline (not a bold tick).
        const double half = 3;
        var perp = direction + Math.PI / 2;
        var a = new Point(tip.X - half * Math.Cos(perp), tip.Y - half * Math.Sin(perp));
        var b = new Point(tip.X + half * Math.Cos(perp), tip.Y + half * Math.Sin(perp));
        var pen = new Pen(new SolidColorBrush(ArcColor), 1);
        dc.DrawLine(pen, a, b);
    }

    /// <summary>Draws an edge/node label with a white background box (render-label.js).</summary>
    private void DrawArcLabel(DrawingContext dc, string text, Typeface typeface, Point center, double fontSize)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Foreground ?? Brushes.Black);

        var w = formatted.Width + 2;
        var rect = new Rect(center.X - w / 2, center.Y - fontSize / 2, w, fontSize);
        dc.DrawRectangle(Brushes.White, null, rect);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y + 0.3 * fontSize - formatted.Baseline));
    }

    /// <summary>
    /// Draws one half-brick (20x20 cell) identified by a WaveDrom skin id,
    /// using the official default skin path definitions (see
    /// <see cref="WaveDromSkin"/>). Each brick's local geometry is translated
    /// by the cell origin so the strokes match the official SVG output.
    /// </summary>
    private void DrawBrick(DrawingContext dc, string id, double x, double y)
    {
        if (!WaveDromSkin.Bricks.TryGetValue(id, out var parts))
        {
            // Unknown brick id: fall back to the official "xxx" hatch brick.
            parts = WaveDromSkin.Bricks["xxx"];
        }

        using var _ = dc.PushTransform(Matrix.CreateTranslation(x, y));
        foreach (var part in parts)
        {
            var geometry = GetCachedBrickGeometry(id, part.Data);
            var (brush, pen) = ResolveStyle(part.Class);
            dc.DrawGeometry(brush, pen, geometry);
        }
    }

    private StreamGeometry GetCachedBrickGeometry(string id, string pathData)
    {
        var key = id + "|" + pathData;
        if (_brickGeometryCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var geometry = SvgPathParser.Parse(pathData);
        _brickGeometryCache[key] = geometry;
        return geometry;
    }

    /// <summary>
    /// Resolves an official skin style class (s1..s16) to fill/stroke brushes
    /// and a pen. Strokes use the presenter <see cref="Foreground"/> so the
    /// diagram honors theming, while fills follow the skin color set.
    /// </summary>
    private (IBrush? Fill, IPen? Pen) ResolveStyle(string styleClass)
    {
        var stroke = Foreground ?? Brushes.Black;
        return styleClass switch
        {
            // s1: solid 1px stroke.
            "s1" => (null, CachedPen(stroke, 1, null)),
            // s2: thin 0.5px hatch stroke.
            "s2" => (null, CachedPen(stroke, 0.5, null)),
            // s3: dashed 1px stroke (dash 1,3).
            "s3" => (null, CachedPen(stroke, 1, new DashStyle([1, 3], 0))),
            // s4: solid 1px stroke.
            "s4" => (null, CachedPen(stroke, 1, null)),
            // s5: white fill (gap body).
            "s5" => (Brushes.White, null),
            // s6: black fill (clock edge arrow).
            "s6" => (stroke, null),
            // s7..s14: data box fills (index 2..9).
            "s7" => (Brushes.White, null),
            "s8" => (new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xB4)), null),
            "s9" => (new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xB9)), null),
            "s10" => (new SolidColorBrush(Color.FromRgb(0xB9, 0xE0, 0xFF)), null),
            "s11" => (new SolidColorBrush(Color.FromRgb(0xCC, 0xFD, 0xFE)), null),
            "s12" => (new SolidColorBrush(Color.FromRgb(0xCD, 0xFD, 0xC5)), null),
            "s13" => (new SolidColorBrush(Color.FromRgb(0xF0, 0xC1, 0xFB)), null),
            "s14" => (new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0xC0)), null),
            _ => (null, null)
        };
    }

    private IPen CachedPen(IBrush brush, double thickness, IDashStyle? dash)
    {
        var key = $"{brush}_{thickness}_{(dash is null ? "solid" : "dash")}";
        if (_brickPenCache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        pen = new Pen(brush, thickness, dash);
        _brickPenCache[key] = pen;
        return pen;
    }

    /// <summary>
    /// Draws a vertically rotated label matching the official render-groups.js:
    /// <c>g translate(x,y) / g rotate(270) / text text-anchor=middle</c>. The
    /// label is centered on <paramref name="x"/> along the y axis (reading
    /// bottom-to-top) with its baseline exactly on <paramref name="x"/>.
    /// </summary>
    private void DrawRotatedText(DrawingContext dc, string text, Typeface typeface, IBrush brush, double x, double centerY)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            brush);

        // SVG applies the inner rotate(270) first, then the outer translate,
        // so compose rotate * translate for Avalonia's apply-a-then-b order.
        var transform = Matrix.CreateRotation(Math.PI * 3 / 2) * Matrix.CreateTranslation(x, centerY);
        using var _ = dc.PushTransform(transform);
        // Baseline at (0,0) with text-anchor middle: center horizontally and
        // place the baseline at y=0.
        dc.DrawText(formatted, new Point(-formatted.Width / 2, -formatted.Baseline));
    }

    /// <summary>
    /// Draws text horizontally centered on <paramref name="centerX"/> with its baseline
    /// at <paramref name="baselineY"/>, matching SVG <c>text-anchor: middle</c> data labels.
    /// </summary>
    private void DrawMiddleAnchoredText(DrawingContext dc, string text, Typeface typeface, IBrush brush, double centerX, double baselineY)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            brush);
        dc.DrawText(formatted, new Point(centerX - formatted.Width / 2, baselineY - formatted.Baseline));
    }

    /// <summary>
    /// Draws a styled head/foot caption as a horizontally-centered run of
    /// spans, honoring per-tspan fill, class, size, decoration, and baseline
    /// shift (render-marks.js captext + tspan attributes).
    /// </summary>
    private void DrawTextSpans(DrawingContext dc, WaveDromTextSpan[] spans, Typeface typeface, double centerX, double baselineY)
    {
        if (spans.Length == 0)
        {
            return;
        }

        // Measure each span to compute the total run width for centering.
        // SVG keeps trailing spaces between tspans (xml:space="preserve"), but
        // FormattedText.Width drops trailing whitespace, so add the trailing
        // space width back so inter-span gaps are preserved.
        var formatted = new FormattedText?[spans.Length];
        var spanWidths = new double[spans.Length];
        var totalWidth = 0d;
        for (var i = 0; i < spans.Length; i++)
        {
            var span = spans[i];
            var (classSizePx, classBold) = ResolveClassFont(span.ClassName);
            var spanSize = span.FontSize ?? classSizePx ?? FontSize;
            var spanBold = span.Bold || classBold;
            var spanTypeface = BuildTypeface(typeface, span.Italic, spanBold);
            var spanBrush = ResolveSpanBrush(span);
            formatted[i] = new FormattedText(
                span.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                spanTypeface,
                spanSize,
                spanBrush);
            spanWidths[i] = MeasureWidthWithTrailingSpaces(formatted[i]!, span.Text, spanTypeface, spanSize);
            totalWidth += spanWidths[i];
        }

        // Lay spans out left-to-right from the centered origin.
        var x = centerX - totalWidth / 2;
        for (var i = 0; i < spans.Length; i++)
        {
            var span = spans[i];
            var ft = formatted[i]!;
            var baseline = baselineY - span.Dy;
            if (span.Superscript)
            {
                baseline -= FontSize * 0.4;
            }
            else if (span.Subscript)
            {
                baseline += FontSize * 0.2;
            }

            var drawY = baseline - ft.Baseline;
            dc.DrawText(ft, new Point(x, drawY));

            if (span.Overline)
            {
                dc.DrawLine(new Pen(ResolveSpanBrush(span), 1), new Point(x, drawY), new Point(x + ft.Width, drawY));
            }

            if (span.Underline)
            {
                dc.DrawLine(new Pen(ResolveSpanBrush(span), 1), new Point(x, drawY + ft.Height), new Point(x + ft.Width, drawY + ft.Height));
            }

            x += spanWidths[i];
        }
    }

    /// <summary>
    /// Returns the drawn width of a span including any trailing spaces, which
    /// FormattedText.Width excludes but SVG preserves between tspans.
    /// Uses a trailing sentinel glyph so the spaces are not trimmed.
    /// </summary>
    private static double MeasureWidthWithTrailingSpaces(
        FormattedText formatted, string text, Typeface typeface, double fontSize)
    {
        var width = formatted.Width;

        var trailing = 0;
        for (var i = text.Length - 1; i >= 0 && text[i] == ' '; i--)
        {
            trailing++;
        }

        if (trailing == 0)
        {
            return width;
        }

        // Measure "text|" and subtract "|" to recover the trailing spaces.
        const char sentinel = '|';
        var withSentinel = new FormattedText(
            text + sentinel,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);
        var sentinelOnly = new FormattedText(
            sentinel.ToString(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black);
        return withSentinel.Width - sentinelOnly.Width;
    }

    private static Typeface BuildTypeface(Typeface baseTypeface, bool italic, bool bold)
    {
        if (!bold && !italic)
        {
            return baseTypeface;
        }

        var style = italic ? FontStyle.Italic : baseTypeface.Style;
        var weight = bold ? FontWeight.Bold : baseTypeface.Weight;
        return new Typeface(baseTypeface.FontFamily, style, weight);
    }

    /// <summary>
    /// Maps official skin heading classes h1..h6 to a px font size (and bold),
    /// matching the default skin CSS (.h1 { font-size:33pt } ... .h6 { 8pt }).
    /// 1pt ~= 1.333px.
    /// </summary>
    private static (double? SizePx, bool Bold) ResolveClassFont(string? className)
    {
        if (className is not { Length: > 0 })
        {
            return (null, false);
        }

        foreach (var token in className.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var size = token switch
            {
                "h1" => 33.0 * 1.3333,
                "h2" => 27.0 * 1.3333,
                "h3" => 20.0 * 1.3333,
                "h4" => 14.0 * 1.3333,
                "h5" => 11.0 * 1.3333,
                "h6" => 8.0 * 1.3333,
                _ => (double?)null,
            };

            if (size is { } px)
            {
                return (px, true);
            }
        }

        return (null, false);
    }

    /// <summary>Resolves a span's foreground from its explicit fill or skin class.</summary>
    private static IBrush ResolveSpanBrush(WaveDromTextSpan span)
    {
        if (span.Fill is { Length: > 0 } fill)
        {
            try
            {
                return new SolidColorBrush(Color.Parse(fill));
            }
            catch
            {
                // Fall through to class/default resolution on parse failure.
            }
        }

        if (span.ClassName is { Length: > 0 } className)
        {
            var brush = ResolveClassBrush(className);
            if (brush is not null)
            {
                return brush;
            }
        }

        return TextBrush;
    }

    /// <summary>Maps official skin text classes (info/muted/warning/error/success) to brushes.</summary>
    private static IBrush? ResolveClassBrush(string className)
    {
        foreach (var token in className.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "muted":
                    return MutedBrush;
                case "info":
                    return InfoBrush;
                case "warning":
                    return new SolidColorBrush(Color.FromRgb(0xF6, 0xB9, 0x00));
                case "error":
                    return new SolidColorBrush(Color.FromRgb(0xF6, 0x00, 0x00));
                case "success":
                    return new SolidColorBrush(Color.FromRgb(0x00, 0xAB, 0x00));
            }
        }

        return null;
    }

    private void DrawRightAlignedText(DrawingContext dc, string text, Typeface typeface, IBrush brush, double right, double y)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            brush);
        dc.DrawText(formatted, new Point(right - formatted.Width, y));
    }

    private void DrawCenteredText(DrawingContext dc, string text, Typeface typeface, IBrush brush, double x, double y, double width, double height)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            brush);
        dc.DrawText(formatted, new Point(x + Math.Max(0, (width - formatted.Width) / 2), y + Math.Max(0, (height - formatted.Height) / 2)));
    }
}
