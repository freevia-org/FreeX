using Avalonia;
using Avalonia.Media;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds an Avalonia <see cref="IBrush"/> that renders an OOXML cell fill pattern on top of the
/// cell's solid background color.  Mirrors the WPF <c>DrawFillPattern</c> method in
/// <c>GridView.Rendering.CellStyles.cs</c> but uses Avalonia's retained-mode <see cref="DrawingBrush"/>
/// instead of immediate-mode <c>DrawingContext</c> calls.
/// </summary>
/// <remarks>
/// The pure opacity calculation for the five gray patterns is exposed as
/// <see cref="GrayPatternOpacity"/> so it can be unit-tested without a running UI.
/// Likewise <see cref="NeedsPatternBrush"/> and <see cref="IsGrayPattern"/> are testable helpers.
/// </remarks>
internal static class CellPatternFill
{
    // ── Tile size (DIPs) — matches WPF step=6 for lines, step=8 for diagonals ──────────────────
    // ── Public API ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="style"/> specifies a pattern that this
    /// class can render (i.e. not None and not Solid — Solid is handled by the background fill).
    /// </summary>
    public static bool NeedsPatternBrush(CellStyle? style) =>
        style is not null &&
        CellFillPatternPlanner.Plan(style.FillPatternStyle).Kind != CellFillPatternPlanKind.None;

    /// <summary>
    /// Returns <see langword="true"/> when the pattern is one of the five gray-density styles
    /// (Gray0625 … DarkGray) that are rendered as a semi-transparent rectangle rather than a
    /// line hatch.
    /// </summary>
    public static bool IsGrayPattern(CellFillPatternStyle style) =>
        CellFillPatternPlanner.Plan(style).Kind == CellFillPatternPlanKind.Opacity;

    /// <summary>
    /// Returns the fill opacity (0.0–1.0) for a gray-density pattern.
    /// Matches WPF: Gray0625=12%, Gray125=18%, LightGray=28%, MediumGray=45%, DarkGray=62%.
    /// Returns 0 for non-gray patterns.
    /// </summary>
    public static double GrayPatternOpacity(CellFillPatternStyle style) =>
        CellFillPatternPlanner.Plan(style).Opacity;

    /// <summary>
    /// Builds a compositing <see cref="IBrush"/> for the pattern portion of a cell fill.
    /// The brush should be layered ON TOP of the cell's solid/gradient background
    /// (the <see cref="Avalonia.Controls.Border"/> background already carries the background color;
    /// return this brush as the <c>Fill</c> of an inner <c>Rectangle</c>).
    /// Returns <see langword="null"/> when <paramref name="style"/> has no visible pattern.
    /// </summary>
    public static IBrush? Build(CellStyle? style, WorkbookTheme theme)
    {
        var fillPlan = CellFillMaterializationPlanner.Plan(
            style,
            theme,
            CellFillMaterializationProfile.Avalonia,
            CellFillFallbackKind.Transparent);
        return Build(fillPlan);
    }

    public static IBrush? Build(CellFillMaterializationPlan fillPlan)
    {
        var patternPlan = fillPlan.Pattern;
        if (patternPlan.Kind == CellFillPatternPlanKind.None || fillPlan.PatternColor is not { } patternColor)
            return null;

        var fgColor      = Color.FromRgb(patternColor.R, patternColor.G, patternColor.B);

        if (patternPlan.Kind == CellFillPatternPlanKind.Opacity)
        {
            // Semi-transparent rectangle — same visual as WPF dc.DrawRectangle with alpha brush.
            return new SolidColorBrush(fgColor, patternPlan.Opacity);
        }

        // Line/cross-hatch patterns — build a tiling DrawingBrush.
        return BuildHatchBrush(patternPlan, fgColor);
    }

    // ── Hatch DrawingBrush construction ───────────────────────────────────────────────────────────

    private static DrawingBrush BuildHatchBrush(CellFillPatternPlan patternPlan, Color fgColor)
    {
        var pen  = new Pen(new SolidColorBrush(fgColor), patternPlan.StrokeThickness);
        var size = patternPlan.TileSize;

        var group = new DrawingGroup();

        foreach (var line in patternPlan.Lines)
        {
            switch (line)
            {
            case CellFillPatternLinePrimitive.Horizontal:
                AddHorizontalLine(group, pen, size);
                break;

            case CellFillPatternLinePrimitive.Vertical:
                AddVerticalLine(group, pen, size);
                break;

            case CellFillPatternLinePrimitive.DescendingDiagonal:
                // Descending diagonal: top-left → bottom-right.
                AddDiagonalLine(group, pen, size, descending: true);
                break;

            case CellFillPatternLinePrimitive.AscendingDiagonal:
                // Ascending diagonal: bottom-left → top-right.
                AddDiagonalLine(group, pen, size, descending: false);
                break;

            }
        }

        return new DrawingBrush(group)
        {
            TileMode        = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, size, size, RelativeUnit.Absolute),
            SourceRect      = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative),
        };
    }

    // ── Tile stroke helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a single horizontal stroke across the bottom of the tile at y = size − 0.5.
    /// When tiled, this produces horizontal lines spaced <paramref name="size"/> DIPs apart,
    /// matching WPF's <c>DrawHorizontalPattern</c> (step = 6).
    /// </summary>
    private static void AddHorizontalLine(DrawingGroup g, Pen pen, double size)
    {
        g.Children.Add(new GeometryDrawing
        {
            Pen      = pen,
            Geometry = new LineGeometry(
                new Point(0, size - 0.5),
                new Point(size, size - 0.5)),
        });
    }

    /// <summary>
    /// Adds a single vertical stroke on the right edge of the tile at x = size − 0.5.
    /// When tiled, this produces vertical lines spaced <paramref name="size"/> DIPs apart,
    /// matching WPF's <c>DrawVerticalPattern</c>.
    /// </summary>
    private static void AddVerticalLine(DrawingGroup g, Pen pen, double size)
    {
        g.Children.Add(new GeometryDrawing
        {
            Pen      = pen,
            Geometry = new LineGeometry(
                new Point(size - 0.5, 0),
                new Point(size - 0.5, size)),
        });
    }

    /// <summary>
    /// Adds a diagonal stroke within one tile that, when tiled at <paramref name="size"/>×size,
    /// produces lines spaced <paramref name="size"/> DIPs apart — matching WPF's
    /// <c>DrawDiagonalPattern</c> (step = 8).
    /// <para>
    /// Descending (top-left→bottom-right) or ascending (bottom-left→top-right).
    /// </para>
    /// </summary>
    private static void AddDiagonalLine(DrawingGroup g, Pen pen, double size, bool descending)
    {
        // A single line from one corner to the opposite corner of the tile.
        // Together with TileMode=Tile this produces the full diagonal hatch.
        var start = descending
            ? new Point(0, 0)
            : new Point(0, size);
        var end = descending
            ? new Point(size, size)
            : new Point(size, 0);

        g.Children.Add(new GeometryDrawing
        {
            Pen      = pen,
            Geometry = new LineGeometry(start, end),
        });
    }
}
