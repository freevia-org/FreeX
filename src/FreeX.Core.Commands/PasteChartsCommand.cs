using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// R92-cmd-paste-floating-objects: Chart analogue of <see cref="PastePicturesCommand"/> -- carries a
/// floating chart along with a plain Ctrl+V paste when the chart's top-left corner lies inside the
/// copied range. Unlike Picture/DrawingShape/TextBox, <see cref="ChartModel"/> has no cell-anchored
/// <c>Anchor</c>; its position is an absolute pixel <c>Left</c>/<c>Top</c> on the sheet's drawing
/// canvas (see XlsxWorksheetChartWriter.ToAnchorMarker, which converts that pixel position to a
/// col/colOff/row/rowOff twoCellAnchor at save time). So instead of remapping a cell address, this
/// command preserves the chart's exact pixel offset relative to the copied range's top-left corner
/// and re-applies that same offset relative to each paste destination's top-left corner. The
/// cumulative row-height/column-width walk used to convert a cell coordinate to a pixel position
/// mirrors RowColumnShiftHelpers.PrintAndCharts.cs's CumulativeRowTop/CumulativeColumnLeft (private to
/// that file) -- duplicated here rather than shared, to keep this command's chart-position math
/// self-contained and avoid coupling to that file's internals.
/// </summary>
public sealed class PasteChartsCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
    private readonly bool _transpose;
    private readonly IReadOnlyList<ChartModel> _sourceCharts;
    private List<ChartModel>? _added;

    public string Label => "Paste Charts";

    public PasteChartsCommand(
        SheetId sourceSheetId,
        SheetId sheetId,
        GridRange sourceRange,
        CellAddress destination,
        IReadOnlyList<ChartModel> sourceCharts,
        bool transpose)
    {
        _sourceSheetId = sourceSheetId;
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _sourceCharts = sourceCharts;
        _transpose = transpose;
    }

    /// <summary>
    /// Tiling counterpart, mirroring <see cref="PastePicturesCommand"/>'s destination-range overload.
    /// </summary>
    public PasteChartsCommand(
        SheetId sourceSheetId,
        SheetId sheetId,
        GridRange sourceRange,
        GridRange destinationRange,
        IReadOnlyList<ChartModel> sourceCharts,
        bool transpose)
        : this(sourceSheetId, sheetId, sourceRange, destinationRange.Start, sourceCharts, transpose)
    {
        _destinationRange = destinationRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var targetSheet = ctx.GetSheet(_sheetId);
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var sourceSheet = ctx.GetSheet(_sourceSheetId);
        var sourceLeft = CumulativeColumnLeft(sourceSheet, _sourceRange.Start.Col);
        var sourceTop = CumulativeRowTop(sourceSheet, _sourceRange.Start.Row);

        _added = [];
        var affected = new List<CellAddress>();
        foreach (var tileAnchor in PastePlacementPolicy.EnumerateTileAnchors(
                     _sourceRange,
                     _destination,
                     _destinationRange,
                     _transpose))
        {
            var destLeft = CumulativeColumnLeft(targetSheet, tileAnchor.Col);
            var destTop = CumulativeRowTop(targetSheet, tileAnchor.Row);

            foreach (var chart in _sourceCharts)
            {
                var dx = chart.Left - sourceLeft;
                var dy = chart.Top - sourceTop;
                var (mappedDx, mappedDy) = _transpose ? (dy, dx) : (dx, dy);

                // Plain-paste carries the chart object itself, not the data it plots -- the
                // DataRange (and any verbatim series/error-bar formula text) must keep pointing at
                // the exact original source sheet/cells regardless of the paste destination, unlike
                // whole-sheet Duplicate Sheet where a same-sheet DataRange follows the copy.
                var clone = DuplicateSheetDrawingCloner.CloneChart(
                    chart, _sourceSheetId, _sheetId, remapSameSheetDataRange: false);
                clone.Left = destLeft + mappedDx;
                clone.Top = destTop + mappedDy;
                targetSheet.Charts.Add(clone);
                _added.Add(clone);
                affected.Add(tileAnchor);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected.Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_added is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var chart in _added)
            sheet.Charts.Remove(chart);
        _added = null;
    }

    /// <summary>
    /// Whether <paramref name="chart"/>'s top-left corner (Left/Top) falls inside the pixel bounding
    /// box of <paramref name="range"/> on <paramref name="sheet"/> -- the Chart analogue of
    /// <c>sourceRange.Contains(picture.Anchor)</c> for cell-anchored objects. Used by
    /// PasteCommandFactory to decide whether a plain-paste of a cell range must carry a chart along.
    /// </summary>
    internal static bool IsAnchoredIn(Sheet sheet, ChartModel chart, GridRange range)
    {
        var left = CumulativeColumnLeft(sheet, range.Start.Col);
        var right = CumulativeColumnLeft(sheet, range.End.Col + 1);
        var top = CumulativeRowTop(sheet, range.Start.Row);
        var bottom = CumulativeRowTop(sheet, range.End.Row + 1);
        return chart.Left >= left && chart.Left < right && chart.Top >= top && chart.Top < bottom;
    }

    // Cumulative pixel size of every row/column strictly before `index` (1-based). Mirrors
    // RowColumnShiftHelpers.PrintAndCharts.cs's CumulativeSize (private to that file).
    private static double CumulativeSize(IEnumerable<KeyValuePair<uint, double>> customSizes, double defaultSize, uint index)
    {
        if (index <= 1) return 0;
        var total = (double)(index - 1) * defaultSize;
        foreach (var (i, size) in customSizes)
            if (i < index) total += size - defaultSize;
        return Math.Max(0, total);
    }

    private static double CumulativeRowTop(Sheet sheet, uint row) =>
        CumulativeSize(sheet.RowHeights, sheet.DefaultRowHeight, row);

    // Same *8 character-to-pixel factor as XlsxWorksheetChartWriter.ToAnchorMarker so the comparison
    // against Left (already in that pixel unit) is consistent.
    private static double CumulativeColumnLeft(Sheet sheet, uint col) =>
        CumulativeSize(
            sheet.ColumnWidths.Select(kv => new KeyValuePair<uint, double>(kv.Key, kv.Value * 8)),
            sheet.DefaultColumnWidth * 8, col);
}
