using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>A UI-free chart source cell resolved from a viewport snapshot.</summary>
public readonly record struct ChartViewportCell(
    uint Row,
    uint Column,
    ScalarValue? RawValue,
    string DisplayText);

/// <summary>
/// Resolves chart-data cells and visible viewport cells with one precedence/filtering policy for
/// every desktop host. Chart-data cells win because they cover off-screen chart ranges; visible
/// cells fill any gaps without replacing that authoritative snapshot.
/// </summary>
public static class ChartViewportCellAccessorBuilder
{
    public static IReadOnlyDictionary<(uint Row, uint Column), ChartViewportCell> Resolve(
        ViewportModel viewport,
        SheetId sheetId,
        GridRange? range = null)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var capacity = ResolveCapacity(viewport, range);
        var lookup = new Dictionary<(uint Row, uint Column), ChartViewportCell>(capacity);

        if (viewport.ChartDataCells is { Count: > 0 })
        {
            foreach (var cell in viewport.ChartDataCells)
            {
                if (cell.SheetId != sheetId || !IsInRange(cell.Row, cell.Col, range))
                    continue;

                lookup[(cell.Row, cell.Col)] = new ChartViewportCell(
                    cell.Row,
                    cell.Col,
                    cell.RawValue,
                    cell.DisplayText);
            }
        }

        if (viewport.Cells is { Count: > 0 })
        {
            foreach (var cell in viewport.Cells)
            {
                if (!IsInRange(cell.Row, cell.Col, range))
                    continue;

                lookup.TryAdd(
                    (cell.Row, cell.Col),
                    new ChartViewportCell(cell.Row, cell.Col, cell.RawValue, cell.DisplayText));
            }
        }

        return lookup;
    }

    public static ChartLayoutRequestBuilder.ChartCellAccessor BuildAccessor(
        ViewportModel viewport,
        SheetId sheetId,
        GridRange? range = null)
    {
        var lookup = Resolve(viewport, sheetId, range);
        return BuildAccessor(lookup);
    }

    public static ChartLayoutRequestBuilder.ChartCellAccessor BuildAccessor(
        IReadOnlyDictionary<(uint Row, uint Column), ChartViewportCell> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var valueAccessor = BuildValueAccessor(lookup);
        return (uint row, uint column, out double value, out string displayText) =>
            valueAccessor(row, column, out _, out value, out displayText);
    }

    public static ChartLayoutRequestBuilder.ChartCellValueAccessor BuildValueAccessor(
        ViewportModel viewport,
        SheetId sheetId,
        GridRange? range = null)
    {
        var lookup = Resolve(viewport, sheetId, range);
        return BuildValueAccessor(lookup);
    }

    public static ChartLayoutRequestBuilder.ChartCellValueAccessor BuildValueAccessor(
        IReadOnlyDictionary<(uint Row, uint Column), ChartViewportCell> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return (
            uint row,
            uint column,
            out ScalarValue? rawValue,
            out double value,
            out string displayText) =>
        {
            if (lookup.TryGetValue((row, column), out var cell))
            {
                rawValue = cell.RawValue;
                displayText = cell.DisplayText;
                return ChartRenderPolicyPlanner.TryGetNumericValue(
                    cell.RawValue,
                    cell.DisplayText,
                    out value);
            }

            rawValue = null;
            value = 0;
            displayText = "";
            return false;
        };
    }

    private static int ResolveCapacity(ViewportModel viewport, GridRange? range)
    {
        if (range is not { } boundedRange)
            return SaturatingAdd(viewport.Cells.Count, viewport.ChartDataCells?.Count ?? 0);

        var rangeCellCount = GetRangeCellCount(boundedRange);
        var visibleCapacity = rangeCellCount > int.MaxValue
            ? viewport.Cells.Count
            : Math.Min(viewport.Cells.Count, (int)rangeCellCount);
        var chartDataCapacity = rangeCellCount > int.MaxValue
            ? viewport.ChartDataCells?.Count ?? 0
            : Math.Min(viewport.ChartDataCells?.Count ?? 0, (int)rangeCellCount);
        return SaturatingAdd(visibleCapacity, chartDataCapacity);
    }

    private static ulong GetRangeCellCount(GridRange range)
    {
        if (range.End.Row < range.Start.Row || range.End.Col < range.Start.Col)
            return 0;

        return ((ulong)range.End.Row - range.Start.Row + 1) *
            ((ulong)range.End.Col - range.Start.Col + 1);
    }

    private static int SaturatingAdd(int left, int right)
    {
        var sum = (long)left + right;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    private static bool IsInRange(uint row, uint column, GridRange? range) =>
        range is not { } boundedRange ||
        (row >= boundedRange.Start.Row &&
         row <= boundedRange.End.Row &&
         column >= boundedRange.Start.Col &&
         column <= boundedRange.End.Col);
}
