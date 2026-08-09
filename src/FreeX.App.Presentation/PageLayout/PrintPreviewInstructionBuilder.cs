using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Non-UI glue between the portable <see cref="PageContentLayout"/> and a print-preview
/// canvas. It flattens one page's ordered render instructions (page background, cell fills, gridlines,
/// per-edge cell borders, cell/chart/heading/header-footer text) into a renderer-agnostic, ordered list of
/// paint primitives (filled rectangles, line segments, positioned text runs). Renderer code only
/// turns each primitive into platform controls, so all of the layout-to-primitive math is unit-tested
/// directly without a running UI.
///
/// It also owns the page-enumeration / navigation math (page count from the pagination plan plus
/// clamped prev/next stepping) so the window's Prev/Next buttons stay in range without UI state logic.
/// </summary>

/// <summary>The kind of paint primitive a <see cref="PrintPreviewPaintInstruction"/> represents.</summary>
public enum PrintPreviewPaintKind
{
    /// <summary>A filled (and optionally unfilled) rectangle: page background or a cell fill.</summary>
    Rectangle,

    /// <summary>A single straight stroked line: a gridline or one cell-border edge.</summary>
    Line,

    /// <summary>A positioned single-line text run: cell text, a heading label, or a header/footer band.</summary>
    Text,

    /// <summary>
    /// R96-render-cf-databar-iconset-preview-1: a filled (and optionally outlined) ellipse -- the
    /// traffic-light / sign / symbol dot glyph of an icon-set conditional format, and the full-disc
    /// fallback WorkbookPdfContentBuilder's PDF path also uses for the Quarter style's pie wedge.
    /// </summary>
    Ellipse,

    /// <summary>
    /// R96-render-cf-databar-iconset-preview-1: a closed, filled (and optionally outlined) polygon --
    /// the arrow / flag / rating-bar / star icon-set glyph shapes.
    /// </summary>
    Polygon,
}

/// <summary>
/// One backend-agnostic paint primitive. Only the fields relevant to <see cref="Kind"/> are populated;
/// the rest carry their defaults. Colors are <see cref="PresentationRgb"/> so no platform color type
/// leaks through; a null <see cref="Fill"/>/<see cref="Stroke"/> means "do not paint that aspect".
/// </summary>
public readonly record struct PrintPreviewPaintInstruction(
    PrintPreviewPaintKind Kind,
    double X1,
    double Y1,
    double X2,
    double Y2,
    PresentationRgb? Fill,
    PresentationRgb? Stroke,
    double StrokeThickness,
    string Text,
    PageTextFont Font,
    PageTextAlignment Alignment,
    IReadOnlyList<LayoutPoint>? Points = null)
{
    /// <summary>A filled/outlined rectangle from a top-left corner plus size.</summary>
    public static PrintPreviewPaintInstruction Rectangle(
        LayoutRect bounds,
        PresentationRgb? fill,
        PresentationRgb? stroke = null,
        double strokeThickness = 0) =>
        new(
            PrintPreviewPaintKind.Rectangle,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            fill,
            stroke,
            strokeThickness,
            "",
            default,
            PageTextAlignment.Left);

    /// <summary>A filled/outlined ellipse from a top-left corner plus size.</summary>
    public static PrintPreviewPaintInstruction Ellipse(
        LayoutRect bounds,
        PresentationRgb? fill,
        PresentationRgb? stroke = null,
        double strokeThickness = 0) =>
        new(
            PrintPreviewPaintKind.Ellipse,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            fill,
            stroke,
            strokeThickness,
            "",
            default,
            PageTextAlignment.Left);

    /// <summary>A closed, filled/outlined polygon through <paramref name="points"/>.</summary>
    public static PrintPreviewPaintInstruction Polygon(
        IReadOnlyList<LayoutPoint> points,
        PresentationRgb? fill,
        PresentationRgb? stroke = null,
        double strokeThickness = 0) =>
        new(
            PrintPreviewPaintKind.Polygon,
            0,
            0,
            0,
            0,
            fill,
            stroke,
            strokeThickness,
            "",
            default,
            PageTextAlignment.Left,
            points);

    /// <summary>A stroked line segment between two points.</summary>
    public static PrintPreviewPaintInstruction Line(
        LayoutPoint start,
        LayoutPoint end,
        PresentationRgb stroke,
        double strokeThickness) =>
        new(
            PrintPreviewPaintKind.Line,
            start.X,
            start.Y,
            end.X,
            end.Y,
            null,
            stroke,
            strokeThickness,
            "",
            default,
            PageTextAlignment.Left);

    /// <summary>A text run whose top-left origin is (<paramref name="origin"/>), laid out in a box of the given width.</summary>
    public static PrintPreviewPaintInstruction TextRun(
        LayoutPoint origin,
        double width,
        string text,
        PageTextFont font,
        PageTextAlignment alignment) =>
        new(
            PrintPreviewPaintKind.Text,
            origin.X,
            origin.Y,
            width,
            0,
            null,
            null,
            0,
            text,
            font,
            alignment);

    /// <summary>Rectangle top-left X (or line start X, or text origin X).</summary>
    public double Left => X1;

    /// <summary>Rectangle top-left Y (or line start Y, or text origin Y).</summary>
    public double Top => Y1;

    /// <summary>Rectangle width (or text box width).</summary>
    public double Width => X2;

    /// <summary>Rectangle height.</summary>
    public double Height => Y2;
}

/// <summary>The flattened paint primitives for one preview page plus the page rectangle to size the surface.</summary>
public sealed record PrintPreviewPagePainting(
    int PageNumber,
    LayoutRect PageBounds,
    IReadOnlyList<PrintPreviewPaintInstruction> Instructions);

public static class PrintPreviewInstructionBuilder
{
    /// <summary>Stroke color for printed gridlines, matching the source print renderer's light gray.</summary>
    public static readonly PresentationRgb GridLineColor = new(208, 208, 208);

    /// <summary>Fill behind row/column headings, mirroring the page-content builder's heading band.</summary>
    public static readonly PresentationRgb HeadingFill = PageContentRenderModelBuilder.HeadingFill;

    /// <summary>Black text for headings and (default) header/footer bands.</summary>
    public static readonly PresentationRgb HeadingTextColor = new(0, 0, 0);

    /// <summary>White page background painted under every page.</summary>
    public static readonly PresentationRgb PageBackground = new(255, 255, 255);

    /// <summary>
    /// Neutral gray bounded-box fill this canvas-only preview paints for a picture in place of the
    /// actual raster image (this preview has no image paint primitive -- see the "9. Pictures" build
    /// step). Print/XPS and PDF export paint the real image; only this in-app preview canvas falls
    /// back to a placeholder, the same scoped gap the chart layer above already has here.
    /// </summary>
    public static readonly PresentationRgb PicturePlaceholderFill = new(225, 225, 225);

    /// <summary>Font used for heading labels and header/footer bands, matching the page-content builder.</summary>
    public static readonly PageTextFont BandFont = new(
        PageContentRenderModelBuilder.PrintFontFamily,
        PageContentRenderModelBuilder.PrintFontSize,
        Bold: false,
        Italic: false,
        HeadingTextColor);

    /// <summary>
    /// Flattens one page's <see cref="PageContentLayout"/> into an ordered list of paint primitives.
    /// Paint order mirrors the source print renderer: page background, heading band fills, cell fills,
    /// gridlines, cell border edges, cell text, charts, text boxes, then header/footer bands on top.
    /// </summary>
    public static PrintPreviewPagePainting Build(WorksheetPrintPageContentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Build(plan.PortableLayout) with { PageBounds = plan.Transform.PageClip };
    }

    public static PrintPreviewPagePainting Build(PageContentLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var instructions = new List<PrintPreviewPaintInstruction>();

        // 1. Page background.
        instructions.Add(PrintPreviewPaintInstruction.Rectangle(layout.PageBounds, PageBackground));

        // 2. Heading band fills (behind heading labels), painted before content like the source renderer.
        foreach (var heading in layout.ColumnHeadings)
            instructions.Add(PrintPreviewPaintInstruction.Rectangle(heading.Bounds, HeadingFill));
        foreach (var heading in layout.RowHeadings)
            instructions.Add(PrintPreviewPaintInstruction.Rectangle(heading.Bounds, HeadingFill));

        // 3. Cell fills.
        foreach (var cell in layout.Cells)
        {
            if (cell.Fill is { } fill)
                instructions.Add(PrintPreviewPaintInstruction.Rectangle(cell.Bounds, fill));
        }

        // 3b. Data-bar / icon-set conditional-format overlays, painted over the cell fill and under
        // gridlines/text -- matching WorkbookPdfContentBuilder.BuildPageWithPageSetup's PDF paint order
        // (R96-render-cf-databar-iconset-1). Before this step no print-preview renderer painted
        // PageCellBlock.DataBar/IconSet at all (see that record's doc comment); the shared
        // PageContentRenderModelBuilder already computed both, so the state was silently dropped only
        // here (R96-render-cf-databar-iconset-preview-1).
        foreach (var cell in layout.Cells)
        {
            if (cell.DataBar is { } dataBar)
                AddDataBar(instructions, dataBar, cell.Bounds);
            if (cell.IconSet is { } iconSet)
                AddIconSet(instructions, iconSet, cell.Bounds);
        }

        // 4. Gridlines.
        foreach (var line in layout.GridLines)
            instructions.Add(PrintPreviewPaintInstruction.Line(line.Start, line.End, GridLineColor, 1));

        // 5. Cell border edges (each visible edge as its own line so clipped edges are skipped).
        foreach (var cell in layout.Cells)
            AddCellBorders(instructions, cell);

        // 6. Heading text.
        foreach (var heading in layout.ColumnHeadings)
            instructions.Add(PrintPreviewPaintInstruction.TextRun(
                heading.TextOrigin, heading.Bounds.Width, heading.Label, BandFont, PageTextAlignment.Center));
        foreach (var heading in layout.RowHeadings)
            instructions.Add(PrintPreviewPaintInstruction.TextRun(
                heading.TextOrigin, heading.Bounds.Width, heading.Label, BandFont, PageTextAlignment.Center));

        // 7. Cell text. The WPF print renderer draws printed cell text from the left inset,
        // so preview instructions normalize cell runs to that renderer-compatible alignment.
        foreach (var cell in layout.Cells)
        {
            if (!string.IsNullOrEmpty(cell.Text))
                instructions.Add(PrintPreviewPaintInstruction.TextRun(
                    cell.TextOrigin, cell.Bounds.Width, cell.Text, cell.Font, PageTextAlignment.Left));
        }

        // 8. Charts over the grid/cell text.
        foreach (var chart in layout.Charts)
        {
            instructions.Add(PrintPreviewPaintInstruction.Rectangle(
                chart.Bounds,
                chart.Fill,
                chart.Outline,
                chart.OutlineThickness));
        }

        foreach (var chart in layout.Charts)
        {
            foreach (var overlay in chart.TextOverlays)
            {
                if (!string.IsNullOrEmpty(overlay.Text))
                    instructions.Add(PrintPreviewPaintInstruction.TextRun(
                        new LayoutPoint(overlay.X, overlay.Y),
                        chart.Bounds.Width,
                        overlay.Text,
                        new PageTextFont(
                            PrintChartTextOverlayPlanner.FontFamily,
                            overlay.FontSize,
                            Bold: false,
                            Italic: false,
                            overlay.Color),
                        PageTextAlignment.Left));
            }
        }

        // 9. Pictures over the grid/cell text and chart layer, below text-box annotations.
        // R92-consumer-wiring-sweep-1: this preview canvas has no image paint primitive (only
        // rectangle/line/text -- the same reason a chart above only ever gets a placeholder box, never
        // an actual rendered chart), so a picture is drawn the same bounded-placeholder way rather than
        // being silently absent as it was before this pass.
        foreach (var picture in layout.Pictures)
        {
            instructions.Add(PrintPreviewPaintInstruction.Rectangle(
                picture.Bounds,
                PicturePlaceholderFill));
        }

        // 10. Text boxes over the grid/cell text and chart layer.
        foreach (var textBox in layout.TextBoxes)
        {
            instructions.Add(PrintPreviewPaintInstruction.Rectangle(
                textBox.Bounds,
                textBox.Fill,
                textBox.Outline,
                textBox.OutlineThickness));
        }

        foreach (var textBox in layout.TextBoxes)
        {
            if (!string.IsNullOrEmpty(textBox.Text))
                instructions.Add(PrintPreviewPaintInstruction.TextRun(
                    new LayoutPoint(textBox.TextBounds.Left, textBox.TextBounds.Top),
                    textBox.TextBounds.Width,
                    textBox.Text,
                    textBox.Font,
                    PageTextAlignment.Left));
        }

        // 11. Header / footer bands.
        foreach (var run in layout.HeaderRuns)
            instructions.Add(PrintPreviewPaintInstruction.TextRun(
                run.TextOrigin, run.Bounds.Width, run.Text, BandFont, run.Alignment));
        foreach (var run in layout.FooterRuns)
            instructions.Add(PrintPreviewPaintInstruction.TextRun(
                run.TextOrigin, run.Bounds.Width, run.Text, BandFont, run.Alignment));

        return new PrintPreviewPagePainting(layout.PageNumber, layout.PageBounds, instructions);
    }

    /// <summary>Gray (96,96,96) icon-set outline stroke, matching WorkbookPdfContentBuilder's CfIconOutlineColor.</summary>
    private static readonly PresentationRgb CfIconOutlineColor = new(96, 96, 96);

    /// <summary>Opaque white, used for the icon glyphs' white overlay strokes/fills.</summary>
    private static readonly PresentationRgb CfIconWhite = new(255, 255, 255);

    /// <summary>
    /// Draws one cell's data bar into its cell rect, reusing the same portable
    /// <see cref="ConditionalDataBarLayoutPlanner"/> geometry WorkbookPdfContentBuilder's PDF path draws
    /// with (R96-render-cf-databar-iconset-1) so the preview bar matches the PDF bar exactly. Does
    /// nothing if the bar would be empty.
    /// </summary>
    private static void AddDataBar(List<PrintPreviewPaintInstruction> instructions, DataBarLayout dataBar, LayoutRect bounds)
    {
        if (ConditionalDataBarLayoutPlanner.Plan(dataBar.StartFraction, dataBar.EndFraction) is not { } bar)
            return;

        var innerWidth = Math.Max(0.0, bounds.Width - (2 * bar.HorizontalInset));
        var innerHeight = Math.Max(0.0, bounds.Height - (2 * bar.VerticalInset));
        var barWidth = bar.FractionWidth * innerWidth;
        if (innerWidth <= 0 || innerHeight <= 0 || barWidth <= 0)
            return;

        var barLeft = bounds.Left + bar.HorizontalInset + (bar.Start * innerWidth);
        var barTop = bounds.Top + bar.VerticalInset;
        instructions.Add(PrintPreviewPaintInstruction.Rectangle(
            new LayoutRect(barLeft, barTop, barWidth, innerHeight), dataBar.FillColor));
    }

    /// <summary>
    /// Draws one cell's icon-set glyph into its cell rect, reusing the same portable
    /// <see cref="ConditionalIconCellLayoutPlanner"/>/<see cref="ConditionalIconGlyphGeometry"/> geometry
    /// WorkbookPdfContentBuilder's PDF path draws with (R96-render-cf-databar-iconset-1), so the preview
    /// glyph matches the PDF glyph. Two glyph primitives (Quarter's pie wedge, Star's partial-fill clip)
    /// fall back to a full-icon-color fill rather than reproducing the exact arc/clip geometry, mirroring
    /// the same sanctioned PDF fallback (this canvas-only preview has no arc/clip paint primitive, only
    /// rectangle/line/ellipse/polygon/text).
    /// </summary>
    private static void AddIconSet(List<PrintPreviewPaintInstruction> instructions, IconSetResult iconSet, LayoutRect bounds)
    {
        var cellLayout = ConditionalIconCellLayoutPlanner.CalculateCellLayout(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, iconSet.ShowValue);
        if (cellLayout.IconSize <= 0)
            return;

        var iconColor = ResolveIconColor(iconSet.Style, iconSet.BucketIndex, iconSet.IconCount);
        var glyphKind = ConditionalIconGlyphResolver.ResolveGlyphKind(iconSet.Style);
        var isAlternateVariant = ConditionalIconGlyphResolver.IsAlternateGlyphVariant(iconSet.Style);
        var glyphOps = ConditionalIconGlyphGeometry.Build(
            glyphKind, iconSet.BucketIndex, iconSet.IconCount,
            cellLayout.IconLeft, cellLayout.IconTop, cellLayout.IconSize, cellLayout.IconSize, isAlternateVariant);

        AddIconGlyphOps(instructions, glyphOps, iconColor);
    }

    private static PresentationRgb ResolveIconColor(string style, int bucketIndex, int iconCount)
    {
        var hex = ConditionalIconGlyphResolver.ResolveIconColor(style, bucketIndex, iconCount);
        return ColorInputParser.TryParseHexColor(hex, out var parsed) && parsed is { } color
            ? new PresentationRgb(color.R, color.G, color.B)
            : CfIconOutlineColor;
    }

    /// <summary>
    /// Converts one glyph's neutral <see cref="CfGlyphOp"/> primitives (already emitted in the layout's
    /// own absolute page coordinate space, since <see cref="ConditionalIconGlyphGeometry.Build"/> was
    /// called with the glyph's real page-space origin) into preview paint instructions. Unlike
    /// WorkbookPdfContentBuilder's PDF equivalent, no y-flip is needed here -- both this layout space and
    /// <see cref="CfGlyphOp"/>'s space are top-left-origin/y-down.
    /// </summary>
    private static void AddIconGlyphOps(
        List<PrintPreviewPaintInstruction> instructions, IReadOnlyList<CfGlyphOp> glyphOps, PresentationRgb iconColor)
    {
        const double outlineWidth = 0.5;
        const double whiteThinWidth = 0.75;
        const double whiteMediumWidth = 0.9;

        PresentationRgb? ResolveFill(CfGlyphFill fill) => fill switch
        {
            CfGlyphFill.Icon => iconColor,
            CfGlyphFill.White => CfIconWhite,
            _ => null,
        };

        (PresentationRgb? Color, double Width) ResolveStroke(CfGlyphStroke stroke) => stroke switch
        {
            CfGlyphStroke.Outline => (CfIconOutlineColor, outlineWidth),
            CfGlyphStroke.WhiteThin => (CfIconWhite, whiteThinWidth),
            CfGlyphStroke.WhiteMedium => (CfIconWhite, whiteMediumWidth),
            _ => (null, 0.0),
        };

        foreach (var op in glyphOps)
        {
            switch (op.Kind)
            {
                case CfGlyphPrimitiveKind.Ellipse:
                {
                    var fillColor = ResolveFill(op.Fill);
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    var bounds = new LayoutRect(
                        op.Center.X - op.RadiusX, op.Center.Y - op.RadiusY, op.RadiusX * 2, op.RadiusY * 2);
                    instructions.Add(PrintPreviewPaintInstruction.Ellipse(bounds, fillColor, strokeColor, strokeWidth));
                    break;
                }
                case CfGlyphPrimitiveKind.Line:
                {
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    if (strokeColor is { } sc && op.Points.Count >= 2)
                        instructions.Add(PrintPreviewPaintInstruction.Line(op.Points[0], op.Points[1], sc, strokeWidth));
                    break;
                }
                case CfGlyphPrimitiveKind.Box:
                {
                    var fillColor = ResolveFill(op.Fill);
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    instructions.Add(PrintPreviewPaintInstruction.Rectangle(op.Rect, fillColor, strokeColor, strokeWidth));
                    break;
                }
                case CfGlyphPrimitiveKind.Polygon:
                case CfGlyphPrimitiveKind.Polyline:
                {
                    if (op.Points.Count < 2)
                        break;

                    if (op.Kind == CfGlyphPrimitiveKind.Polyline)
                    {
                        // Open, unfilled stroke: decompose into individual line segments -- this preview
                        // has no dedicated open-polyline paint kind (only closed/filled Polygon).
                        var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                        if (strokeColor is { } sc)
                            for (var i = 0; i < op.Points.Count - 1; i++)
                                instructions.Add(PrintPreviewPaintInstruction.Line(op.Points[i], op.Points[i + 1], sc, strokeWidth));
                        break;
                    }

                    var fillColor = ResolveFill(op.Fill);
                    var (polyStrokeColor, polyStrokeWidth) = ResolveStroke(op.Stroke);
                    instructions.Add(PrintPreviewPaintInstruction.Polygon(op.Points, fillColor, polyStrokeColor, polyStrokeWidth));
                    break;
                }
                case CfGlyphPrimitiveKind.Pie:
                {
                    // R96 fallback (matching WorkbookPdfContentBuilder's PDF fallback): draw the pie
                    // wedge as a full filled circle rather than reproducing its exact arc geometry.
                    var bounds = new LayoutRect(
                        op.Center.X - op.RadiusX, op.Center.Y - op.RadiusY, op.RadiusX * 2, op.RadiusY * 2);
                    instructions.Add(PrintPreviewPaintInstruction.Ellipse(bounds, iconColor));
                    break;
                }
                case CfGlyphPrimitiveKind.StarFillFraction:
                {
                    // R96 fallback (matching WorkbookPdfContentBuilder's PDF fallback, explicitly
                    // sanctioned by ConditionalIconGlyphGeometry.Build's own doc comment): fill the whole
                    // star with the icon color instead of clipping to the fill fraction.
                    instructions.Add(PrintPreviewPaintInstruction.Polygon(op.Points, iconColor, CfIconOutlineColor, outlineWidth));
                    break;
                }
            }
        }
    }

    private static void AddCellBorders(ICollection<PrintPreviewPaintInstruction> instructions, PageCellBlock cell)
    {
        var borders = cell.Borders;
        if (!borders.HasAny)
            return;

        var b = cell.Bounds;
        if (borders.Top.IsVisible)
            AddEdge(instructions, b.Left, b.Top, b.Right, b.Top, borders.Top);
        if (borders.Bottom.IsVisible)
            AddEdge(instructions, b.Left, b.Bottom, b.Right, b.Bottom, borders.Bottom);
        if (borders.Left.IsVisible)
            AddEdge(instructions, b.Left, b.Top, b.Left, b.Bottom, borders.Left);
        if (borders.Right.IsVisible)
            AddEdge(instructions, b.Right, b.Top, b.Right, b.Bottom, borders.Right);
    }

    private static void AddEdge(
        ICollection<PrintPreviewPaintInstruction> instructions,
        double x1,
        double y1,
        double x2,
        double y2,
        PageBorderEdge edge) =>
        instructions.Add(PrintPreviewPaintInstruction.Line(
            new LayoutPoint(x1, y1),
            new LayoutPoint(x2, y2),
            edge.Color,
            BorderThickness(edge.Style)));

    /// <summary>Maps a model border style to a stroke thickness in device-independent pixels.</summary>
    public static double BorderThickness(BorderStyle style) =>
        style switch
        {
            BorderStyle.Thick => 3,
            BorderStyle.Medium => 2,
            BorderStyle.Double => 2,
            BorderStyle.None => 0,
            _ => 1,
        };

    /// <summary>Whether a model border style is drawn as a dashed stroke.</summary>
    public static bool IsDashed(BorderStyle style) =>
        style is BorderStyle.Dashed or BorderStyle.Dotted;
}

/// <summary>
/// Page-enumeration / navigation math for the print-preview window: the page count comes from the
/// pagination plan, and prev/next stepping is clamped into range so the window never asks the page
/// builder for an out-of-range index. Pure value logic, unit-tested without a UI.
/// </summary>
public readonly record struct PrintPreviewPageNavigator(int PageCount, int CurrentIndex)
{
    /// <summary>Creates a navigator over <paramref name="pageCount"/> pages positioned at the first page.</summary>
    public static PrintPreviewPageNavigator Create(int pageCount) =>
        new(Math.Max(0, pageCount), 0);

    /// <summary>Whether there is at least one page to show.</summary>
    public bool HasPages => PageCount > 0;

    /// <summary>The 1-based page number of the current page (1 when there are no pages).</summary>
    public int CurrentPageNumber => HasPages ? CurrentIndex + 1 : 1;

    /// <summary>Whether a previous page exists (so the Prev button should be enabled).</summary>
    public bool CanGoPrevious => CurrentIndex > 0;

    /// <summary>Whether a next page exists (so the Next button should be enabled).</summary>
    public bool CanGoNext => CurrentIndex < PageCount - 1;

    /// <summary>Returns a navigator one page earlier, clamped to the first page.</summary>
    public PrintPreviewPageNavigator Previous() =>
        this with { CurrentIndex = Math.Max(0, CurrentIndex - 1) };

    /// <summary>Returns a navigator one page later, clamped to the last page.</summary>
    public PrintPreviewPageNavigator Next() =>
        this with { CurrentIndex = Math.Min(Math.Max(0, PageCount - 1), CurrentIndex + 1) };

    /// <summary>Returns a navigator positioned at <paramref name="index"/>, clamped into range.</summary>
    public PrintPreviewPageNavigator JumpTo(int index) =>
        this with { CurrentIndex = HasPages ? Math.Clamp(index, 0, PageCount - 1) : 0 };

    /// <summary>"Page X of N" caption for the navigation label.</summary>
    public string Caption => $"Page {CurrentPageNumber} of {Math.Max(1, PageCount)}";
}
