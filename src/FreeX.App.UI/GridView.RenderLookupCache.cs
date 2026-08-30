using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private sealed record RenderCellLookupCache(
        IReadOnlyList<DisplayCell> Cells,
        IReadOnlyList<RowMetric> RowMetrics,
        IReadOnlyList<ColMetric> ColMetrics,
        Dictionary<(uint Row, uint Col), CellStyle> Styles,
        Dictionary<(uint Row, uint Col), CellStyle> BorderStyles,
        IReadOnlyList<RenderBorderCell> BorderCells,
        Dictionary<uint, RowMetric> Rows,
        Dictionary<uint, ColMetric> Columns);

    private readonly record struct RenderBorderCell(
        uint Row,
        uint Col,
        CellStyle Style);

    private sealed record RenderMetricLookupCache(
        IReadOnlyList<RowMetric> RowMetrics,
        IReadOnlyList<ColMetric> ColMetrics,
        Dictionary<uint, RowMetric> Rows,
        Dictionary<uint, ColMetric> Columns);

    private sealed record OccupiedCellLookupCache(
        IReadOnlyList<DisplayCell> Cells,
        CellAddress? EditingCell,
        HashSet<(uint Row, uint Col)> Occupied);

    private sealed record PageBreakLookupCache(
        IReadOnlyCollection<uint> Source,
        int Count,
        ulong Fingerprint,
        IReadOnlySet<uint> Lookup);
}
