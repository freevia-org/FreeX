namespace FreeX.App.Presentation.QuickAnalysis;

public enum QuickAnalysisPreviewIconGlyph
{
    EmptyGrid,
    HorizontalBars,
    ColorScale,
    IconSet,
    HighlightGrid,
    ClearFormat,
    VerticalBars,
    StackedVerticalBars,
    LineChart,
    Pie,
    Area,
    Scatter,
    Formula,
    Table,
    WinLoss
}

public sealed record QuickAnalysisPreviewIconPlan(
    QuickAnalysisPreviewIconGlyph Glyph,
    double Width,
    double Height,
    IReadOnlyList<QuickAnalysisPreviewIconElement> Elements);

public abstract record QuickAnalysisPreviewIconElement;

public sealed record QuickAnalysisPreviewIconRectangle(
    double Left,
    double Top,
    double Width,
    double Height,
    QuickAnalysisPreviewIconColor Fill,
    QuickAnalysisPreviewIconColor? Stroke = null,
    double StrokeThickness = 0) : QuickAnalysisPreviewIconElement;

public sealed record QuickAnalysisPreviewIconEllipse(
    double Left,
    double Top,
    double Size,
    QuickAnalysisPreviewIconColor Fill) : QuickAnalysisPreviewIconElement;

public sealed record QuickAnalysisPreviewIconLine(
    double X1,
    double Y1,
    double X2,
    double Y2,
    QuickAnalysisPreviewIconColor Stroke,
    double StrokeThickness) : QuickAnalysisPreviewIconElement;

public sealed record QuickAnalysisPreviewIconPolygon(
    IReadOnlyList<QuickAnalysisPreviewIconPoint> Points,
    QuickAnalysisPreviewIconColor Fill) : QuickAnalysisPreviewIconElement;

public sealed record QuickAnalysisPreviewIconText(
    string Text,
    double Left,
    double Top,
    double FontSize,
    QuickAnalysisPreviewIconFontWeight FontWeight,
    QuickAnalysisPreviewIconColor Foreground) : QuickAnalysisPreviewIconElement;

public enum QuickAnalysisPreviewIconFontWeight
{
    Normal,
    SemiBold
}

public readonly record struct QuickAnalysisPreviewIconPoint(double X, double Y);

public readonly record struct QuickAnalysisPreviewIconColor(byte A, byte R, byte G, byte B)
{
    public static QuickAnalysisPreviewIconColor FromRgb(byte r, byte g, byte b) =>
        new(255, r, g, b);

    public static QuickAnalysisPreviewIconColor FromArgb(byte a, byte r, byte g, byte b) =>
        new(a, r, g, b);

    public static QuickAnalysisPreviewIconColor Firebrick => FromRgb(178, 34, 34);
    public static QuickAnalysisPreviewIconColor Goldenrod => FromRgb(218, 165, 32);
    public static QuickAnalysisPreviewIconColor LightGoldenrodYellow => FromRgb(250, 250, 210);
    public static QuickAnalysisPreviewIconColor LightGray => FromRgb(211, 211, 211);
    public static QuickAnalysisPreviewIconColor SeaGreen => FromRgb(46, 139, 87);
    public static QuickAnalysisPreviewIconColor SteelBlue => FromRgb(70, 130, 180);
    public static QuickAnalysisPreviewIconColor White => FromRgb(255, 255, 255);
}

/// <summary>
/// Shared Quick Analysis menu-icon shape planning. Renderers still draw native controls and brushes;
/// this keeps preview visual grouping, geometry, and color descriptors out of platform glue.
/// </summary>
public static class QuickAnalysisPreviewIconPlanner
{
    private const double DefaultWidth = 34;
    private const double DefaultHeight = 22;

    public static QuickAnalysisPreviewIconPlan Plan(QuickAnalysisPreviewVisual visual) =>
        Plan(visual.Kind);

    public static QuickAnalysisPreviewIconPlan Plan(QuickAnalysisPreviewVisualKind kind) =>
        PlanGlyph(kind switch
        {
            QuickAnalysisPreviewVisualKind.DataBars => QuickAnalysisPreviewIconGlyph.HorizontalBars,
            QuickAnalysisPreviewVisualKind.ColorScale => QuickAnalysisPreviewIconGlyph.ColorScale,
            QuickAnalysisPreviewVisualKind.IconSet => QuickAnalysisPreviewIconGlyph.IconSet,
            QuickAnalysisPreviewVisualKind.Highlight => QuickAnalysisPreviewIconGlyph.HighlightGrid,
            QuickAnalysisPreviewVisualKind.ClearFormat => QuickAnalysisPreviewIconGlyph.ClearFormat,
            QuickAnalysisPreviewVisualKind.ColumnChart => QuickAnalysisPreviewIconGlyph.VerticalBars,
            QuickAnalysisPreviewVisualKind.ColumnSparkline => QuickAnalysisPreviewIconGlyph.VerticalBars,
            QuickAnalysisPreviewVisualKind.StackedColumnChart => QuickAnalysisPreviewIconGlyph.StackedVerticalBars,
            QuickAnalysisPreviewVisualKind.LineChart => QuickAnalysisPreviewIconGlyph.LineChart,
            QuickAnalysisPreviewVisualKind.LineSparkline => QuickAnalysisPreviewIconGlyph.LineChart,
            QuickAnalysisPreviewVisualKind.PieChart => QuickAnalysisPreviewIconGlyph.Pie,
            QuickAnalysisPreviewVisualKind.BarChart => QuickAnalysisPreviewIconGlyph.HorizontalBars,
            QuickAnalysisPreviewVisualKind.AreaChart => QuickAnalysisPreviewIconGlyph.Area,
            QuickAnalysisPreviewVisualKind.ScatterChart => QuickAnalysisPreviewIconGlyph.Scatter,
            QuickAnalysisPreviewVisualKind.TotalFormula => QuickAnalysisPreviewIconGlyph.Formula,
            QuickAnalysisPreviewVisualKind.Table => QuickAnalysisPreviewIconGlyph.Table,
            QuickAnalysisPreviewVisualKind.WinLossSparkline => QuickAnalysisPreviewIconGlyph.WinLoss,
            _ => QuickAnalysisPreviewIconGlyph.EmptyGrid
        });

    private static QuickAnalysisPreviewIconPlan PlanGlyph(QuickAnalysisPreviewIconGlyph glyph) =>
        new(glyph, DefaultWidth, DefaultHeight, BuildElements(glyph));

    private static IReadOnlyList<QuickAnalysisPreviewIconElement> BuildElements(QuickAnalysisPreviewIconGlyph glyph) =>
        glyph switch
        {
            QuickAnalysisPreviewIconGlyph.HorizontalBars => Bars(vertical: false, stacked: false),
            QuickAnalysisPreviewIconGlyph.ColorScale =>
            [
                Rect(4, 5, 8, 12, QuickAnalysisPreviewIconColor.FromRgb(248, 105, 107)),
                Rect(13, 5, 8, 12, QuickAnalysisPreviewIconColor.FromRgb(255, 235, 132)),
                Rect(22, 5, 8, 12, QuickAnalysisPreviewIconColor.FromRgb(99, 190, 123)),
            ],
            QuickAnalysisPreviewIconGlyph.IconSet =>
            [
                Ellipse(6, 7, QuickAnalysisPreviewIconColor.Firebrick),
                Ellipse(15, 7, QuickAnalysisPreviewIconColor.Goldenrod),
                Ellipse(24, 7, QuickAnalysisPreviewIconColor.SeaGreen),
            ],
            QuickAnalysisPreviewIconGlyph.HighlightGrid =>
                Grid(QuickAnalysisPreviewIconColor.LightGoldenrodYellow, QuickAnalysisPreviewIconColor.Goldenrod),
            QuickAnalysisPreviewIconGlyph.ClearFormat =>
            [
                ..Grid(QuickAnalysisPreviewIconColor.White, QuickAnalysisPreviewIconColor.LightGray),
                Line(6, 17, 28, 5, QuickAnalysisPreviewIconColor.Firebrick, 1.5),
            ],
            QuickAnalysisPreviewIconGlyph.VerticalBars => Bars(vertical: true, stacked: false),
            QuickAnalysisPreviewIconGlyph.StackedVerticalBars => Bars(vertical: true, stacked: true),
            QuickAnalysisPreviewIconGlyph.LineChart =>
            [
                Line(5, 16, 13, 10, QuickAnalysisPreviewIconColor.SteelBlue, 1.4),
                Line(13, 10, 21, 13, QuickAnalysisPreviewIconColor.SteelBlue, 1.4),
                Line(21, 13, 29, 6, QuickAnalysisPreviewIconColor.SteelBlue, 1.4),
            ],
            QuickAnalysisPreviewIconGlyph.Pie =>
            [
                Ellipse(8, 4, QuickAnalysisPreviewIconColor.SteelBlue, 14),
                Rect(21, 7, 7, 4, QuickAnalysisPreviewIconColor.Goldenrod),
                Rect(21, 13, 7, 4, QuickAnalysisPreviewIconColor.SeaGreen),
            ],
            QuickAnalysisPreviewIconGlyph.Area =>
            [
                new QuickAnalysisPreviewIconPolygon(
                    [
                        new(5, 17),
                        new(11, 9),
                        new(18, 12),
                        new(27, 5),
                        new(29, 17),
                    ],
                    QuickAnalysisPreviewIconColor.FromArgb(150, 70, 130, 180)),
            ],
            QuickAnalysisPreviewIconGlyph.Scatter =>
            [
                Ellipse(6, 13, QuickAnalysisPreviewIconColor.SteelBlue, 4),
                Ellipse(14, 8, QuickAnalysisPreviewIconColor.SeaGreen, 4),
                Ellipse(24, 5, QuickAnalysisPreviewIconColor.Goldenrod, 4),
            ],
            QuickAnalysisPreviewIconGlyph.Formula =>
            [
                new QuickAnalysisPreviewIconText(
                    "fx",
                    10,
                    1,
                    15,
                    QuickAnalysisPreviewIconFontWeight.SemiBold,
                    QuickAnalysisPreviewIconColor.SteelBlue),
            ],
            QuickAnalysisPreviewIconGlyph.Table =>
                Grid(
                    QuickAnalysisPreviewIconColor.FromRgb(229, 244, 239),
                    QuickAnalysisPreviewIconColor.FromRgb(38, 120, 95)),
            QuickAnalysisPreviewIconGlyph.WinLoss =>
            [
                Rect(7, 5, 4, 6, QuickAnalysisPreviewIconColor.SeaGreen),
                Rect(15, 11, 4, 6, QuickAnalysisPreviewIconColor.Firebrick),
                Rect(23, 5, 4, 6, QuickAnalysisPreviewIconColor.SeaGreen),
            ],
            _ => Grid(QuickAnalysisPreviewIconColor.White, QuickAnalysisPreviewIconColor.LightGray),
        };

    private static QuickAnalysisPreviewIconElement[] Grid(
        QuickAnalysisPreviewIconColor fill,
        QuickAnalysisPreviewIconColor stroke)
    {
        var elements = new QuickAnalysisPreviewIconElement[6];
        var index = 0;
        for (var row = 0; row < 2; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                elements[index++] = new QuickAnalysisPreviewIconRectangle(
                    3 + col * 9,
                    3 + row * 8,
                    9,
                    8,
                    fill,
                    stroke,
                    0.6);
            }
        }

        return elements;
    }

    private static QuickAnalysisPreviewIconElement[] Bars(bool vertical, bool stacked)
    {
        var fill = QuickAnalysisPreviewIconColor.SteelBlue;
        var accent = QuickAnalysisPreviewIconColor.FromRgb(132, 185, 95);
        if (vertical)
        {
            var heights = new[] { 8.0, 15.0, 11.0 };
            var elements = new QuickAnalysisPreviewIconElement[stacked ? heights.Length * 2 : heights.Length];
            var index = 0;
            for (var i = 0; i < heights.Length; i++)
            {
                elements[index++] = Rect(7 + i * 8, 18 - heights[i], 5, heights[i], fill);
                if (stacked)
                    elements[index++] = Rect(7 + i * 8, 18 - heights[i] - 4, 5, 4, accent);
            }

            return elements;
        }

        var widths = new[] { 14.0, 22.0, 18.0 };
        var bars = new QuickAnalysisPreviewIconElement[widths.Length];
        for (var i = 0; i < widths.Length; i++)
            bars[i] = Rect(5, 5 + i * 5, widths[i], 3, fill);

        return bars;
    }

    private static QuickAnalysisPreviewIconRectangle Rect(
        double left,
        double top,
        double width,
        double height,
        QuickAnalysisPreviewIconColor fill) =>
        new(left, top, width, height, fill);

    private static QuickAnalysisPreviewIconEllipse Ellipse(
        double left,
        double top,
        QuickAnalysisPreviewIconColor fill,
        double size = 5) =>
        new(left, top, size, fill);

    private static QuickAnalysisPreviewIconLine Line(
        double x1,
        double y1,
        double x2,
        double y2,
        QuickAnalysisPreviewIconColor stroke,
        double thickness) =>
        new(x1, y1, x2, y2, stroke, thickness);
}
