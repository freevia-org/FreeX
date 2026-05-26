using System.Windows;
using System.Windows.Threading;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public enum ObjectKind { None, Picture, Shape, TextBox }

public partial class GridView
{
    private enum ResizeTarget { None, Row, Column }

    private enum ObjectDragKind { None, Move, ResizeSE, ResizeE, ResizeS }

    private Guid _selectedObjectId;
    private ObjectKind _selectedObjectKind;
    private ObjectDragKind _objectDragKind;
    private Point _objectDragStartPos;
    private Rect _objectDragStartRect;
    private CellAddress _objectDragStartAnchor;

    private Dictionary<(uint Row, uint Col), GridRange> _mergeLookup = [];
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
            if (_mergeLookup.Count > 0)
                _mergeLookup.Clear();
            return;
        }

        _mergeLookup.Clear();

        foreach (var rowMetric in Viewport.RowMetrics)
        {
            var row = rowMetric.Row;
            foreach (var colMetric in Viewport.ColMetrics)
            {
                var col = colMetric.Col;
                foreach (var merge in MergedRegions)
                {
                    if (row >= merge.Start.Row &&
                        row <= merge.End.Row &&
                        col >= merge.Start.Col &&
                        col <= merge.End.Col)
                        _mergeLookup[(row, col)] = merge;
                }
            }
        }
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
