using System.Windows;
using System.Windows.Threading;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public enum ObjectKind { None, Picture, Shape, TextBox }

public partial class GridView
{
    private enum ResizeTarget { None, Row, Column }

    private Guid _selectedObjectId;
    private ObjectKind _selectedObjectKind;
    private ObjectDragKind _objectDragKind;
    private Point _objectDragStartPos;
    private Rect _objectDragStartRect;
    private CellAddress _objectDragStartAnchor;

    private Dictionary<(uint Row, uint Col), GridRange> _mergeLookup = [];
    private bool _mergeLookupCacheValid;
    private int _mergeLookupMergedRegionCount;
    private long _mergeLookupMergedRegionSignature;
    private int _mergeLookupVisibleRowCount;
    private int _mergeLookupVisibleColumnCount;
    private long _mergeLookupVisibleRowSignature;
    private long _mergeLookupVisibleColumnSignature;
    private uint _mergeLookupFirstVisibleRow;
    private uint _mergeLookupLastVisibleRow;
    private uint _mergeLookupFirstVisibleColumn;
    private uint _mergeLookupLastVisibleColumn;
    private ResizeTarget _resizeTarget = ResizeTarget.None;
    private uint _resizeIndex;
    private double _resizeDragStart;
    private double _resizeSizeStart;
    private double _resizeLinePos;
    private bool _autofillDragging;
    private GridRange? _autofillSourceRange;
    private CellAddress? _autofillTarget;
    private WorksheetPageMarginEdge? _marginDragEdge;
    private SplitDividerHandle _splitDividerDragHandle = SplitDividerHandle.None;
    private bool _splitPaneScrollbarDragging;
    private SplitPaneScrollbar? _splitPaneScrollbarDragSource;
    private double _splitPaneScrollbarDragPointerOffset;

    private void RebuildMergeLookup()
    {
        if (MergedRegions is not { Count: > 0 } || Viewport == null)
        {
            ClearMergeLookupCache();
            return;
        }

        var rowCount = Viewport.RowMetrics.Count;
        var columnCount = Viewport.ColMetrics.Count;
        var firstRow = rowCount > 0 ? Viewport.RowMetrics[0].Row : 0;
        var lastRow = rowCount > 0 ? Viewport.RowMetrics[^1].Row : 0;
        var firstColumn = columnCount > 0 ? Viewport.ColMetrics[0].Col : 0;
        var lastColumn = columnCount > 0 ? Viewport.ColMetrics[^1].Col : 0;
        var mergedRegionSignature = CalculateMergedRegionSignature(MergedRegions);
        var visibleRowSignature = CalculateVisibleRowSignature(Viewport.RowMetrics);
        var visibleColumnSignature = CalculateVisibleColumnSignature(Viewport.ColMetrics);

        if (_mergeLookupCacheValid &&
            _mergeLookupMergedRegionCount == MergedRegions.Count &&
            _mergeLookupMergedRegionSignature == mergedRegionSignature &&
            _mergeLookupVisibleRowCount == rowCount &&
            _mergeLookupVisibleColumnCount == columnCount &&
            _mergeLookupVisibleRowSignature == visibleRowSignature &&
            _mergeLookupVisibleColumnSignature == visibleColumnSignature &&
            _mergeLookupFirstVisibleRow == firstRow &&
            _mergeLookupLastVisibleRow == lastRow &&
            _mergeLookupFirstVisibleColumn == firstColumn &&
            _mergeLookupLastVisibleColumn == lastColumn)
        {
            return;
        }

        _mergeLookup.Clear();
        var mergesByVisibleRow = BuildVisibleRowMergeLookup();

        foreach (var rowMetric in Viewport.RowMetrics)
        {
            var row = rowMetric.Row;
            if (!mergesByVisibleRow.TryGetValue(row, out var rowMerges))
                continue;

            foreach (var colMetric in Viewport.ColMetrics)
            {
                var col = colMetric.Col;
                foreach (var merge in rowMerges)
                {
                    if (col >= merge.Start.Col && col <= merge.End.Col)
                        _mergeLookup[(row, col)] = merge;
                }
            }
        }

        _mergeLookupCacheValid = true;
        _mergeLookupMergedRegionCount = MergedRegions.Count;
        _mergeLookupMergedRegionSignature = mergedRegionSignature;
        _mergeLookupVisibleRowCount = rowCount;
        _mergeLookupVisibleColumnCount = columnCount;
        _mergeLookupVisibleRowSignature = visibleRowSignature;
        _mergeLookupVisibleColumnSignature = visibleColumnSignature;
        _mergeLookupFirstVisibleRow = firstRow;
        _mergeLookupLastVisibleRow = lastRow;
        _mergeLookupFirstVisibleColumn = firstColumn;
        _mergeLookupLastVisibleColumn = lastColumn;
    }

    private void ClearMergeLookupCache()
    {
        if (_mergeLookup.Count > 0)
            _mergeLookup.Clear();

        _mergeLookupCacheValid = false;
        _mergeLookupMergedRegionCount = 0;
        _mergeLookupMergedRegionSignature = 0;
        _mergeLookupVisibleRowCount = 0;
        _mergeLookupVisibleColumnCount = 0;
        _mergeLookupVisibleRowSignature = 0;
        _mergeLookupVisibleColumnSignature = 0;
        _mergeLookupFirstVisibleRow = 0;
        _mergeLookupLastVisibleRow = 0;
        _mergeLookupFirstVisibleColumn = 0;
        _mergeLookupLastVisibleColumn = 0;
    }

    private static long CalculateMergedRegionSignature(IReadOnlyList<GridRange> mergedRegions)
    {
        unchecked
        {
            var signature = 17L;
            foreach (var region in mergedRegions)
            {
                signature = signature * 31 + region.Start.Row;
                signature = signature * 31 + region.Start.Col;
                signature = signature * 31 + region.End.Row;
                signature = signature * 31 + region.End.Col;
            }

            return signature;
        }
    }

    private static long CalculateVisibleRowSignature(IReadOnlyList<RowMetric> rows)
    {
        unchecked
        {
            var signature = 17L;
            foreach (var row in rows)
                signature = signature * 31 + row.Row;

            return signature;
        }
    }

    private static long CalculateVisibleColumnSignature(IReadOnlyList<ColMetric> columns)
    {
        unchecked
        {
            var signature = 17L;
            foreach (var column in columns)
                signature = signature * 31 + column.Col;

            return signature;
        }
    }

    private Dictionary<uint, List<GridRange>> BuildVisibleRowMergeLookup()
    {
        var result = new Dictionary<uint, List<GridRange>>();
        foreach (var rowMetric in Viewport!.RowMetrics)
        {
            var row = rowMetric.Row;
            foreach (var merge in MergedRegions!)
            {
                if (row < merge.Start.Row || row > merge.End.Row)
                    continue;

                if (!result.TryGetValue(row, out var rowMerges))
                {
                    rowMerges = [];
                    result[row] = rowMerges;
                }

                rowMerges.Add(merge);
            }
        }

        return result;
    }

    private DispatcherTimer? _marchTimer;
    private double _marchOffset;

    private void StartMarchTimer()
    {
        if (_marchTimer != null) return;
        _marchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _marchTimer.Tick += (_, _) =>
        {
            _marchOffset = (_marchOffset + 1.5) % 8.0;
            InvalidateVisual();
        };
        _marchTimer.Start();
    }

    private void StopMarchTimer()
    {
        _marchTimer?.Stop();
        _marchTimer = null;
        _marchOffset = 0;
        InvalidateVisual();
    }
}
