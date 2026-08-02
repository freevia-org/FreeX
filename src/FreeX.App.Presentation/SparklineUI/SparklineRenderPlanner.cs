using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SparklineUI;

/// <summary>
/// One sparkline ready to draw in a cell: its location, kind, the numeric series read from the
/// source range, and the cell-local rectangle the geometry is laid out inside. Produced by
/// <see cref="SparklineRenderPlanner"/> from the active sheet, with no renderer types, so the
/// non-UI glue (sheet -&gt; series -&gt; layout) is unit-testable without a running shell.
/// </summary>
public readonly record struct SparklineRenderInstruction(
    Guid Id,
    CellAddress Location,
    SparklineKind Kind,
    IReadOnlyList<double> Values,
    LayoutRect CellRect,
    bool RightToLeft = false);

/// <summary>
/// Pure, UI-free glue that mirrors the Windows host's sparkline pipeline for shared shell renderers:
/// reads each <see cref="SparklineModel"/>'s data range off the sheet into a numeric series
/// (number / date / bool, hidden rows and columns skipped), then dispatches the series through the
/// portable <see cref="SparklineLayoutEngine"/>. The renderer turns the returned geometry into
/// platform primitives; the tests exercise the value read + layout selection without any UI.
/// </summary>
public static class SparklineRenderPlanner
{
    // Matches the Windows GridView's per-cell sparkline inset (3px on every side, unzoomed).
    public const double CellInset = 3;

    /// <summary>
    /// Reads every sparkline on <paramref name="sheet"/> into its numeric series, keyed by id.
    /// Delegates to <see cref="SparklineSeriesReader.BuildValues"/>: data ranges over the
    /// supported cell cap are reported as empty, and only number / date / bool cells contribute.
    /// <paramref name="workbook"/> must own <paramref name="sheet"/> -- it resolves each
    /// sparkline's data range to its own source sheet when that differs from the host sheet
    /// (Excel's cross-sheet sparkline data range).
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildValues(Workbook workbook, Sheet sheet) =>
        SparklineSeriesReader.BuildValues(workbook, sheet);

    /// <summary>
    /// Reads a single sparkline's data range into its numeric series. Hidden rows and columns are
    /// skipped; non-numeric cells are ignored. <paramref name="workbook"/> must own
    /// <paramref name="sheet"/> -- see <see cref="BuildValues"/>.
    /// </summary>
    public static IReadOnlyList<double> ReadSeries(Workbook workbook, Sheet sheet, SparklineModel sparkline) =>
        SparklineSeriesReader.ReadSeries(workbook, sheet, sparkline);

    /// <summary>
    /// Builds the cell-local draw instructions for every sparkline whose <see cref="SparklineModel.Location"/>
    /// resolves to a rectangle via <paramref name="cellRectLookup"/> and whose series is non-empty.
    /// The lookup hands back the cell's pixel rectangle in shell coordinates; the inset is applied
    /// here so the geometry sits inside the cell consistently across renderers.
    /// </summary>
    public static IReadOnlyList<SparklineRenderInstruction> Plan(
        Sheet sheet,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>> values,
        CellRectLookup cellRectLookup,
        double inset = CellInset)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(cellRectLookup);

        var instructions = new List<SparklineRenderInstruction>(sheet.Sparklines.Count);
        foreach (var sparkline in sheet.Sparklines)
        {
            if (!values.TryGetValue(sparkline.Id, out var series) || series.Count == 0)
                continue;
            if (!cellRectLookup(sparkline.Location, out var cellRect))
                continue;

            var rect = new LayoutRect(
                cellRect.X + inset,
                cellRect.Y + inset,
                Math.Max(1, cellRect.Width - (inset * 2)),
                Math.Max(1, cellRect.Height - (inset * 2)));
            instructions.Add(new SparklineRenderInstruction(
                sparkline.Id,
                sparkline.Location,
                sparkline.Kind,
                series,
                rect,
                sparkline.RightToLeft));
        }

        return instructions;
    }

    /// <summary>
    /// Lays out a line sparkline's geometry from a render instruction, honoring the sparkline's
    /// <see cref="SparklineRenderInstruction.RightToLeft"/> "Plot Data Right-to-Left" flag.
    /// </summary>
    public static SparklineLineLayout LayoutLine(SparklineRenderInstruction instruction) =>
        SparklineLayoutEngine.CalculateLineLayout(
            instruction.Values, instruction.CellRect, overrideMin: null, overrideMax: null, datePositions: null, instruction.RightToLeft);

    /// <summary>
    /// Lays out a column / win-loss sparkline's geometry from a render instruction, honoring the
    /// sparkline's <see cref="SparklineRenderInstruction.RightToLeft"/> "Plot Data Right-to-Left" flag.
    /// </summary>
    public static SparklineColumnLayout LayoutColumn(SparklineRenderInstruction instruction) =>
        SparklineLayoutEngine.CalculateColumnLayout(instruction.Values, instruction.CellRect, instruction.Kind, instruction.RightToLeft);

    /// <summary>
    /// Resolves a cell's pixel rectangle (in shell coordinates) for a sparkline location, returning
    /// false when the cell is not currently laid out (e.g. scrolled out of the viewport).
    /// </summary>
    public delegate bool CellRectLookup(CellAddress location, out LayoutRect cellRect);
}
