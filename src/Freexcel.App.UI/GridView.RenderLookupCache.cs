using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private sealed record RenderCellLookupCache(
        ViewportModel Viewport,
        Dictionary<(uint Row, uint Col), CellStyle> Styles,
        Dictionary<uint, RowMetric> Rows,
        Dictionary<uint, ColMetric> Columns);

    private sealed record OccupiedCellLookupCache(
        ViewportModel Viewport,
        CellAddress? EditingCell,
        HashSet<(uint Row, uint Col)> Occupied);
}
