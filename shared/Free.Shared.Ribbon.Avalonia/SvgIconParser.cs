using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// A minimal, software-safe SVG → Avalonia translator. It parses the per-command ribbon SVGs (the SAME
/// 299 files the WPF host loads, shared via FreeX.Ribbon.Definitions) into a native Avalonia
/// <see cref="DrawingGroup"/>, built entirely from <see cref="Avalonia.Media"/> primitives
/// (<see cref="Geometry"/>, <see cref="GeometryDrawing"/>, <see cref="DrawingGroup"/>, <see cref="Pen"/>,
/// <see cref="SolidColorBrush"/>, <see cref="Matrix"/>). These render through Avalonia 12's own drawing
/// pipeline — the very same pipeline the existing kind glyphs use — so they are safe under software /
/// Xvfb rendering. Crucially this avoids any external SVG library (e.g. Avalonia.Svg.Skia) that would
/// pull a second, mismatched Avalonia version into the graph and black-screen the ribbon.
/// </summary>
/// <remarks>
/// <para>Supported element set (verified against the 299 shipped icons):</para>
/// <list type="bullet">
/// <item><c>&lt;svg viewBox&gt;</c> — establishes the source coordinate space; the whole drawing is
/// scaled to the requested size by the caller (icon Image sized to <c>size</c> + Uniform stretch).</item>
/// <item><c>&lt;rect x y width height rx ry fill stroke stroke-width&gt;</c> — RectangleGeometry, rounded
/// when rx/ry present.</item>
/// <item><c>&lt;path d ...&gt;</c> — <see cref="Geometry.Parse"/> over the SVG path mini-language.</item>
/// <item><c>&lt;circle cx cy r&gt;</c> / <c>&lt;ellipse cx cy rx ry&gt;</c> — EllipseGeometry.</item>
/// <item><c>&lt;polygon points&gt;</c> — a closed <see cref="PolylineGeometry"/>.</item>
/// <item><c>&lt;g transform fill stroke ...&gt;</c> — a nested DrawingGroup whose transform is applied
/// and whose paint attributes are inherited by descendants.</item>
/// <item><c>&lt;text x y font-size font-weight text-anchor dominant-baseline fill&gt;</c> — rendered as a
/// geometry via <see cref="FormattedText.BuildGeometry"/> (text-anchor=middle and dominant-baseline=central
/// honored).</item>
/// </list>
/// <para>Paint: <c>fill="none"</c> → no fill; a stroke becomes a <see cref="Pen"/> (thickness +
/// <see cref="PenLineCap"/>/<see cref="PenLineJoin"/> from stroke-linecap/linejoin). Colors parse
/// <c>#rrggbb</c>, <c>#rgb</c>, <c>none</c>, and named black/white. Simple
/// <c>&lt;linearGradient&gt;</c> definitions resolve to native Avalonia gradient brushes. Other
/// <c>&lt;defs&gt;</c> content is skipped.</para>
/// </remarks>
internal static class SvgIconParser
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Letter-glyph font. The icon SVGs author text in "Segoe UI"; Avalonia resolves the first available
    /// family in this comma-separated chain, so Windows uses Segoe UI while Linux/macOS fall back to the
    /// closest neutral sans, keeping the glyph faithful.
    /// </summary>
    private static readonly FontFamily GlyphFontFamily =
        new("Segoe UI, Liberation Sans, DejaVu Sans, Arial, sans-serif");

    /// <summary>
    /// Parses the SVG at <paramref name="filePath"/> into an Avalonia <see cref="DrawingImage"/>, or
    /// returns <see langword="null"/> when the file is unreadable / contains nothing renderable. The image
    /// is authored in the SVG's own viewBox coordinate space; the caller scales it to the wanted pixel
    /// size via an <see cref="Avalonia.Controls.Image"/> with <see cref="Stretch.Uniform"/>.
    /// </summary>
    public static DrawingImage? TryParseFile(string filePath) =>
        TryParseFile(filePath, monochromeBrush: null);

    public static DrawingImage? TryParseFile(string filePath, IBrush? monochromeBrush)
        => TryParseFile(filePath, monochromeBrush, includeViewBoxBounds: true);

    internal static DrawingImage? TryParseFile(
        string filePath,
        IBrush? monochromeBrush,
        bool includeViewBoxBounds)
    {
        try
        {
            var doc = XDocument.Load(filePath);
            var root = doc.Root;
            if (root is null)
                return null;

            var viewBox = ReadViewBox(root);

            var group = new DrawingGroup();
            var inherited = SvgPaint.CreateRoot(monochromeBrush);
            foreach (var child in root.Elements())
                ParseElement(child, group, inherited);

            if (group.Children.Count == 0)
                return null;

            // A transparent backing rect over the full viewBox pins the drawing's bounds to the design
            // square so Uniform stretch scales consistently regardless of the painted content extent.
            if (includeViewBoxBounds && viewBox is { } box)
            {
                var backing = new GeometryDrawing
                {
                    Brush = Brushes.Transparent,
                    Geometry = new RectangleGeometry(box),
                };
                group.Children.Insert(0, backing);
            }

            return new DrawingImage { Drawing = group };
        }
        catch
        {
            return null;
        }
    }

    private static void ParseElement(XElement element, DrawingGroup parent, SvgPaint inherited)
    {
        var name = element.Name.LocalName;
        switch (name)
        {
            case "g":
                ParseGroup(element, parent, inherited);
                break;
            case "rect":
                AddGeometry(parent, BuildRect(element), element, inherited);
                break;
            case "path":
                AddPath(parent, element, inherited);
                break;
            case "circle":
                AddGeometry(parent, BuildCircle(element), element, inherited);
                break;
            case "ellipse":
                AddGeometry(parent, BuildEllipse(element), element, inherited);
                break;
            case "polygon":
                AddGeometry(parent, BuildPolygon(element), element, inherited);
                break;
            case "line":
                AddGeometry(parent, BuildLine(element), element, inherited);
                break;
            case "text":
                AddText(parent, element, inherited);
                break;
            case "defs":
            case "linearGradient":
            case "radialGradient":
            case "title":
            case "desc":
                // Definitions / metadata: not drawn directly. Gradients are resolved on use via the
                // gradient lookup when a fill="url(#id)" is encountered.
                break;
            default:
                // Unknown wrapper: still recurse so nested drawables are not lost.
                if (element.HasElements)
                    foreach (var child in element.Elements())
                        ParseElement(child, parent, inherited);
                break;
        }
    }

    private static void ParseGroup(XElement element, DrawingGroup parent, SvgPaint inherited)
    {
        var groupPaint = inherited.Inherit(element);
        var child = new DrawingGroup();

        var transform = ParseTransform(element.Attribute("transform")?.Value);
        if (transform is { } m)
            child.Transform = new MatrixTransform(m);

        var opacity = ParseDouble(element.Attribute("opacity")?.Value);
        if (opacity is { } o && o < 1.0)
            child.Opacity = o;

        foreach (var node in element.Elements())
            ParseElement(node, child, groupPaint);

        if (child.Children.Count > 0)
            parent.Children.Add(child);
    }

    private static void AddGeometry(DrawingGroup parent, Geometry? geometry, XElement element, SvgPaint inherited)
    {
        if (geometry is null)
            return;

        var paint = inherited.Inherit(element);
        var drawing = BuildGeometryDrawing(geometry, paint, element);
        if (drawing is null)
            return;

        WrapAndAdd(parent, drawing, element);
    }

    private static void AddPath(DrawingGroup parent, XElement element, SvgPaint inherited)
    {
        var d = element.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(d))
            return;

        Geometry geometry;
        try
        {
            geometry = Geometry.Parse(d);
        }
        catch
        {
            return;
        }

        var paint = inherited.Inherit(element);
        if (geometry is PathGeometry pg && paint.FillRule is { } rule)
            pg.FillRule = rule;

        var drawing = BuildGeometryDrawing(geometry, paint, element);
        if (drawing is null)
            return;

        WrapAndAdd(parent, drawing, element);
    }

    private static void AddText(DrawingGroup parent, XElement element, SvgPaint inherited)
    {
        var text = element.Value;
        if (string.IsNullOrEmpty(text))
            return;

        var paint = inherited.Inherit(element);
        var fontSize = ParseDouble(element.Attribute("font-size")?.Value) ?? 12.0;
        var weight = ParseFontWeight(element.Attribute("font-weight")?.Value);
        var x = ParseDouble(element.Attribute("x")?.Value) ?? 0;
        var y = ParseDouble(element.Attribute("y")?.Value) ?? 0;
        var anchor = element.Attribute("text-anchor")?.Value;
        var baseline = element.Attribute("dominant-baseline")?.Value;

        var fillBrush = paint.Fill ?? Brushes.Black;

        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(GlyphFontFamily, FontStyle.Normal, weight),
            fontSize,
            fillBrush);

        // SVG (x,y) is the anchor point on the text baseline unless dominant-baseline asks for a visual
        // center. Match the WPF SVG loader here so shared text-heavy icons (Bold/Underline/etc.) do not
        // sit low in their button slots on Linux.
        var originX = x;
        if (string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase))
            originX -= formatted.Width / 2;
        else if (string.Equals(anchor, "end", StringComparison.OrdinalIgnoreCase))
            originX -= formatted.Width;

        var originY = y - formatted.Baseline;
        if (string.Equals(baseline, "central", StringComparison.OrdinalIgnoreCase)
            || string.Equals(baseline, "middle", StringComparison.OrdinalIgnoreCase))
        {
            originY = y - formatted.Height / 2;
        }

        var geometry = formatted.BuildGeometry(new Point(originX, originY));
        if (geometry is null)
            return;

        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Brush = fillBrush,
        };
        if (paint.Stroke is { } pen)
            drawing.Pen = pen;

        WrapAndAdd(parent, drawing, element);
    }

    // Wraps a single drawing in a transform/opacity group when those attributes are present on the
    // element, then appends it to the parent.
    private static void WrapAndAdd(DrawingGroup parent, GeometryDrawing drawing, XElement element)
    {
        var transform = ParseTransform(element.Attribute("transform")?.Value);
        var opacity = ParseDouble(element.Attribute("opacity")?.Value);

        if (transform is null && (opacity is null || opacity >= 1.0))
        {
            parent.Children.Add(drawing);
            return;
        }

        var wrapper = new DrawingGroup();
        if (transform is { } m)
            wrapper.Transform = new MatrixTransform(m);
        if (opacity is { } o && o < 1.0)
            wrapper.Opacity = o;
        wrapper.Children.Add(drawing);
        parent.Children.Add(wrapper);
    }

    private static GeometryDrawing? BuildGeometryDrawing(Geometry geometry, SvgPaint paint, XElement element)
    {
        var hasFill = paint.Fill is not null;
        var hasStroke = paint.Stroke is not null;
        if (!hasFill && !hasStroke)
            return null;

        return new GeometryDrawing
        {
            Geometry = geometry,
            Brush = paint.Fill,
            Pen = paint.Stroke,
        };
    }

    // ── Geometry builders ───────────────────────────────────────────────────────────────────────

    private static Geometry? BuildRect(XElement element)
    {
        var w = ParseDouble(element.Attribute("width")?.Value) ?? 0;
        var h = ParseDouble(element.Attribute("height")?.Value) ?? 0;
        if (w <= 0 || h <= 0)
            return null;

        var x = ParseDouble(element.Attribute("x")?.Value) ?? 0;
        var y = ParseDouble(element.Attribute("y")?.Value) ?? 0;
        var rx = ParseDouble(element.Attribute("rx")?.Value);
        var ry = ParseDouble(element.Attribute("ry")?.Value);
        var radiusX = rx ?? ry ?? 0;
        var radiusY = ry ?? rx ?? 0;

        var rect = new Rect(x, y, w, h);
        return radiusX > 0 || radiusY > 0
            ? new RectangleGeometry(rect, radiusX, radiusY)
            : new RectangleGeometry(rect);
    }

    private static Geometry? BuildCircle(XElement element)
    {
        var r = ParseDouble(element.Attribute("r")?.Value) ?? 0;
        if (r <= 0)
            return null;
        var cx = ParseDouble(element.Attribute("cx")?.Value) ?? 0;
        var cy = ParseDouble(element.Attribute("cy")?.Value) ?? 0;
        return new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2));
    }

    private static Geometry? BuildEllipse(XElement element)
    {
        var rx = ParseDouble(element.Attribute("rx")?.Value) ?? 0;
        var ry = ParseDouble(element.Attribute("ry")?.Value) ?? 0;
        if (rx <= 0 || ry <= 0)
            return null;
        var cx = ParseDouble(element.Attribute("cx")?.Value) ?? 0;
        var cy = ParseDouble(element.Attribute("cy")?.Value) ?? 0;
        return new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));
    }

    private static Geometry? BuildPolygon(XElement element)
    {
        var raw = element.Attribute("points")?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var nums = SplitNumbers(raw);
        if (nums.Count < 4)
            return null;

        var points = new List<Point>(nums.Count / 2);
        for (var i = 0; i + 1 < nums.Count; i += 2)
            points.Add(new Point(nums[i], nums[i + 1]));

        return new PolylineGeometry(points, isFilled: true);
    }

    private static Geometry BuildLine(XElement element)
    {
        var x1 = ParseDouble(element.Attribute("x1")?.Value) ?? 0;
        var y1 = ParseDouble(element.Attribute("y1")?.Value) ?? 0;
        var x2 = ParseDouble(element.Attribute("x2")?.Value) ?? 0;
        var y2 = ParseDouble(element.Attribute("y2")?.Value) ?? 0;
        return new LineGeometry(new Point(x1, y1), new Point(x2, y2));
    }

    // ── Transform parsing ───────────────────────────────────────────────────────────────────────

    // Handles the transform forms the icons actually use: rotate(a [cx cy]), translate(tx [ty]),
    // scale(s [sy]), and whitespace-separated chains thereof (applied left-to-right).
    private static Matrix? ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var result = Matrix.Identity;
        var any = false;
        var i = 0;
        while (i < value.Length)
        {
            var open = value.IndexOf('(', i);
            if (open < 0)
                break;
            var name = value[i..open].Trim().TrimStart(',').Trim();
            var close = value.IndexOf(')', open);
            if (close < 0)
                break;
            var args = SplitNumbers(value[(open + 1)..close]);
            i = close + 1;

            Matrix? op = name switch
            {
                "rotate" => BuildRotate(args),
                "translate" => args.Count >= 1
                    ? Matrix.CreateTranslation(args[0], args.Count >= 2 ? args[1] : 0)
                    : null,
                "scale" => args.Count >= 1
                    ? Matrix.CreateScale(args[0], args.Count >= 2 ? args[1] : args[0])
                    : null,
                "matrix" => args.Count == 6
                    ? new Matrix(args[0], args[1], args[2], args[3], args[4], args[5])
                    : null,
                _ => null,
            };

            if (op is { } m)
            {
                // SVG applies the leftmost transform outermost: result = result * op.
                result = m * result;
                any = true;
            }
        }

        return any ? result : null;
    }

    private static Matrix? BuildRotate(IReadOnlyList<double> args)
    {
        if (args.Count == 0)
            return null;
        var angle = args[0];
        if (args.Count >= 3)
        {
            var cx = args[1];
            var cy = args[2];
            return Matrix.CreateTranslation(-cx, -cy)
                * Matrix.CreateRotation(angle * Math.PI / 180.0)
                * Matrix.CreateTranslation(cx, cy);
        }

        return Matrix.CreateRotation(angle * Math.PI / 180.0);
    }

    // ── Primitives ──────────────────────────────────────────────────────────────────────────────

    private static Rect? ReadViewBox(XElement root)
    {
        var viewBox = root.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            var parts = SplitNumbers(viewBox);
            if (parts.Count == 4)
                return new Rect(parts[0], parts[1], parts[2], parts[3]);
        }

        var w = ParseDouble(root.Attribute("width")?.Value);
        var h = ParseDouble(root.Attribute("height")?.Value);
        if (w is > 0 && h is > 0)
            return new Rect(0, 0, w.Value, h.Value);
        return null;
    }

    private static List<double> SplitNumbers(string text)
    {
        var list = new List<double>();
        var token = new System.Text.StringBuilder();

        void Flush()
        {
            if (token.Length == 0)
                return;
            if (double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                list.Add(n);
            token.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is ' ' or ',' or '\t' or '\n' or '\r')
            {
                Flush();
            }
            else if (ch is '-' && token.Length > 0 && token[^1] is not 'e' and not 'E')
            {
                // A '-' that is not an exponent sign starts a new number.
                Flush();
                token.Append(ch);
            }
            else
            {
                token.Append(ch);
            }
        }

        Flush();
        return list;
    }

    private static double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
    }

    private static FontWeight ParseFontWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FontWeight.Normal;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            return (FontWeight)numeric;
        return value.Trim().ToLowerInvariant() switch
        {
            "bold" => FontWeight.Bold,
            "semibold" => FontWeight.SemiBold,
            "normal" => FontWeight.Normal,
            _ => FontWeight.Normal,
        };
    }

    // ── Paint state (inherited fill/stroke down the element tree) ────────────────────────────────

    /// <summary>
    /// Resolved paint state at a point in the SVG tree. <see cref="Fill"/>/<see cref="Stroke"/> are the
    /// concrete brush/pen to apply (null = none). Inherited from the parent and overridden by an
    /// element's own presentation attributes via <see cref="Inherit"/>.
    /// </summary>
    private readonly struct SvgPaint
    {
        public IBrush? Fill { get; private init; }
        public Pen? Stroke { get; private init; }
        public FillRule? FillRule { get; private init; }

        // Stroke is built lazily from these because stroke paint, width and caps can be set at different
        // levels of the tree (e.g. <g stroke-width> with per-path stroke).
        private readonly IBrush? _strokeBrush;
        private readonly double _strokeWidth;
        private readonly PenLineCap _lineCap;
        private readonly PenLineJoin _lineJoin;
        private readonly IBrush? _monochromeBrush;

        private SvgPaint(
            IBrush? fill,
            IBrush? strokeBrush,
            double strokeWidth,
            PenLineCap lineCap,
            PenLineJoin lineJoin,
            FillRule? fillRule,
            IBrush? monochromeBrush)
        {
            Fill = fill;
            _strokeBrush = strokeBrush;
            _strokeWidth = strokeWidth;
            _lineCap = lineCap;
            _lineJoin = lineJoin;
            FillRule = fillRule;
            _monochromeBrush = monochromeBrush;
            Stroke = strokeBrush is { } brush && strokeWidth > 0
                ? new Pen(brush, strokeWidth) { LineCap = lineCap, LineJoin = lineJoin }
                : null;
        }

        // The SVG default: black fill, no stroke, 1px stroke width, butt caps / miter joins.
        public static SvgPaint CreateRoot(IBrush? monochromeBrush) => new(
            fill: monochromeBrush ?? Brushes.Black,
            strokeBrush: null,
            strokeWidth: 1.0,
            lineCap: PenLineCap.Flat,
            lineJoin: PenLineJoin.Miter,
            fillRule: null,
            monochromeBrush: monochromeBrush);

        /// <summary>Returns a new paint state with this element's presentation attributes applied over self.</summary>
        public SvgPaint Inherit(XElement element)
        {
            var fill = Fill;
            if (element.Attribute("fill")?.Value is { } fillRaw)
                fill = ParseBrush(fillRaw, element);

            var strokeBrush = _strokeBrush;
            if (element.Attribute("stroke")?.Value is { } strokeRaw)
                strokeBrush = ParseBrush(strokeRaw, element);

            var strokeWidth = _strokeWidth;
            if (ParseDouble(element.Attribute("stroke-width")?.Value) is { } sw)
                strokeWidth = sw;

            var lineCap = _lineCap;
            if (element.Attribute("stroke-linecap")?.Value is { } cap)
                lineCap = ParseLineCap(cap);

            var lineJoin = _lineJoin;
            if (element.Attribute("stroke-linejoin")?.Value is { } join)
                lineJoin = ParseLineJoin(join);

            var fillRule = FillRule;
            if (element.Attribute("fill-rule")?.Value is { } fr)
                fillRule = string.Equals(fr.Trim(), "evenodd", StringComparison.OrdinalIgnoreCase)
                    ? global::Avalonia.Media.FillRule.EvenOdd
                    : global::Avalonia.Media.FillRule.NonZero;

            return new SvgPaint(fill, strokeBrush, strokeWidth, lineCap, lineJoin, fillRule, _monochromeBrush);
        }

        private IBrush? ParseBrush(string raw, XElement element)
        {
            var value = raw.Trim();
            if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;
            if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return Brushes.Transparent;
            if (_monochromeBrush is { } monochrome)
                return monochrome;
            if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                return ResolveGradientBrush(value, element);
            return ParseColor(value) is { } c ? new SolidColorBrush(c) : null;
        }

        private static IBrush? ResolveGradientBrush(string urlRef, XElement element)
        {
            var id = urlRef.Trim();
            var hash = id.IndexOf('#');
            var rp = id.IndexOf(')');
            if (hash < 0 || rp < 0 || rp <= hash)
                return Brushes.Gray;
            id = id[(hash + 1)..rp].Trim();

            var root = element.AncestorsAndSelf().Last();
            foreach (var grad in root.Descendants())
            {
                if (grad.Name.LocalName is not ("linearGradient" or "radialGradient"))
                    continue;
                if (grad.Attribute("id")?.Value != id)
                    continue;

                if (grad.Name.LocalName == "linearGradient" &&
                    TryBuildLinearGradientBrush(grad, root) is { } brush)
                {
                    return brush;
                }

                foreach (var stop in grad.Elements())
                {
                    if (stop.Name.LocalName != "stop")
                        continue;
                    if (stop.Attribute("stop-color")?.Value is { } sc && ParseColor(sc) is { } c)
                        return new SolidColorBrush(c);
                }
            }

            return Brushes.Gray;
        }

        private static LinearGradientBrush? TryBuildLinearGradientBrush(XElement gradient, XElement root)
        {
            var stops = new List<GradientStop>();
            foreach (var stop in gradient.Elements())
            {
                if (stop.Name.LocalName != "stop")
                    continue;
                if (stop.Attribute("stop-color")?.Value is not { } rawColor || ParseColor(rawColor) is not { } color)
                    continue;

                stops.Add(new GradientStop(color, ParseGradientOffset(stop.Attribute("offset")?.Value)));
            }

            if (stops.Count == 0)
                return null;

            var usesUserSpace = string.Equals(
                gradient.Attribute("gradientUnits")?.Value,
                "userSpaceOnUse",
                StringComparison.OrdinalIgnoreCase);
            var viewBox = ReadViewBox(root) ?? new Rect(0, 0, 1, 1);
            var x1 = ParseDouble(gradient.Attribute("x1")?.Value) ?? 0;
            var y1 = ParseDouble(gradient.Attribute("y1")?.Value) ?? 0;
            var x2 = ParseDouble(gradient.Attribute("x2")?.Value) ?? 1;
            var y2 = ParseDouble(gradient.Attribute("y2")?.Value) ?? 0;

            var brush = new LinearGradientBrush
            {
                StartPoint = ToGradientPoint(x1, y1, viewBox, usesUserSpace),
                EndPoint = ToGradientPoint(x2, y2, viewBox, usesUserSpace),
            };
            foreach (var stop in stops.OrderBy(stop => stop.Offset))
                brush.GradientStops.Add(stop);
            return brush;
        }

        private static RelativePoint ToGradientPoint(double x, double y, Rect viewBox, bool usesUserSpace)
        {
            if (!usesUserSpace)
                return new RelativePoint(x, y, RelativeUnit.Relative);

            var relativeX = viewBox.Width > 0 ? (x - viewBox.X) / viewBox.Width : 0;
            var relativeY = viewBox.Height > 0 ? (y - viewBox.Y) / viewBox.Height : 0;
            return new RelativePoint(relativeX, relativeY, RelativeUnit.Relative);
        }

        private static double ParseGradientOffset(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            var value = raw.Trim();
            if (value.EndsWith("%", StringComparison.Ordinal) &&
                double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                return Math.Clamp(percent / 100d, 0d, 1d);
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)
                ? Math.Clamp(offset, 0d, 1d)
                : 0d;
        }
    }

    private static PenLineCap ParseLineCap(string value) => value.Trim().ToLowerInvariant() switch
    {
        "round" => PenLineCap.Round,
        "square" => PenLineCap.Square,
        _ => PenLineCap.Flat,
    };

    private static PenLineJoin ParseLineJoin(string value) => value.Trim().ToLowerInvariant() switch
    {
        "round" => PenLineJoin.Round,
        "bevel" => PenLineJoin.Bevel,
        _ => PenLineJoin.Miter,
    };

    // Parses #rrggbb, #rgb, none, transparent, and named black/white. Returns null for "none".
    private static Color? ParseColor(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return Colors.Transparent;

        if (value[0] == '#')
        {
            var hex = value[1..];
            if (hex.Length == 3)
            {
                var r = Convert.ToByte($"{hex[0]}{hex[0]}", 16);
                var g = Convert.ToByte($"{hex[1]}{hex[1]}", 16);
                var b = Convert.ToByte($"{hex[2]}{hex[2]}", 16);
                return Color.FromRgb(r, g, b);
            }

            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromRgb(r, g, b);
            }

            if (hex.Length == 8)
            {
                var a = Convert.ToByte(hex.Substring(0, 2), 16);
                var r = Convert.ToByte(hex.Substring(2, 2), 16);
                var g = Convert.ToByte(hex.Substring(4, 2), 16);
                var b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }

            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "black" => Colors.Black,
            "white" => Colors.White,
            _ => Color.TryParse(value, out var named) ? named : null,
        };
    }
}
