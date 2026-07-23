using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Non-UI glue between <see cref="PageBreakPreviewLayoutPlanner"/> and a page-break preview overlay
/// canvas. It resolves the active print range for a sheet (explicit print area, else used range), maps
/// the on-screen viewport metrics into the display pixel space the grid actually renders in (zoom +
/// minimum row/column sizing + header offset), and flattens the planner's layout into a renderer-
/// agnostic list of draw instructions (masks, page borders with their visible edges, break lines, and
/// the centered "Page N" watermark with its font size). All pure data, so it is unit-tested directly;
/// renderer code only turns each instruction into platform controls.
/// </summary>

/// <summary>A semi-transparent rectangle dimming the area outside the print range.</summary>
public readonly record struct PageBreakMaskInstruction(double Left, double Top, double Width, double Height);

/// <summary>
/// A page border to stroke. Only the edges in <see cref="Edges"/> are on-screen, so the renderer
/// draws those four sides individually (rather than one rect) to avoid borders on clipped edges.
/// </summary>
public readonly record struct PageBreakBorderInstruction(
    double Left,
    double Top,
    double Width,
    double Height,
    PageBreakPreviewPageEdges Edges);

/// <summary>A dashed automatic page-break line segment.</summary>
public readonly record struct PageBreakLineInstruction(double X1, double Y1, double X2, double Y2);

/// <summary>The "Page N" watermark text, its font size, and the page rect to center it in.</summary>
public readonly record struct PageBreakWatermarkInstruction(
    string Text,
    double FontSize,
    double Left,
    double Top,
    double Width,
    double Height);

/// <summary>The full flattened instruction set for one page-break-preview frame.</summary>
public sealed record PageBreakPreviewInstructions(
    IReadOnlyList<PageBreakMaskInstruction> Masks,
    IReadOnlyList<PageBreakBorderInstruction> Borders,
    IReadOnlyList<PageBreakLineInstruction> Lines,
    IReadOnlyList<PageBreakWatermarkInstruction> Watermarks)
{
    public static PageBreakPreviewInstructions Empty { get; } = new([], [], [], []);

    public bool IsEmpty => Masks.Count == 0 && Borders.Count == 0 && Lines.Count == 0 && Watermarks.Count == 0;
}

public static class PageBreakPreviewInstructionBuilder
{
    /// <summary>
    /// Resolves the print range that the page-break preview slices into pages: the sheet's explicit
    /// print area when it is set on this sheet, otherwise the used range. Returns false when neither is
    /// available (an empty sheet has nothing to preview).
    /// </summary>
    public static bool TryResolvePrintRange(Sheet sheet, out GridRange printRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.PrintArea is { } explicitArea && explicitArea.Start.Sheet == sheet.Id)
        {
            printRange = explicitArea;
            return true;
        }

        if (sheet.GetUsedRange() is { } usedRange)
        {
            printRange = usedRange;
            return true;
        }

        printRange = default;
        return false;
    }

    /// <summary>
    /// Resolves every print range the page-break preview should render: all of the sheet's configured
    /// print areas (Excel supports a multi-area print range via a comma-separated
    /// <c>_xlnm.Print_Area</c> defined name — <see cref="Sheet.PrintAreas"/> holds one <see
    /// cref="GridRange"/> per area), or the used range when none is configured. Unlike <see
    /// cref="TryResolvePrintRange"/> (which only exposes the first area, for single-range callers),
    /// this mirrors <c>WorkbookExportPrintPlanner.ResolveSheetPrintRanges</c> so the preview does not
    /// mask out a real, second-or-later print area as non-printing. Returns false when neither is
    /// available (an empty sheet has nothing to preview).
    /// </summary>
    public static bool TryResolvePrintRanges(Sheet sheet, out IReadOnlyList<GridRange> printRanges)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var areas = sheet.PrintAreas.Where(area => area.Start.Sheet == sheet.Id).ToList();
        if (areas.Count > 0)
        {
            printRanges = areas;
            return true;
        }

        if (sheet.GetUsedRange() is { } usedRange)
        {
            printRanges = [usedRange];
            return true;
        }

        printRanges = [];
        return false;
    }

    /// <summary>
    /// Re-projects the session's viewport metrics into the display pixel space the grid renderer draws
    /// in: each column/row gets <c>max(minimum, size) × zoom</c> and cumulative offsets accumulate in
    /// that same space, so the overlay lines up with the rendered cells. Mirrors the grid's
    /// GetDisplayedColumnWidth / GetDisplayedRowHeight sizing.
    /// </summary>
    public static ViewportModel ProjectToDisplaySpace(
        ViewportModel viewport,
        double zoomFactor,
        double minimumColumnWidth,
        double minimumRowHeight)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var columns = new List<ColMetric>(viewport.ColMetrics.Count);
        var left = 0.0;
        foreach (var metric in viewport.ColMetrics)
        {
            var width = Math.Max(minimumColumnWidth, metric.Width) * zoomFactor;
            columns.Add(new ColMetric(metric.Col, width, left));
            left += width;
        }

        var rows = new List<RowMetric>(viewport.RowMetrics.Count);
        var top = 0.0;
        foreach (var metric in viewport.RowMetrics)
        {
            var height = Math.Max(minimumRowHeight, metric.Height) * zoomFactor;
            rows.Add(new RowMetric(metric.Row, height, top));
            top += height;
        }

        return viewport with { RowMetrics = rows, ColMetrics = columns };
    }

    /// <summary>Flattens a planner layout into renderer-agnostic draw instructions.</summary>
    public static PageBreakPreviewInstructions Build(PageBreakPreviewLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var masks = new List<PageBreakMaskInstruction>(layout.OutsidePrintAreaMasks.Count);
        foreach (var mask in layout.OutsidePrintAreaMasks)
            masks.Add(new PageBreakMaskInstruction(mask.Left, mask.Top, mask.Width, mask.Height));

        var borders = new List<PageBreakBorderInstruction>(layout.Pages.Count);
        var watermarks = new List<PageBreakWatermarkInstruction>(layout.Pages.Count);
        foreach (var page in layout.Pages)
        {
            var bounds = page.Bounds;
            borders.Add(new PageBreakBorderInstruction(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                page.VisibleEdges));

            watermarks.Add(new PageBreakWatermarkInstruction(
                $"Page {page.PageNumber}",
                PageBreakPreviewLayoutPlanner.CalculateWatermarkFontSize(bounds),
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height));
        }

        var lines = new List<PageBreakLineInstruction>(layout.AutomaticBreakLines.Count);
        foreach (var line in layout.AutomaticBreakLines)
            lines.Add(new PageBreakLineInstruction(line.Start.X, line.Start.Y, line.End.X, line.End.Y));

        return new PageBreakPreviewInstructions(masks, borders, lines, watermarks);
    }
}
