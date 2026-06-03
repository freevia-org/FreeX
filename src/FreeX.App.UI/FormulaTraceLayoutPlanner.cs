using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public interface IFormulaTraceArrowLayoutConsumer
{
    void AcceptLayout(
        Point start,
        Point end,
        FormulaTraceArrowLayoutKind kind,
        CellAddress? navigationTarget);
}

public static class FormulaTraceLayoutPlanner
{
    public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId)
    {
        var consumer = new FormulaTraceArrowLayoutCollector(arrows.Count);
        VisitLayouts(viewport, arrows, sheetId, ref consumer);
        return consumer.ToLayouts();
    }

    public static void VisitLayouts<TConsumer>(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        ref TConsumer consumer)
        where TConsumer : struct, IFormulaTraceArrowLayoutConsumer
    {
        var metrics = new FormulaTraceMetricLookup(viewport, GridView.CalculateRowHeaderWidth(viewport));
        for (var i = 0; i < arrows.Count; i++)
        {
            var arrow = arrows[i];
            var fromOnCurrentSheet = arrow.From.Sheet.Equals(sheetId);
            var toOnCurrentSheet = arrow.To.Sheet.Equals(sheetId);
            var fromVisible = fromOnCurrentSheet && metrics.TryGetCellRect(arrow.From, out var fromRect);
            var toVisible = toOnCurrentSheet && metrics.TryGetCellRect(arrow.To, out var toRect);

            if (fromVisible && toVisible)
            {
                consumer.AcceptLayout(
                    CenterOf(fromRect),
                    CenterOf(toRect),
                    FormulaTraceArrowLayoutKind.VisibleArrow,
                    null);
                continue;
            }

            var markerKind = fromOnCurrentSheet && toOnCurrentSheet
                ? FormulaTraceArrowLayoutKind.OffscreenMarker
                : FormulaTraceArrowLayoutKind.CrossSheetMarker;

            if (fromVisible)
            {
                var markerPoint = CenterOf(fromRect);
                consumer.AcceptLayout(markerPoint, markerPoint, markerKind, arrow.To);
            }
            else if (toVisible)
            {
                var markerPoint = CenterOf(toRect);
                consumer.AcceptLayout(markerPoint, markerPoint, markerKind, arrow.From);
            }
        }
    }

    public static CellAddress? HitTestMarker(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        Point pos)
    {
        const double hitRadius = 8;
        var metrics = new FormulaTraceMetricLookup(viewport, GridView.CalculateRowHeaderWidth(viewport));
        for (var i = 0; i < arrows.Count; i++)
        {
            var arrow = arrows[i];
            if (!TryGetMarkerHit(
                in metrics,
                arrow,
                sheetId,
                out var markerPoint,
                out var navigationTarget) ||
                (markerPoint - pos).Length > hitRadius)
            {
                continue;
            }

            return navigationTarget;
        }

        return null;
    }

    private struct FormulaTraceArrowLayoutCollector(int capacity) : IFormulaTraceArrowLayoutConsumer
    {
        private readonly int _capacity = capacity;
        private List<FormulaTraceArrowLayout>? _layouts;

        public void AcceptLayout(
            Point start,
            Point end,
            FormulaTraceArrowLayoutKind kind,
            CellAddress? navigationTarget)
        {
            _layouts ??= new List<FormulaTraceArrowLayout>(_capacity);
            _layouts.Add(new FormulaTraceArrowLayout(start, end, kind, navigationTarget));
        }

        public readonly IReadOnlyList<FormulaTraceArrowLayout> ToLayouts() =>
            _layouts is { Count: > 0 } layouts ? layouts : [];
    }

    private static Point CenterOf(Rect rect) =>
        new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private readonly struct FormulaTraceMetricLookup
    {
        private readonly IReadOnlyList<RowMetric> _rows;
        private readonly IReadOnlyList<ColMetric> _columns;
        private readonly RowMetric[]? _rowArray;
        private readonly ColMetric[]? _colArray;
        private readonly List<RowMetric>? _rowList;
        private readonly List<ColMetric>? _colList;
        private readonly double _rowHeaderWidth;
        private readonly uint _firstRow;
        private readonly uint _lastRow;
        private readonly uint _firstCol;
        private readonly uint _lastCol;
        private readonly bool _hasRows;
        private readonly bool _hasColumns;

        public FormulaTraceMetricLookup(ViewportModel viewport, double rowHeaderWidth)
        {
            _rows = viewport.RowMetrics;
            _columns = viewport.ColMetrics;
            _rowArray = _rows as RowMetric[];
            _colArray = _columns as ColMetric[];
            _rowList = _rows as List<RowMetric>;
            _colList = _columns as List<ColMetric>;
            _rowHeaderWidth = rowHeaderWidth;
            _hasRows = _rows.Count > 0;
            _hasColumns = _columns.Count > 0;
            _firstRow = _hasRows ? _rows[0].Row : 0;
            _lastRow = _hasRows ? _rows[^1].Row : 0;
            _firstCol = _hasColumns ? _columns[0].Col : 0;
            _lastCol = _hasColumns ? _columns[^1].Col : 0;
        }

        public bool TryGetCellRect(CellAddress address, out Rect rect)
        {
            var row = FindRowMetric(address.Row);
            var col = FindColMetric(address.Col);
            if (row is null || col is null)
            {
                rect = Rect.Empty;
                return false;
            }

            rect = new Rect(
                col.LeftOffset + _rowHeaderWidth,
                row.TopOffset + GridView.ColHeaderHeight,
                col.Width,
                row.Height);
            return true;
        }

        private RowMetric? FindRowMetric(uint row)
        {
            if (!_hasRows || row < _firstRow || row > _lastRow)
                return null;

            if (_rowArray is not null)
                return FormulaTraceLayoutPlanner.FindRowMetric(_rowArray, row, _firstRow);

            if (_rowList is not null)
                return FormulaTraceLayoutPlanner.FindRowMetric(_rowList, row, _firstRow);

            return FormulaTraceLayoutPlanner.FindRowMetric(_rows, row, _firstRow);
        }

        private ColMetric? FindColMetric(uint col)
        {
            if (!_hasColumns || col < _firstCol || col > _lastCol)
                return null;

            if (_colArray is not null)
                return FormulaTraceLayoutPlanner.FindColMetric(_colArray, col, _firstCol);

            if (_colList is not null)
                return FormulaTraceLayoutPlanner.FindColMetric(_colList, col, _firstCol);

            return FormulaTraceLayoutPlanner.FindColMetric(_columns, col, _firstCol);
        }
    }

    private static bool TryGetMarkerHit(
        in FormulaTraceMetricLookup metrics,
        FormulaTraceArrow arrow,
        SheetId sheetId,
        out Point markerPoint,
        out CellAddress navigationTarget)
    {
        var fromOnCurrentSheet = arrow.From.Sheet.Equals(sheetId);
        var toOnCurrentSheet = arrow.To.Sheet.Equals(sheetId);
        var fromVisible = fromOnCurrentSheet && metrics.TryGetCellRect(arrow.From, out var fromRect);
        var toVisible = toOnCurrentSheet && metrics.TryGetCellRect(arrow.To, out var toRect);

        if (fromVisible == toVisible)
        {
            markerPoint = default;
            navigationTarget = default;
            return false;
        }

        if (fromVisible)
        {
            markerPoint = CenterOf(fromRect);
            navigationTarget = arrow.To;
            return true;
        }

        markerPoint = CenterOf(toRect);
        navigationTarget = arrow.From;
        return true;
    }

    private static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> rows, uint row, uint firstRow)
    {
        var index = row - firstRow;
        if (index < (uint)rows.Count)
        {
            var indexedMetric = rows[(int)index];
            if (indexedMetric.Row == row)
                return indexedMetric;
        }

        var low = 0;
        var high = rows.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = rows[mid];

            if (metric.Row == row)
                return metric;

            if (metric.Row < row)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static RowMetric? FindRowMetric(RowMetric[] rows, uint row, uint firstRow)
    {
        var index = row - firstRow;
        if (index < (uint)rows.Length)
        {
            var indexedMetric = rows[(int)index];
            if (indexedMetric.Row == row)
                return indexedMetric;
        }

        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = rows[mid];

            if (metric.Row == row)
                return metric;

            if (metric.Row < row)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static RowMetric? FindRowMetric(List<RowMetric> rows, uint row, uint firstRow)
    {
        var index = row - firstRow;
        if (index < (uint)rows.Count)
        {
            var indexedMetric = rows[(int)index];
            if (indexedMetric.Row == row)
                return indexedMetric;
        }

        var low = 0;
        var high = rows.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = rows[mid];

            if (metric.Row == row)
                return metric;

            if (metric.Row < row)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static ColMetric? FindColMetric(IReadOnlyList<ColMetric> columns, uint col, uint firstCol)
    {
        var index = col - firstCol;
        if (index < (uint)columns.Count)
        {
            var indexedMetric = columns[(int)index];
            if (indexedMetric.Col == col)
                return indexedMetric;
        }

        var low = 0;
        var high = columns.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = columns[mid];

            if (metric.Col == col)
                return metric;

            if (metric.Col < col)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static ColMetric? FindColMetric(ColMetric[] columns, uint col, uint firstCol)
    {
        var index = col - firstCol;
        if (index < (uint)columns.Length)
        {
            var indexedMetric = columns[(int)index];
            if (indexedMetric.Col == col)
                return indexedMetric;
        }

        var low = 0;
        var high = columns.Length - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = columns[mid];

            if (metric.Col == col)
                return metric;

            if (metric.Col < col)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }

    private static ColMetric? FindColMetric(List<ColMetric> columns, uint col, uint firstCol)
    {
        var index = col - firstCol;
        if (index < (uint)columns.Count)
        {
            var indexedMetric = columns[(int)index];
            if (indexedMetric.Col == col)
                return indexedMetric;
        }

        var low = 0;
        var high = columns.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = columns[mid];

            if (metric.Col == col)
                return metric;

            if (metric.Col < col)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return null;
    }
}
