using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaAuditing;

public enum FormulaTraceArrowLayoutKind
{
    VisibleArrow,
    OffscreenMarker,
    CrossSheetMarker
}

public enum FormulaTraceMetricPositionMode
{
    MetricOffsets,
    SequentialVisibleMetrics
}

public enum FormulaTraceMarkerMode
{
    None,
    VisibleEndpoint
}

public readonly record struct FormulaTraceColor(byte R, byte G, byte B);

/// <summary>
/// Describes renderer-specific projection details without exposing renderer geometry types.
/// Header origins are already expressed in displayed pixels; metric sizes and offsets are scaled
/// by <see cref="ZoomFactor"/> inside the planner.
/// </summary>
public readonly record struct FormulaTraceViewportProjection(
    double RowHeaderWidth,
    double ColumnHeaderHeight,
    double ZoomFactor,
    double MinimumColumnWidth,
    double MinimumRowHeight,
    FormulaTraceMetricPositionMode PositionMode)
{
    public static FormulaTraceViewportProjection FromMetricOffsets(
        double rowHeaderWidth,
        double columnHeaderHeight) =>
        new(
            rowHeaderWidth,
            columnHeaderHeight,
            ZoomFactor: 1,
            MinimumColumnWidth: 0,
            MinimumRowHeight: 0,
            PositionMode: FormulaTraceMetricPositionMode.MetricOffsets);

    public static FormulaTraceViewportProjection FromSequentialVisibleMetrics(
        double rowHeaderWidth,
        double columnHeaderHeight,
        double zoomFactor,
        double minimumColumnWidth,
        double minimumRowHeight) =>
        new(
            rowHeaderWidth,
            columnHeaderHeight,
            zoomFactor,
            minimumColumnWidth,
            minimumRowHeight,
            FormulaTraceMetricPositionMode.SequentialVisibleMetrics);
}

/// <summary>Portable visual policy retained by each renderer profile for exact parity.</summary>
public readonly record struct FormulaTraceOverlayStyle(
    FormulaTraceColor PrecedentColor,
    FormulaTraceColor DependentColor,
    double StrokeWidth,
    double ArrowHeadLength,
    double ArrowHeadHalfWidth,
    double ArrowHeadMinimumLength,
    bool DrawArrowHeadAtMinimumLength,
    double SourceMarkerRadius,
    double EndpointMarkerRadius,
    double CrossSheetRingRadius,
    double MarkerHitRadius)
{
    public FormulaTraceColor ResolveColor(FormulaTraceArrowKind kind) =>
        kind == FormulaTraceArrowKind.Dependent ? DependentColor : PrecedentColor;
}

public readonly record struct FormulaTraceOverlayProfile(
    FormulaTraceMarkerMode MarkerMode,
    bool SuppressCoincidentVisibleArrows,
    FormulaTraceOverlayStyle Style);

/// <summary>
/// The profiles intentionally preserve the established renderer appearances. Their differences are
/// explicit shared policy rather than geometry and style constants hidden in native controls.
/// </summary>
public static class FormulaTraceOverlayProfiles
{
    private static readonly FormulaTraceColor WpfBlue = new(0, 102, 204);
    private static readonly FormulaTraceColor AvaloniaPrecedentGreen = new(0, 102, 51);
    private static readonly FormulaTraceColor AvaloniaDependentBlue = new(0, 86, 179);

    public static FormulaTraceOverlayProfile Wpf { get; } = new(
        FormulaTraceMarkerMode.VisibleEndpoint,
        SuppressCoincidentVisibleArrows: false,
        Style: new FormulaTraceOverlayStyle(
            WpfBlue,
            WpfBlue,
            StrokeWidth: 1.5,
            ArrowHeadLength: 8,
            ArrowHeadHalfWidth: 4,
            ArrowHeadMinimumLength: 0.1,
            DrawArrowHeadAtMinimumLength: false,
            SourceMarkerRadius: 0,
            EndpointMarkerRadius: 5,
            CrossSheetRingRadius: 8,
            MarkerHitRadius: 8));

    public static FormulaTraceOverlayProfile Avalonia { get; } = new(
        FormulaTraceMarkerMode.None,
        SuppressCoincidentVisibleArrows: true,
        Style: new FormulaTraceOverlayStyle(
            AvaloniaPrecedentGreen,
            AvaloniaDependentBlue,
            StrokeWidth: 1.5,
            ArrowHeadLength: 10,
            ArrowHeadHalfWidth: 5,
            ArrowHeadMinimumLength: 0.001,
            DrawArrowHeadAtMinimumLength: true,
            SourceMarkerRadius: 3,
            EndpointMarkerRadius: 0,
            CrossSheetRingRadius: 0,
            MarkerHitRadius: 0));
}

public readonly record struct FormulaTraceArrowLayout(
    LayoutPoint Start,
    LayoutPoint End,
    FormulaTraceArrowLayoutKind Kind = FormulaTraceArrowLayoutKind.VisibleArrow,
    CellAddress? NavigationTarget = null,
    FormulaTraceArrowKind ArrowKind = FormulaTraceArrowKind.Precedent);

public interface IFormulaTraceArrowLayoutConsumer
{
    void AcceptLayout(
        LayoutPoint start,
        LayoutPoint end,
        FormulaTraceArrowLayoutKind kind,
        CellAddress? navigationTarget,
        FormulaTraceArrowKind arrowKind);
}

/// <summary>
/// Resolves formula-auditing addresses against visible row/column metrics, selects visible arrow or
/// navigation-marker endpoints, and projects the result into portable overlay coordinates.
/// </summary>
public static class FormulaTraceOverlayPlanner
{
    public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        FormulaTraceViewportProjection projection,
        FormulaTraceOverlayProfile profile)
    {
        var consumer = new FormulaTraceArrowLayoutCollector(arrows.Count);
        VisitLayouts(viewport, arrows, sheetId, projection, profile, ref consumer);
        return consumer.ToLayouts();
    }

    public static void VisitLayouts<TConsumer>(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        FormulaTraceViewportProjection projection,
        FormulaTraceOverlayProfile profile,
        ref TConsumer consumer)
        where TConsumer : struct, IFormulaTraceArrowLayoutConsumer
    {
        var metrics = new FormulaTraceMetricLookup(viewport, projection);
        for (var i = 0; i < arrows.Count; i++)
        {
            var arrow = arrows[i];
            var fromOnCurrentSheet = arrow.From.Sheet.Equals(sheetId);
            var toOnCurrentSheet = arrow.To.Sheet.Equals(sheetId);
            var fromVisible = fromOnCurrentSheet && metrics.TryGetCellRect(arrow.From, out var fromRect);
            var toVisible = toOnCurrentSheet && metrics.TryGetCellRect(arrow.To, out var toRect);

            if (fromVisible && toVisible)
            {
                var start = fromRect.Center;
                var end = toRect.Center;
                if (!profile.SuppressCoincidentVisibleArrows || start != end)
                {
                    consumer.AcceptLayout(
                        start,
                        end,
                        FormulaTraceArrowLayoutKind.VisibleArrow,
                        navigationTarget: null,
                        arrowKind: arrow.Kind);
                }

                continue;
            }

            if (profile.MarkerMode != FormulaTraceMarkerMode.VisibleEndpoint)
                continue;

            var markerKind = fromOnCurrentSheet && toOnCurrentSheet
                ? FormulaTraceArrowLayoutKind.OffscreenMarker
                : FormulaTraceArrowLayoutKind.CrossSheetMarker;

            if (fromVisible)
            {
                var markerPoint = fromRect.Center;
                consumer.AcceptLayout(markerPoint, markerPoint, markerKind, arrow.To, arrow.Kind);
            }
            else if (toVisible)
            {
                var markerPoint = toRect.Center;
                consumer.AcceptLayout(markerPoint, markerPoint, markerKind, arrow.From, arrow.Kind);
            }
        }
    }

    public static CellAddress? HitTestMarker(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        FormulaTraceViewportProjection projection,
        FormulaTraceOverlayProfile profile,
        LayoutPoint position)
    {
        if (profile.MarkerMode != FormulaTraceMarkerMode.VisibleEndpoint || profile.Style.MarkerHitRadius <= 0)
            return null;

        var hitRadiusSquared = profile.Style.MarkerHitRadius * profile.Style.MarkerHitRadius;
        var metrics = new FormulaTraceMetricLookup(viewport, projection);
        for (var i = 0; i < arrows.Count; i++)
        {
            if (!TryGetMarkerHit(
                    in metrics,
                    arrows[i],
                    sheetId,
                    out var markerPoint,
                    out var navigationTarget) ||
                DistanceSquared(markerPoint, position) > hitRadiusSquared)
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
        private FormulaTraceArrowLayout[]? _layouts;
        private int _count;

        public void AcceptLayout(
            LayoutPoint start,
            LayoutPoint end,
            FormulaTraceArrowLayoutKind kind,
            CellAddress? navigationTarget,
            FormulaTraceArrowKind arrowKind)
        {
            _layouts ??= GC.AllocateUninitializedArray<FormulaTraceArrowLayout>(_capacity);
            _layouts[_count++] = new FormulaTraceArrowLayout(start, end, kind, navigationTarget, arrowKind);
        }

        public readonly IReadOnlyList<FormulaTraceArrowLayout> ToLayouts() =>
            _layouts is null ? [] :
            _count == _layouts.Length ? _layouts :
            CopyLayouts(_layouts, _count);
    }

    private static FormulaTraceArrowLayout[] CopyLayouts(FormulaTraceArrowLayout[] layouts, int count)
    {
        var copy = new FormulaTraceArrowLayout[count];
        Array.Copy(layouts, copy, count);
        return copy;
    }

    private static bool TryGetMarkerHit(
        in FormulaTraceMetricLookup metrics,
        FormulaTraceArrow arrow,
        SheetId sheetId,
        out LayoutPoint markerPoint,
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
            markerPoint = fromRect.Center;
            navigationTarget = arrow.To;
            return true;
        }

        markerPoint = toRect.Center;
        navigationTarget = arrow.From;
        return true;
    }

    private static double DistanceSquared(LayoutPoint first, LayoutPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private readonly struct FormulaTraceMetricLookup
    {
        private readonly IReadOnlyList<RowMetric> _rows;
        private readonly IReadOnlyList<ColMetric> _columns;
        private readonly FormulaTraceViewportProjection _projection;
        private readonly double[]? _sequentialRowTops;
        private readonly double[]? _sequentialColumnLefts;
        private readonly uint _firstRow;
        private readonly uint _lastRow;
        private readonly uint _firstCol;
        private readonly uint _lastCol;
        private readonly bool _hasRows;
        private readonly bool _hasColumns;

        public FormulaTraceMetricLookup(ViewportModel viewport, FormulaTraceViewportProjection projection)
        {
            _rows = viewport.RowMetrics;
            _columns = viewport.ColMetrics;
            _projection = projection;
            _hasRows = _rows.Count > 0;
            _hasColumns = _columns.Count > 0;
            _firstRow = _hasRows ? _rows[0].Row : 0;
            _lastRow = _hasRows ? _rows[^1].Row : 0;
            _firstCol = _hasColumns ? _columns[0].Col : 0;
            _lastCol = _hasColumns ? _columns[^1].Col : 0;

            if (projection.PositionMode == FormulaTraceMetricPositionMode.SequentialVisibleMetrics)
            {
                _sequentialRowTops = BuildSequentialRowTops(_rows, projection);
                _sequentialColumnLefts = BuildSequentialColumnLefts(_columns, projection);
            }
            else
            {
                _sequentialRowTops = null;
                _sequentialColumnLefts = null;
            }
        }

        public bool TryGetCellRect(CellAddress address, out LayoutRect rect)
        {
            var rowIndex = FindRowMetricIndex(address.Row);
            var columnIndex = FindColumnMetricIndex(address.Col);
            if (rowIndex < 0 || columnIndex < 0)
            {
                rect = default;
                return false;
            }

            var row = _rows[rowIndex];
            var column = _columns[columnIndex];
            var zoom = _projection.ZoomFactor;
            var left = _projection.RowHeaderWidth +
                (_sequentialColumnLefts is null ? column.LeftOffset * zoom : _sequentialColumnLefts[columnIndex]);
            var top = _projection.ColumnHeaderHeight +
                (_sequentialRowTops is null ? row.TopOffset * zoom : _sequentialRowTops[rowIndex]);
            var width = Math.Max(_projection.MinimumColumnWidth, column.Width) * zoom;
            var height = Math.Max(_projection.MinimumRowHeight, row.Height) * zoom;

            rect = new LayoutRect(left, top, width, height);
            return true;
        }

        private int FindRowMetricIndex(uint row) =>
            FindMetricIndex(_rows, row, _firstRow, _lastRow, static metric => metric.Row);

        private int FindColumnMetricIndex(uint column) =>
            FindMetricIndex(_columns, column, _firstCol, _lastCol, static metric => metric.Col);
    }

    private static double[] BuildSequentialRowTops(
        IReadOnlyList<RowMetric> rows,
        FormulaTraceViewportProjection projection)
    {
        var positions = GC.AllocateUninitializedArray<double>(rows.Count);
        var top = 0.0;
        for (var i = 0; i < rows.Count; i++)
        {
            positions[i] = top;
            top += Math.Max(projection.MinimumRowHeight, rows[i].Height) * projection.ZoomFactor;
        }

        return positions;
    }

    private static double[] BuildSequentialColumnLefts(
        IReadOnlyList<ColMetric> columns,
        FormulaTraceViewportProjection projection)
    {
        var positions = GC.AllocateUninitializedArray<double>(columns.Count);
        var left = 0.0;
        for (var i = 0; i < columns.Count; i++)
        {
            positions[i] = left;
            left += Math.Max(projection.MinimumColumnWidth, columns[i].Width) * projection.ZoomFactor;
        }

        return positions;
    }

    private static int FindMetricIndex<TMetric>(
        IReadOnlyList<TMetric> metrics,
        uint address,
        uint first,
        uint last,
        Func<TMetric, uint> getAddress)
    {
        if (metrics.Count == 0 || address < first || address > last)
            return -1;

        var directIndex = address - first;
        if (directIndex < (uint)metrics.Count && getAddress(metrics[(int)directIndex]) == address)
            return (int)directIndex;

        var low = 0;
        var high = metrics.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metricAddress = getAddress(metrics[mid]);
            if (metricAddress == address)
                return mid;

            if (metricAddress < address)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return -1;
    }
}

public readonly record struct FormulaTraceArrowHeadGeometry(
    bool IsVisible,
    LayoutPoint Tip,
    LayoutPoint Left,
    LayoutPoint Right);

/// <summary>Computes native-agnostic arrowhead vertices for formula trace overlays.</summary>
public static class FormulaTraceOverlayGeometryPlanner
{
    public static FormulaTraceArrowHeadGeometry CalculateArrowHead(
        LayoutPoint start,
        LayoutPoint end,
        FormulaTraceOverlayStyle style)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < style.ArrowHeadMinimumLength ||
            (!style.DrawArrowHeadAtMinimumLength && length == style.ArrowHeadMinimumLength))
            return default;

        var unitX = dx / length;
        var unitY = dy / length;
        var baseX = end.X - unitX * style.ArrowHeadLength;
        var baseY = end.Y - unitY * style.ArrowHeadLength;
        var perpendicularX = -unitY;
        var perpendicularY = unitX;

        return new FormulaTraceArrowHeadGeometry(
            IsVisible: true,
            Tip: end,
            Left: new LayoutPoint(
                baseX + perpendicularX * style.ArrowHeadHalfWidth,
                baseY + perpendicularY * style.ArrowHeadHalfWidth),
            Right: new LayoutPoint(
                baseX - perpendicularX * style.ArrowHeadHalfWidth,
                baseY - perpendicularY * style.ArrowHeadHalfWidth));
    }
}
