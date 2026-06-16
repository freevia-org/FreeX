namespace Free.Shared.Ribbon.Icons;

/// <summary>
/// Platform-neutral description of a single ribbon icon. All coordinates live on a
/// <see cref="Artboard"/>-by-<see cref="Artboard"/> square (matching the WPF source-of-truth artboard),
/// so a renderer simply needs to scale the artboard into the target pixel box.
/// </summary>
/// <remarks>
/// This is the single shared source of truth for icon shapes. Both the WPF
/// (<c>RibbonIconFactory</c>) and Avalonia (<c>AvaloniaRibbonIcons</c>) renderers build their
/// native visuals from these definitions, so the two platforms draw the same icons.
/// </remarks>
public sealed record RibbonIconGeometry(
    RibbonCommandIconKind Kind,
    IReadOnlyList<RibbonIconElement> Elements)
{
    /// <summary>The square design surface every element is positioned on.</summary>
    public const double Artboard = 24;
}

/// <summary>The kind of a single drawable element within an icon.</summary>
public enum RibbonIconElementKind
{
    /// <summary>A stroked (and optionally filled) SVG path, geometry in <see cref="RibbonIconElement.PathData"/>.</summary>
    Path,

    /// <summary>A straight line between (X1,Y1) and (X2,Y2).</summary>
    Line,

    /// <summary>A stroked rectangle (X,Y,Width,Height) with optional corner <see cref="RibbonIconElement.Radius"/>.</summary>
    Rectangle,

    /// <summary>A solid-filled rectangle (X,Y,Width,Height).</summary>
    FilledRectangle,

    /// <summary>A stroked ellipse fitting the box (X,Y,Width,Height).</summary>
    Ellipse,

    /// <summary>A solid-filled circle centred at (X1,Y1) with diameter <see cref="RibbonIconElement.Width"/>.</summary>
    FilledCircle,

    /// <summary>A run of text positioned within the artboard, see the text-specific properties.</summary>
    Text,
}

/// <summary>The weight a text element should be drawn with (platform-neutral).</summary>
public enum RibbonIconTextWeight
{
    Normal,
    SemiBold,
    Bold,
}

/// <summary>
/// A single drawable element of an icon. The interpretation of the numeric fields depends on
/// <see cref="Kind"/>; see the <see cref="RibbonIconElementKind"/> members. Helper factory methods
/// mirror the WPF primitive helpers so the geometry transcribes 1:1 from the source-of-truth drawings.
/// </summary>
public sealed record RibbonIconElement
{
    public RibbonIconElementKind Kind { get; init; }

    /// <summary>SVG path string (artboard coordinates). Only set for <see cref="RibbonIconElementKind.Path"/>.</summary>
    public string? PathData { get; init; }

    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }

    /// <summary>Box width / circle diameter / text font size, depending on kind.</summary>
    public double Width { get; init; }

    /// <summary>Box height.</summary>
    public double Height { get; init; }

    /// <summary>Stroke thickness for stroked elements.</summary>
    public double StrokeThickness { get; init; } = 1.5;

    /// <summary>Corner radius for rectangles.</summary>
    public double Radius { get; init; }

    /// <summary>True when the element's interior is dash-stroked (lines only).</summary>
    public bool Dashed { get; init; }

    /// <summary>When &gt; 0 the path is filled with the glyph color at this opacity (in addition to being stroked).</summary>
    public double FillOpacity { get; init; }

    // Text-only fields.
    public string? Text { get; init; }
    public RibbonIconTextWeight TextWeight { get; init; } = RibbonIconTextWeight.Normal;

    // ---- Factory helpers mirroring the WPF primitive helpers ----

    public static RibbonIconElement Path(string data, double thickness = 1.5, double fillOpacity = 0) =>
        new() { Kind = RibbonIconElementKind.Path, PathData = data, StrokeThickness = thickness, FillOpacity = fillOpacity };

    /// <summary>A filled-and-stroked shape path (matches WPF <c>DrawShapePath</c>: 1.5 stroke, 0.08 fill).</summary>
    public static RibbonIconElement ShapePath(string data) =>
        new() { Kind = RibbonIconElementKind.Path, PathData = data, StrokeThickness = 1.5, FillOpacity = 0.08 };

    public static RibbonIconElement Line(double x1, double y1, double x2, double y2, double thickness = 1.5, bool dashed = false) =>
        new() { Kind = RibbonIconElementKind.Line, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = thickness, Dashed = dashed };

    public static RibbonIconElement Rectangle(double x, double y, double width, double height, double radius = 0) =>
        new() { Kind = RibbonIconElementKind.Rectangle, X1 = x, Y1 = y, Width = width, Height = height, Radius = radius, StrokeThickness = 1.5 };

    public static RibbonIconElement FilledRectangle(double x, double y, double width, double height) =>
        new() { Kind = RibbonIconElementKind.FilledRectangle, X1 = x, Y1 = y, Width = width, Height = height };

    public static RibbonIconElement Ellipse(double x, double y, double width, double height, double thickness = 1.5) =>
        new() { Kind = RibbonIconElementKind.Ellipse, X1 = x, Y1 = y, Width = width, Height = height, StrokeThickness = thickness };

    /// <summary>A solid circle centred at (cx,cy) with the given diameter.</summary>
    public static RibbonIconElement FilledCircle(double cx, double cy, double diameter) =>
        new() { Kind = RibbonIconElementKind.FilledCircle, X1 = cx, Y1 = cy, Width = diameter };

    public static RibbonIconElement TextRun(string text, double fontSize, RibbonIconTextWeight weight, double x = 0, double y = 0) =>
        new() { Kind = RibbonIconElementKind.Text, Text = text, Width = fontSize, TextWeight = weight, X1 = x, Y1 = y };
}
