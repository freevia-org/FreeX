using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.IO;
using Freexcel.Core.Model;
using CellHAlign  = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign  = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.UI;

/// <summary>
/// A high-performance, virtualized spreadsheet grid control.
/// Renders only the visible portion of the workbook using low-level DrawingContext.
/// </summary>
public class GridView : FrameworkElement
{
    public GridView() { Focusable = true; FocusVisualStyle = null; }

    // Column header strip height (horizontal row of A, B, C … letters)
    public const double ColHeaderHeight = 18;
    // Row header strip width (vertical column of 1, 2, 3 … numbers)
    public const double RowHeaderWidth  = 30;

    private const double ResizeHitZone = 4;
    private const double MinCellSize   = 5;
    private const double PageMarginGuideHitZone = 5;

    private static readonly Typeface DefaultTypeface       = new("Segoe UI");
    private static readonly Brush    GridLineBrush         = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly Brush    TextBrush             = Brushes.Black;
    private static readonly Brush    HeaderBackgroundBrush = new SolidColorBrush(Color.FromRgb(242, 242, 242));
    private static readonly Brush    HeaderHighlightBrush  = new SolidColorBrush(Color.FromRgb(218, 232, 218));
    private static readonly Pen      GridPen               = new(GridLineBrush, 1);
    private static readonly Brush    SelectionBrush        = new SolidColorBrush(Color.FromArgb(32, 33, 115, 70));
    private static readonly Pen      SelectionPen          = new(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 2);
    private static readonly Pen      ResizeLinePen         = MakeResizeLinePen();
    private static readonly Pen      FreezePen             = MakeFreezePen();
    private static readonly Brush    PageBreakPreviewBrush = new SolidColorBrush(Color.FromArgb(28, 0, 103, 192));
    private static readonly Pen      PageBreakPen          = MakePageBreakPen();
    private static readonly Pen      PageLayoutPen         = MakePageLayoutPen();
    private static readonly Pen      PageMarginGuidePen    = MakePageMarginGuidePen();
    private static readonly Pen      SplitPanePen          = MakeSplitPanePen();

    private static Pen MakeResizeLinePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeFreezePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 200)), 2);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageBreakPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0, 103, 192)), 2)
        {
            DashStyle = new DashStyle([6.0, 4.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageLayoutPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.5);
        pen.Freeze();
        return pen;
    }

    private static Pen MakePageMarginGuidePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(80, 150, 220)), 1)
        {
            DashStyle = new DashStyle([3.0, 3.0], 0)
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakeSplitPanePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 3);
        pen.Freeze();
        return pen;
    }

    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(nameof(Viewport), typeof(ViewportModel), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public ViewportModel? Viewport
    {
        get => (ViewportModel?)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public static readonly DependencyProperty SelectedRangeProperty =
        DependencyProperty.Register(nameof(SelectedRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? SelectedRange
    {
        get => (GridRange?)GetValue(SelectedRangeProperty);
        set => SetValue(SelectedRangeProperty, value);
    }

    public static readonly DependencyProperty SelectedRangesProperty =
        DependencyProperty.Register(nameof(SelectedRanges), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<GridRange>? SelectedRanges
    {
        get => (IReadOnlyList<GridRange>?)GetValue(SelectedRangesProperty);
        set => SetValue(SelectedRangesProperty, value);
    }

    public static readonly DependencyProperty ChartsProperty =
        DependencyProperty.Register(nameof(Charts), typeof(IReadOnlyList<ChartModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ChartModel>? Charts
    {
        get => (IReadOnlyList<ChartModel>?)GetValue(ChartsProperty);
        set => SetValue(ChartsProperty, value);
    }

    public static readonly DependencyProperty TextBoxesProperty =
        DependencyProperty.Register(nameof(TextBoxes), typeof(IReadOnlyList<TextBoxModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<TextBoxModel>? TextBoxes
    {
        get => (IReadOnlyList<TextBoxModel>?)GetValue(TextBoxesProperty);
        set => SetValue(TextBoxesProperty, value);
    }

    public static readonly DependencyProperty DrawingShapesProperty =
        DependencyProperty.Register(nameof(DrawingShapes), typeof(IReadOnlyList<DrawingShapeModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<DrawingShapeModel>? DrawingShapes
    {
        get => (IReadOnlyList<DrawingShapeModel>?)GetValue(DrawingShapesProperty);
        set => SetValue(DrawingShapesProperty, value);
    }

    public static readonly DependencyProperty PicturesProperty =
        DependencyProperty.Register(nameof(Pictures), typeof(IReadOnlyList<PictureModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<PictureModel>? Pictures
    {
        get => (IReadOnlyList<PictureModel>?)GetValue(PicturesProperty);
        set => SetValue(PicturesProperty, value);
    }

    public static readonly DependencyProperty SparklinesProperty =
        DependencyProperty.Register(nameof(Sparklines), typeof(IReadOnlyList<SparklineModel>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<SparklineModel>? Sparklines
    {
        get => (IReadOnlyList<SparklineModel>?)GetValue(SparklinesProperty);
        set => SetValue(SparklinesProperty, value);
    }

    public static readonly DependencyProperty SparklineValuesProperty =
        DependencyProperty.Register(nameof(SparklineValues), typeof(IReadOnlyDictionary<Guid, IReadOnlyList<double>>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyDictionary<Guid, IReadOnlyList<double>>? SparklineValues
    {
        get => (IReadOnlyDictionary<Guid, IReadOnlyList<double>>?)GetValue(SparklineValuesProperty);
        set => SetValue(SparklineValuesProperty, value);
    }

    public static readonly DependencyProperty MergedRegionsProperty =
        DependencyProperty.Register(nameof(MergedRegions), typeof(IReadOnlyList<GridRange>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<GridRange>? MergedRegions
    {
        get => (IReadOnlyList<GridRange>?)GetValue(MergedRegionsProperty);
        set => SetValue(MergedRegionsProperty, value);
    }

    public static readonly DependencyProperty ShowGridLinesProperty =
        DependencyProperty.Register(nameof(ShowGridLines), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowGridLines
    {
        get => (bool)GetValue(ShowGridLinesProperty);
        set => SetValue(ShowGridLinesProperty, value);
    }

    public static readonly DependencyProperty ShowHeadersProperty =
        DependencyProperty.Register(nameof(ShowHeaders), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowHeaders
    {
        get => (bool)GetValue(ShowHeadersProperty);
        set => SetValue(ShowHeadersProperty, value);
    }

    public static readonly DependencyProperty UseR1C1ReferenceStyleProperty =
        DependencyProperty.Register(nameof(UseR1C1ReferenceStyle), typeof(bool), typeof(GridView),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool UseR1C1ReferenceStyle
    {
        get => (bool)GetValue(UseR1C1ReferenceStyleProperty);
        set => SetValue(UseR1C1ReferenceStyleProperty, value);
    }

    public static readonly DependencyProperty ZoomFactorProperty =
        DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(GridView),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double ZoomFactor
    {
        get => (double)GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    public static readonly DependencyProperty WorksheetViewModeProperty =
        DependencyProperty.Register(nameof(WorksheetViewMode), typeof(WorksheetViewMode), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetViewMode.Normal, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetViewMode WorksheetViewMode
    {
        get => (WorksheetViewMode)GetValue(WorksheetViewModeProperty);
        set => SetValue(WorksheetViewModeProperty, value);
    }

    public static readonly DependencyProperty RowPageBreaksProperty =
        DependencyProperty.Register(nameof(RowPageBreaks), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyCollection<uint>? RowPageBreaks
    {
        get => (IReadOnlyCollection<uint>?)GetValue(RowPageBreaksProperty);
        set => SetValue(RowPageBreaksProperty, value);
    }

    public static readonly DependencyProperty ColumnPageBreaksProperty =
        DependencyProperty.Register(nameof(ColumnPageBreaks), typeof(IReadOnlyCollection<uint>), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyCollection<uint>? ColumnPageBreaks
    {
        get => (IReadOnlyCollection<uint>?)GetValue(ColumnPageBreaksProperty);
        set => SetValue(ColumnPageBreaksProperty, value);
    }

    public static readonly DependencyProperty PrintAreaProperty =
        DependencyProperty.Register(nameof(PrintArea), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GridRange? PrintArea
    {
        get => (GridRange?)GetValue(PrintAreaProperty);
        set => SetValue(PrintAreaProperty, value);
    }

    public static readonly DependencyProperty SplitRowProperty =
        DependencyProperty.Register(nameof(SplitRow), typeof(uint?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public uint? SplitRow
    {
        get => (uint?)GetValue(SplitRowProperty);
        set => SetValue(SplitRowProperty, value);
    }

    public static readonly DependencyProperty SplitColumnProperty =
        DependencyProperty.Register(nameof(SplitColumn), typeof(uint?), typeof(GridView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public uint? SplitColumn
    {
        get => (uint?)GetValue(SplitColumnProperty);
        set => SetValue(SplitColumnProperty, value);
    }

    public static readonly DependencyProperty PageMarginsProperty =
        DependencyProperty.Register(nameof(PageMargins), typeof(WorksheetPageMargins), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPageMargins.Narrow, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPageMargins PageMargins
    {
        get => (WorksheetPageMargins)GetValue(PageMarginsProperty);
        set => SetValue(PageMarginsProperty, value);
    }

    public static readonly DependencyProperty PageOrientationProperty =
        DependencyProperty.Register(nameof(PageOrientation), typeof(WorksheetPageOrientation), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPageOrientation.Portrait, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPageOrientation PageOrientation
    {
        get => (WorksheetPageOrientation)GetValue(PageOrientationProperty);
        set => SetValue(PageOrientationProperty, value);
    }

    public static readonly DependencyProperty PaperSizeProperty =
        DependencyProperty.Register(nameof(PaperSize), typeof(WorksheetPaperSize), typeof(GridView),
            new FrameworkPropertyMetadata(WorksheetPaperSize.A4, FrameworkPropertyMetadataOptions.AffectsRender));
    public WorksheetPaperSize PaperSize
    {
        get => (WorksheetPaperSize)GetValue(PaperSizeProperty);
        set => SetValue(PaperSizeProperty, value);
    }

    // ClipboardRange: when set, draws marching ants around this range
    public static readonly DependencyProperty ClipboardRangeProperty =
        DependencyProperty.Register(nameof(ClipboardRange), typeof(GridRange?), typeof(GridView),
            new FrameworkPropertyMetadata(null, OnClipboardRangeChanged));
    public GridRange? ClipboardRange
    {
        get => (GridRange?)GetValue(ClipboardRangeProperty);
        set => SetValue(ClipboardRangeProperty, value);
    }

    private static void OnClipboardRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gv = (GridView)d;
        if (e.NewValue != null)
            gv.StartMarchTimer();
        else
            gv.StopMarchTimer();
    }

    // ── Merge lookup (rebuilt once per render pass, O(1) per cell) ───────────

    private Dictionary<(uint Row, uint Col), GridRange> _mergeLookup = [];

    private void RebuildMergeLookup()
    {
        _mergeLookup.Clear();
        if (MergedRegions == null || Viewport == null) return;

        var visRows = new HashSet<uint>(Viewport.RowMetrics.Select(r => r.Row));
        var visCols = new HashSet<uint>(Viewport.ColMetrics.Select(c => c.Col));

        foreach (var merge in MergedRegions)
        {
            for (uint r = merge.Start.Row; r <= merge.End.Row; r++)
            {
                if (!visRows.Contains(r)) continue;
                for (uint c = merge.Start.Col; c <= merge.End.Col; c++)
                {
                    if (visCols.Contains(c))
                        _mergeLookup[(r, c)] = merge;
                }
            }
        }
    }

    // ── Marching ants ─────────────────────────────────────────────────────────

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

    // ── Resize drag state ─────────────────────────────────────────────────────

    private enum ResizeTarget { None, Row, Column }
    private ResizeTarget _resizeTarget = ResizeTarget.None;
    private uint   _resizeIndex;
    private double _resizeDragStart;
    private double _resizeSizeStart;
    private double _resizeLinePos;

    // Autofill drag state
    private bool      _autofillDragging;
    private GridRange? _autofillSourceRange;
    private CellAddress? _autofillTarget;

    // Page Layout margin-guide drag state
    private WorksheetPageMarginEdge? _marginDragEdge;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired while the user drags a column border (real-time).</summary>
    public event Action<uint, double>? ColumnResizing;
    /// <summary>Fired when the user releases after resizing a column.</summary>
    public event Action<uint, double>? ColumnResized;

    /// <summary>Fired while the user drags a row border (real-time).</summary>
    public event Action<uint, double>? RowResizing;
    /// <summary>Fired when the user releases after resizing a row.</summary>
    public event Action<uint, double>? RowResized;

    /// <summary>Fired when the user drags the autofill handle and releases.</summary>
    public event Action<GridRange, GridRange>? AutofillRequested;

    /// <summary>Fired on right mouse button down with the clicked cell address.</summary>
    public event Action<CellAddress, System.Windows.Point>? ContextMenuRequested;

    /// <summary>Fired when the user releases after dragging a Page Layout margin guide.</summary>
    public event Action<WorksheetPageMargins>? PageMarginsChanged;

    // ── OnRender ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        RebuildMergeLookup();
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));

        RenderHeaders(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderWorksheetViewOverlay(dc);
        RenderSparklines(dc);
        RenderSelection(dc);
        RenderAutofillPreview(dc);
        RenderMarchingAnts(dc);
        RenderFreezeDivider(dc);
        RenderSplitDivider(dc);
        RenderResizeLine(dc);
        RenderCharts(dc);
        RenderDrawingShapes(dc);
        RenderPictures(dc);
        RenderTextBoxes(dc);

        dc.Pop();
    }

    // ── Mouse: resize hit-testing ─────────────────────────────────────────────

    private (ResizeTarget Target, uint Index, double CurrentSize) HitTestResize(Point pos)
    {
        if (Viewport == null) return (ResizeTarget.None, 0, 0);

        if (pos.Y < ColHeaderHeight)
        {
            foreach (var col in Viewport.ColMetrics)
            {
                double rightEdge = col.LeftOffset + col.Width + RowHeaderWidth;
                if (Math.Abs(pos.X - rightEdge) <= ResizeHitZone)
                    return (ResizeTarget.Column, col.Col, col.Width);
            }
        }

        if (pos.X < RowHeaderWidth)
        {
            foreach (var row in Viewport.RowMetrics)
            {
                double bottomEdge = row.TopOffset + row.Height + ColHeaderHeight;
                if (Math.Abs(pos.Y - bottomEdge) <= ResizeHitZone)
                    return (ResizeTarget.Row, row.Row, row.Height);
            }
        }

        return (ResizeTarget.None, 0, 0);
    }

    private bool IsOnAutofillHandle(Point pos)
    {
        if (Viewport == null || !SelectedRange.HasValue) return false;
        var range = SelectedRange.Value;
        var endRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == range.End.Row);
        var endCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == range.End.Col);
        if (endRow == null || endCol == null) return false;

        const double handleSize = 6;
        double hx = endCol.LeftOffset + endCol.Width + RowHeaderWidth  - handleSize / 2;
        double hy = endRow.TopOffset  + endRow.Height + ColHeaderHeight - handleSize / 2;
        return pos.X >= hx - 3 && pos.X <= hx + handleSize + 3
            && pos.Y >= hy - 3 && pos.Y <= hy + handleSize + 3;
    }

    private WorksheetPageMarginEdge? HitTestPageMarginGuide(Point pos)
    {
        if (WorksheetViewMode != WorksheetViewMode.PageLayout || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;

        if (pos.Y >= guide.Value.Top && pos.Y <= guide.Value.Bottom)
        {
            if (Math.Abs(pos.X - guide.Value.MarginLeft) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Left;
            if (Math.Abs(pos.X - guide.Value.MarginRight) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Right;
        }

        if (pos.X >= guide.Value.Left && pos.X <= guide.Value.Right)
        {
            if (Math.Abs(pos.Y - guide.Value.MarginTop) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Top;
            if (Math.Abs(pos.Y - guide.Value.MarginBottom) <= PageMarginGuideHitZone)
                return WorksheetPageMarginEdge.Bottom;
        }

        return null;
    }

    private WorksheetPageMargins? GetPageMarginsForDraggedGuide(Point pos)
    {
        if (_marginDragEdge is not { } edge || PrintArea is not { } printArea)
            return null;

        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return null;

        var fraction = edge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
            ? (pos.X - guide.Value.Left) / Math.Max(1.0, guide.Value.Right - guide.Value.Left)
            : (pos.Y - guide.Value.Top) / Math.Max(1.0, guide.Value.Bottom - guide.Value.Top);

        return WorksheetPageLayout.GetMarginsFromGuideFraction(
            PaperSize,
            PageOrientation,
            PageMargins,
            edge,
            fraction);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_marginDragEdge.HasValue)
        {
            if (GetPageMarginsForDraggedGuide(pos) is { } margins)
                PageMargins = margins;
            Cursor = _marginDragEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging && Viewport != null)
        {
            foreach (var rm in Viewport.RowMetrics)
            {
                double top = rm.TopOffset + ColHeaderHeight;
                if (pos.Y >= top && pos.Y < top + rm.Height)
                {
                    foreach (var cm in Viewport.ColMetrics)
                    {
                        double left = cm.LeftOffset + RowHeaderWidth;
                        if (pos.X >= left && pos.X < left + cm.Width)
                        {
                            _autofillTarget = new CellAddress(default, rm.Row, cm.Col);
                            break;
                        }
                    }
                    break;
                }
            }
            InvalidateVisual();
            return;
        }

        if (_resizeTarget == ResizeTarget.Column)
        {
            var col = Viewport!.ColMetrics.FirstOrDefault(c => c.Col == _resizeIndex);
            if (col is null) return;
            double newWidth = Math.Max(MinCellSize, _resizeSizeStart + (pos.X - _resizeDragStart));
            _resizeLinePos = col.LeftOffset + newWidth + RowHeaderWidth;
            ColumnResizing?.Invoke(_resizeIndex, newWidth);
            InvalidateVisual();
        }
        else if (_resizeTarget == ResizeTarget.Row)
        {
            var row = Viewport!.RowMetrics.FirstOrDefault(r => r.Row == _resizeIndex);
            if (row is null) return;
            double newHeight = Math.Max(MinCellSize, _resizeSizeStart + (pos.Y - _resizeDragStart));
            _resizeLinePos = row.TopOffset + newHeight + ColHeaderHeight;
            RowResizing?.Invoke(_resizeIndex, newHeight);
            InvalidateVisual();
        }
        else
        {
            var (target, _, _) = HitTestResize(pos);
            var marginGuide = HitTestPageMarginGuide(pos);
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE
                   : target == ResizeTarget.Row    ? Cursors.SizeNS
                   : marginGuide is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right ? Cursors.SizeWE
                   : marginGuide is WorksheetPageMarginEdge.Top or WorksheetPageMarginEdge.Bottom ? Cursors.SizeNS
                   : null;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (HitTestPageMarginGuide(pos) is { } marginEdge)
        {
            _marginDragEdge = marginEdge;
            Cursor = marginEdge is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right
                ? Cursors.SizeWE
                : Cursors.SizeNS;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (SelectedRange.HasValue && IsOnAutofillHandle(pos))
        {
            _autofillDragging    = true;
            _autofillSourceRange = SelectedRange.Value;
            _autofillTarget      = SelectedRange.Value.End;
            CaptureMouse();
            Cursor = Cursors.Cross;
            e.Handled = true;
            return;
        }

        var (target, index, size) = HitTestResize(pos);
        if (target != ResizeTarget.None)
        {
            _resizeTarget    = target;
            _resizeIndex     = index;
            _resizeSizeStart = size;
            _resizeDragStart = target == ResizeTarget.Column ? pos.X : pos.Y;
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE : Cursors.SizeNS;

            if (target == ResizeTarget.Column)
            {
                var col = Viewport!.ColMetrics.First(c => c.Col == index);
                _resizeLinePos = col.LeftOffset + col.Width + RowHeaderWidth;
            }
            else
            {
                var row = Viewport!.RowMetrics.First(r => r.Row == index);
                _resizeLinePos = row.TopOffset + row.Height + ColHeaderHeight;
            }

            CaptureMouse();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonDown(e);
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (Viewport == null) { base.OnMouseRightButtonDown(e); return; }
        var pos = e.GetPosition(this);
        if (pos.X < RowHeaderWidth || pos.Y < ColHeaderHeight) { base.OnMouseRightButtonDown(e); return; }

        foreach (var rm in Viewport.RowMetrics)
        {
            double top = rm.TopOffset + ColHeaderHeight;
            if (pos.Y < top || pos.Y >= top + rm.Height) continue;
            foreach (var cm in Viewport.ColMetrics)
            {
                double left = cm.LeftOffset + RowHeaderWidth;
                if (pos.X >= left && pos.X < left + cm.Width)
                {
                    ContextMenuRequested?.Invoke(new CellAddress(default, rm.Row, cm.Col), pos);
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnMouseRightButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_marginDragEdge.HasValue)
        {
            var pos = e.GetPosition(this);
            if (GetPageMarginsForDraggedGuide(pos) is { } margins)
            {
                PageMargins = margins;
                PageMarginsChanged?.Invoke(margins);
            }

            _marginDragEdge = null;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging)
        {
            _autofillDragging = false;
            ReleaseMouseCapture();
            Cursor = null;

            if (_autofillSourceRange.HasValue && _autofillTarget.HasValue)
            {
                var src    = _autofillSourceRange.Value;
                var target = _autofillTarget.Value;
                if (target.Row > src.End.Row || target.Col > src.End.Col)
                {
                    var fillRange = new GridRange(
                        new CellAddress(src.Start.Sheet,
                            target.Row > src.End.Row ? src.End.Row + 1 : src.Start.Row,
                            target.Col > src.End.Col ? src.End.Col + 1 : src.Start.Col),
                        new CellAddress(src.Start.Sheet, target.Row, target.Col));
                    AutofillRequested?.Invoke(src, fillRange);
                }
            }

            _autofillSourceRange = null;
            _autofillTarget      = null;
            e.Handled = true;
            return;
        }

        if (_resizeTarget != ResizeTarget.None)
        {
            var pos = e.GetPosition(this);
            double delta = _resizeTarget == ResizeTarget.Column
                ? pos.X - _resizeDragStart
                : pos.Y - _resizeDragStart;
            double newSize = Math.Max(MinCellSize, _resizeSizeStart + delta);

            if (_resizeTarget == ResizeTarget.Column)
                ColumnResized?.Invoke(_resizeIndex, newSize);
            else
                RowResized?.Invoke(_resizeIndex, newSize);

            _resizeTarget = ResizeTarget.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
        else
        {
            base.OnMouseLeftButtonUp(e);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_resizeTarget == ResizeTarget.None && !_marginDragEdge.HasValue)
            Cursor = null;
        base.OnMouseLeave(e);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void RenderFreezeDivider(DrawingContext dc)
    {
        if (Viewport?.FrozenPanes == null) return;
        var fp = Viewport.FrozenPanes;

        if (fp.Rows > 0)
        {
            var lastFrozenRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == fp.Rows);
            if (lastFrozenRow != null)
            {
                double y = lastFrozenRow.TopOffset + lastFrozenRow.Height + ColHeaderHeight;
                dc.DrawLine(FreezePen, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (fp.Cols > 0)
        {
            var lastFrozenCol = Viewport.ColMetrics.FirstOrDefault(c => c.Col == fp.Cols);
            if (lastFrozenCol != null)
            {
                double x = lastFrozenCol.LeftOffset + lastFrozenCol.Width + RowHeaderWidth;
                dc.DrawLine(FreezePen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderSplitDivider(DrawingContext dc)
    {
        if (Viewport == null) return;

        if (SplitRow is { } splitRow)
        {
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == splitRow);
            if (row is not null)
            {
                var y = row.TopOffset + ColHeaderHeight;
                dc.DrawLine(SplitPanePen, new Point(RowHeaderWidth, y), new Point(ActualWidth, y));
            }
        }

        if (SplitColumn is { } splitColumn)
        {
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == splitColumn);
            if (col is not null)
            {
                var x = col.LeftOffset + RowHeaderWidth;
                dc.DrawLine(SplitPanePen, new Point(x, ColHeaderHeight), new Point(x, ActualHeight));
            }
        }
    }

    private void RenderCharts(DrawingContext dc)
    {
        if (Charts == null || Viewport == null) return;
        foreach (var chart in Charts)
        {
            var img = ChartRenderer.Render(chart, Viewport);
            if (img == null) continue;
            var rect = new Rect(
                chart.Left + RowHeaderWidth, chart.Top + ColHeaderHeight,
                chart.Width, chart.Height);
            dc.DrawImage(img, rect);
        }
    }

    private void RenderTextBoxes(DrawingContext dc)
    {
        if (TextBoxes == null || Viewport == null) return;

        foreach (var textBox in TextBoxes)
        {
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == textBox.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == textBox.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + RowHeaderWidth,
                row.TopOffset + ColHeaderHeight,
                Math.Max(24, textBox.Width),
                Math.Max(18, textBox.Height));
            var rotationPushed = PushRotation(dc, textBox.RotationDegrees, rect);
            var fillBrush = textBox.FillColor is { } fillColor
                ? new SolidColorBrush(Color.FromArgb(242, fillColor.R, fillColor.G, fillColor.B))
                : new SolidColorBrush(Color.FromArgb(242, 255, 255, 255));
            var borderColor = textBox.OutlineColor ?? new CellColor(89, 89, 89);
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(borderColor.R, borderColor.G, borderColor.B)), 1);
            dc.DrawRectangle(fillBrush, borderPen, rect);

            var text = new FormattedText(
                textBox.Text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface,
                12,
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, rect.Width - 8),
                MaxTextHeight = Math.Max(1, rect.Height - 8)
            };

            dc.PushClip(new RectangleGeometry(new Rect(rect.Left + 4, rect.Top + 4, rect.Width - 8, rect.Height - 8)));
            dc.DrawText(text, new Point(rect.Left + 4, rect.Top + 4));
            dc.Pop();
            if (rotationPushed) dc.Pop();
        }
    }

    private void RenderDrawingShapes(DrawingContext dc)
    {
        if (DrawingShapes == null || Viewport == null) return;

        foreach (var shape in DrawingShapes)
        {
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == shape.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == shape.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + RowHeaderWidth,
                row.TopOffset + ColHeaderHeight,
                Math.Max(8, shape.Width),
                Math.Max(8, shape.Height));

            var rotationPushed = PushRotation(dc, shape.RotationDegrees, rect);
            var outlineColor = shape.OutlineColor ?? new CellColor(68, 68, 68);
            var fillColor = shape.FillColor ?? new CellColor(31, 119, 180);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(outlineColor.R, outlineColor.G, outlineColor.B)), 1.5);
            var fill = new SolidColorBrush(Color.FromArgb(32, fillColor.R, fillColor.G, fillColor.B));
            switch (shape.Kind)
            {
                case DrawingShapeKind.Rectangle:
                    dc.DrawRectangle(fill, pen, rect);
                    break;
                case DrawingShapeKind.Ellipse:
                    dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                    break;
                case DrawingShapeKind.Line:
                    dc.DrawLine(pen, rect.TopLeft, rect.BottomRight);
                    break;
            }
            if (rotationPushed) dc.Pop();
        }
    }

    private static bool PushRotation(DrawingContext dc, double rotationDegrees, Rect rect)
    {
        if (Math.Abs(rotationDegrees % 360) <= 0.0001)
            return false;

        dc.PushTransform(new RotateTransform(
            rotationDegrees,
            rect.Left + rect.Width / 2,
            rect.Top + rect.Height / 2));
        return true;
    }

    private void RenderPictures(DrawingContext dc)
    {
        if (Pictures == null || Viewport == null) return;

        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(210, 210, 210)), 0.75);
        var fill = Brushes.White;
        foreach (var picture in Pictures)
        {
            var row = Viewport.RowMetrics.FirstOrDefault(r => r.Row == picture.Anchor.Row);
            var col = Viewport.ColMetrics.FirstOrDefault(c => c.Col == picture.Anchor.Col);
            if (row is null || col is null) continue;

            var rect = new Rect(
                col.LeftOffset + RowHeaderWidth,
                row.TopOffset + ColHeaderHeight,
                Math.Max(24, picture.Width),
                Math.Max(18, picture.Height));

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.PushTransform(new RotateTransform(
                    picture.RotationDegrees,
                    rect.Left + rect.Width / 2,
                    rect.Top + rect.Height / 2));

            if (picture.Kind == PictureKind.Image &&
                TryLoadPictureImage(picture, out var image))
            {
                dc.DrawImage(image, rect);
                dc.DrawRectangle(null, borderPen, rect);
                if (Math.Abs(picture.RotationDegrees) > 0.0001)
                    dc.Pop();
                continue;
            }

            dc.DrawRectangle(fill, borderPen, rect);

            var rows = Math.Max(1, picture.SourceRowCount);
            var cols = Math.Max(1, picture.SourceColumnCount);
            var cellWidth = rect.Width / cols;
            var cellHeight = rect.Height / rows;

            for (uint r = 1; r < rows; r++)
            {
                var y = rect.Top + r * cellHeight;
                dc.DrawLine(gridPen, new Point(rect.Left, y), new Point(rect.Right, y));
            }

            for (uint c = 1; c < cols; c++)
            {
                var x = rect.Left + c * cellWidth;
                dc.DrawLine(gridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            }

            foreach (var cell in picture.Cells)
            {
                if (cell.RowOffset >= rows || cell.ColumnOffset >= cols || string.IsNullOrEmpty(cell.Text))
                    continue;
                var textRect = new Rect(
                    rect.Left + cell.ColumnOffset * cellWidth + 3,
                    rect.Top + cell.RowOffset * cellHeight + 1,
                    Math.Max(1, cellWidth - 6),
                    Math.Max(1, cellHeight - 2));
                var text = new FormattedText(
                    cell.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    DefaultTypeface,
                    11,
                    TextBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    MaxTextWidth = textRect.Width,
                    MaxTextHeight = textRect.Height,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                dc.PushClip(new RectangleGeometry(textRect));
                dc.DrawText(text, textRect.TopLeft);
                dc.Pop();
            }

            if (Math.Abs(picture.RotationDegrees) > 0.0001)
                dc.Pop();
        }
    }

    private static bool TryLoadPictureImage(PictureModel picture, out ImageSource? image)
    {
        image = null;
        if (picture.ImageBytes is not { Length: > 0 })
            return false;

        try
        {
            using var stream = new MemoryStream(picture.ImageBytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RenderSparklines(DrawingContext dc)
    {
        if (Sparklines == null || SparklineValues == null || Viewport == null) return;

        var rowLookup = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookup = Viewport.ColMetrics.ToDictionary(c => c.Col);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 1.25);
        var fill = new SolidColorBrush(Color.FromRgb(33, 115, 70));
        var negativeFill = new SolidColorBrush(Color.FromRgb(192, 0, 0));

        foreach (var sparkline in Sparklines)
        {
            if (!rowLookup.TryGetValue(sparkline.Location.Row, out var row) ||
                !colLookup.TryGetValue(sparkline.Location.Col, out var col) ||
                !SparklineValues.TryGetValue(sparkline.Id, out var values) ||
                values.Count == 0)
            {
                continue;
            }

            var rect = new Rect(
                col.LeftOffset + RowHeaderWidth + 3,
                row.TopOffset + ColHeaderHeight + 3,
                Math.Max(1, col.Width - 6),
                Math.Max(1, row.Height - 6));

            dc.PushClip(new RectangleGeometry(rect));
            if (sparkline.Kind == SparklineKind.Line)
                DrawLineSparkline(dc, values, rect, pen);
            else
                DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, fill, negativeFill);
            dc.Pop();
        }
    }

    private static void DrawLineSparkline(DrawingContext dc, IReadOnlyList<double> values, Rect rect, Pen pen)
    {
        if (values.Count == 1)
        {
            var point = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            dc.DrawEllipse(pen.Brush, null, point, 1.5, 1.5);
            return;
        }

        var min = values.Min();
        var max = values.Max();
        var span = Math.Abs(max - min) < 0.0000001 ? 1 : max - min;
        var points = values
            .Select((value, index) => new Point(
                rect.Left + rect.Width * index / (values.Count - 1),
                rect.Bottom - ((value - min) / span * rect.Height)))
            .ToList();

        for (var i = 1; i < points.Count; i++)
            dc.DrawLine(pen, points[i - 1], points[i]);
    }

    private static void DrawColumnSparkline(
        DrawingContext dc,
        IReadOnlyList<double> values,
        Rect rect,
        bool winLoss,
        Brush positiveFill,
        Brush negativeFill)
    {
        var maxAbs = values.Select(Math.Abs).DefaultIfEmpty(1).Max();
        if (maxAbs < 0.0000001) maxAbs = 1;
        var axis = rect.Top + rect.Height / 2;
        var slot = rect.Width / values.Count;
        var barWidth = Math.Max(1, slot * 0.65);

        for (var i = 0; i < values.Count; i++)
        {
            var value = winLoss ? Math.Sign(values[i]) : values[i];
            var height = winLoss
                ? rect.Height / 2
                : Math.Abs(value) / maxAbs * rect.Height / 2;
            var x = rect.Left + i * slot + (slot - barWidth) / 2;
            var y = value >= 0 ? axis - height : axis;
            dc.DrawRectangle(value >= 0 ? positiveFill : negativeFill, null, new Rect(x, y, barWidth, Math.Max(1, height)));
        }
    }

    private void RenderResizeLine(DrawingContext dc)
    {
        if (_resizeTarget == ResizeTarget.Column)
            dc.DrawLine(ResizeLinePen,
                new Point(_resizeLinePos, 0),
                new Point(_resizeLinePos, ActualHeight));
        else if (_resizeTarget == ResizeTarget.Row)
            dc.DrawLine(ResizeLinePen,
                new Point(0, _resizeLinePos),
                new Point(ActualWidth, _resizeLinePos));
    }

    private void RenderAutofillPreview(DrawingContext dc)
    {
        if (!_autofillDragging || !_autofillSourceRange.HasValue || !_autofillTarget.HasValue) return;
        var vp = Viewport;
        if (vp == null) return;

        var src = _autofillSourceRange.Value;
        var tgt = _autofillTarget.Value;

        // Extend selection rect to cover source + fill target
        var previewStart = new CellAddress(src.Start.Sheet,
            Math.Min(src.Start.Row, tgt.Row),
            Math.Min(src.Start.Col, tgt.Col));
        var previewEnd = new CellAddress(src.Start.Sheet,
            Math.Max(src.End.Row, tgt.Row),
            Math.Max(src.End.Col, tgt.Col));

        var (top, left, bottom, right) = GetRangePixels(vp,
            new GridRange(previewStart, previewEnd));
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return;

        var dash = new DashStyle([4.0, 4.0], 0);
        var pen  = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 1.5) { DashStyle = dash };
        pen.Freeze();
        var rect = new Rect(left.Value, top.Value, right.Value - left.Value, bottom.Value - top.Value);
        dc.DrawRectangle(null, pen, rect);
    }

    private void RenderMarchingAnts(DrawingContext dc)
    {
        var cbRange = ClipboardRange;
        if (cbRange == null || Viewport == null) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, cbRange.Value);
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return;

        var dash = new DashStyle([4.0, 4.0], _marchOffset);
        var pen  = new Pen(new SolidColorBrush(Color.FromRgb(33, 115, 70)), 1.5) { DashStyle = dash };
        pen.Freeze();
        var rect = new Rect(left.Value, top.Value, right.Value - left.Value, bottom.Value - top.Value);
        dc.DrawRectangle(null, pen, rect);
    }

    private void RenderWorksheetViewOverlay(DrawingContext dc)
    {
        if (Viewport == null || WorksheetViewMode == WorksheetViewMode.Normal) return;

        if (WorksheetViewMode == WorksheetViewMode.PageBreakPreview)
        {
            dc.DrawRectangle(PageBreakPreviewBrush, null,
                new Rect(RowHeaderWidth, ColHeaderHeight,
                    Math.Max(0, ActualWidth - RowHeaderWidth),
                    Math.Max(0, ActualHeight - ColHeaderHeight)));
        }

        if (PrintArea is { } printArea)
        {
            RenderPrintAreaBoundary(dc, printArea,
                WorksheetViewMode == WorksheetViewMode.PageLayout ? PageLayoutPen : PageBreakPen);
            if (WorksheetViewMode == WorksheetViewMode.PageLayout)
                RenderPageMarginGuides(dc, printArea);
        }

        RenderManualPageBreaks(dc);
    }

    private void RenderPageMarginGuides(DrawingContext dc, GridRange printArea)
    {
        var guide = GetPageMarginGuidePixels(printArea);
        if (guide is null) return;

        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.MarginLeft, guide.Value.Top), new Point(guide.Value.MarginLeft, guide.Value.Bottom));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.MarginRight, guide.Value.Top), new Point(guide.Value.MarginRight, guide.Value.Bottom));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.Left, guide.Value.MarginTop), new Point(guide.Value.Right, guide.Value.MarginTop));
        dc.DrawLine(PageMarginGuidePen, new Point(guide.Value.Left, guide.Value.MarginBottom), new Point(guide.Value.Right, guide.Value.MarginBottom));
    }

    private (double Top, double Left, double Bottom, double Right,
        double MarginLeft, double MarginRight, double MarginTop, double MarginBottom)?
        GetPageMarginGuidePixels(GridRange printArea)
    {
        if (Viewport == null) return null;
        var (top, left, bottom, right) = GetRangePixels(Viewport, printArea);
        if (!top.HasValue || !left.HasValue || !bottom.HasValue || !right.HasValue) return null;

        var guide = WorksheetPageLayout.GetMarginGuideFractions(PaperSize, PageOrientation, PageMargins);
        var width = right.Value - left.Value;
        var height = bottom.Value - top.Value;
        if (width <= 0 || height <= 0) return null;

        return (
            top.Value,
            left.Value,
            bottom.Value,
            right.Value,
            left.Value + width * guide.Left,
            left.Value + width * guide.Right,
            top.Value + height * guide.Top,
            top.Value + height * guide.Bottom);
    }

    private void RenderPrintAreaBoundary(DrawingContext dc, GridRange printArea, Pen pen)
    {
        if (Viewport == null) return;
        var rows = Viewport.RowMetrics;
        var cols = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;
        if (printArea.End.Row < rows[0].Row || printArea.Start.Row > rows[^1].Row) return;
        if (printArea.End.Col < cols[0].Col || printArea.Start.Col > cols[^1].Col) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, printArea);
        var drawTop = top ?? ColHeaderHeight;
        var drawLeft = left ?? RowHeaderWidth;
        var drawBottom = bottom ?? ActualHeight;
        var drawRight = right ?? ActualWidth;

        dc.DrawRectangle(null, pen, new Rect(
            new Point(drawLeft, drawTop),
            new Point(drawRight, drawBottom)));
    }

    private void RenderManualPageBreaks(DrawingContext dc)
    {
        if (Viewport == null) return;

        if (RowPageBreaks is not null)
        {
            foreach (var rowBreak in RowPageBreaks)
            {
                var metric = Viewport.RowMetrics.FirstOrDefault(row => row.Row == rowBreak);
                if (metric is null) continue;
                var y = metric.TopOffset + ColHeaderHeight;
                dc.DrawLine(PageBreakPen, new Point(RowHeaderWidth, y), new Point(ActualWidth, y));
            }
        }

        if (ColumnPageBreaks is not null)
        {
            foreach (var columnBreak in ColumnPageBreaks)
            {
                var metric = Viewport.ColMetrics.FirstOrDefault(col => col.Col == columnBreak);
                if (metric is null) continue;
                var x = metric.LeftOffset + RowHeaderWidth;
                dc.DrawLine(PageBreakPen, new Point(x, ColHeaderHeight), new Point(x, ActualHeight));
            }
        }
    }

    // Returns pixel coords for a range, clamped to viewport boundaries.
    private (double? top, double? left, double? bottom, double? right) GetRangePixels(
        ViewportModel vp, GridRange range)
    {
        double? top = null, left = null, bottom = null, right = null;
        foreach (var row in vp.RowMetrics)
        {
            if (row.Row == range.Start.Row) top    = row.TopOffset + ColHeaderHeight;
            if (row.Row == range.End.Row)   bottom = row.TopOffset + row.Height + ColHeaderHeight;
        }
        foreach (var col in vp.ColMetrics)
        {
            if (col.Col == range.Start.Col) left  = col.LeftOffset + RowHeaderWidth;
            if (col.Col == range.End.Col)   right = col.LeftOffset + col.Width + RowHeaderWidth;
        }
        return (top, left, bottom, right);
    }

    private void RenderSelection(DrawingContext dc)
    {
        if (Viewport == null) return;
        if (SelectedRanges is { Count: > 0 } selectedRanges)
        {
            foreach (var range in selectedRanges)
                RenderSelectionRange(dc, range, drawHandle: false);

            if (SelectedRange is { } activeRange)
                RenderSelectionHandle(dc, activeRange);
            return;
        }

        if (SelectedRange == null) return;

        RenderSelectionRange(dc, SelectedRange.Value, drawHandle: true);
    }

    private void RenderSelectionRange(DrawingContext dc, GridRange range, bool drawHandle)
    {
        if (Viewport == null) return;
        var rows  = Viewport.RowMetrics;
        var cols  = Viewport.ColMetrics;
        if (rows.Count == 0 || cols.Count == 0) return;

        if (range.End.Row < rows[0].Row || range.Start.Row > rows[^1].Row) return;
        if (range.End.Col < cols[0].Col || range.Start.Col > cols[^1].Col) return;

        var (top, left, bottom, right) = GetRangePixels(Viewport, range);

        double drawTop    = top    ?? ColHeaderHeight;
        double drawBottom = bottom ?? ActualHeight;
        double drawLeft   = left   ?? RowHeaderWidth;
        double drawRight  = right  ?? ActualWidth;

        var rect = new Rect(new Point(drawLeft, drawTop), new Point(drawRight, drawBottom));
        dc.DrawRectangle(SelectionBrush, null, rect);

        if (top.HasValue)    dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawRight, drawTop));
        if (bottom.HasValue) dc.DrawLine(SelectionPen, new Point(drawLeft,  drawBottom), new Point(drawRight, drawBottom));
        if (left.HasValue)   dc.DrawLine(SelectionPen, new Point(drawLeft,  drawTop),    new Point(drawLeft,  drawBottom));
        if (right.HasValue)  dc.DrawLine(SelectionPen, new Point(drawRight, drawTop),    new Point(drawRight, drawBottom));

        if (drawHandle)
            DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private void RenderSelectionHandle(DrawingContext dc, GridRange range)
    {
        if (Viewport == null) return;
        var (top, left, bottom, right) = GetRangePixels(Viewport, range);
        double drawBottom = bottom ?? ActualHeight;
        double drawRight  = right  ?? ActualWidth;
        DrawSelectionHandle(dc, right, bottom, drawRight, drawBottom);
    }

    private static void DrawSelectionHandle(DrawingContext dc, double? right, double? bottom, double drawRight, double drawBottom)
    {
        if (!right.HasValue || !bottom.HasValue)
            return;

        const double handleSize = 6;
        double hx = drawRight - handleSize / 2;
        double hy = drawBottom - handleSize / 2;
        dc.DrawRectangle(Brushes.White, SelectionPen,
            new Rect(hx, hy, handleSize, handleSize));
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(33, 115, 70)), null,
            new Rect(hx + 1, hy + 1, handleSize - 2, handleSize - 2));
    }

    private void RenderHeaders(DrawingContext dc)
    {
        if (!ShowHeaders) return;

        var selectedRanges = SelectedRanges;
        var selRange = SelectedRange;

        // Column Headers (A, B, C…)
        foreach (var col in Viewport!.ColMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => col.Col >= r.Start.Col && col.Col <= r.End.Col)
                : selRange.HasValue
                    && col.Col >= selRange.Value.Start.Col
                    && col.Col <= selRange.Value.End.Col;

            var bg   = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(col.LeftOffset + RowHeaderWidth, 0, col.Width, ColHeaderHeight);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                FormatColumnHeader(col.Col, UseR1C1ReferenceStyle),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Row Headers (1, 2, 3…)
        foreach (var row in Viewport!.RowMetrics)
        {
            bool inSel = selectedRanges is { Count: > 0 }
                ? selectedRanges.Any(r => row.Row >= r.Start.Row && row.Row <= r.End.Row)
                : selRange.HasValue
                    && row.Row >= selRange.Value.Start.Row
                    && row.Row <= selRange.Value.End.Row;

            var bg   = inSel ? HeaderHighlightBrush : HeaderBackgroundBrush;
            var rect = new Rect(0, row.TopOffset + ColHeaderHeight, RowHeaderWidth, row.Height);
            dc.DrawRectangle(bg, GridPen, rect);

            var text = new FormattedText(
                row.Row.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                DefaultTypeface, 11, TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(text, new Point(
                rect.Left + (rect.Width  - text.Width)  / 2,
                rect.Top  + (rect.Height - text.Height) / 2));
        }

        // Top-left corner
        dc.DrawRectangle(HeaderBackgroundBrush, GridPen,
            new Rect(0, 0, RowHeaderWidth, ColHeaderHeight));
    }

    internal static string FormatColumnHeader(uint column, bool useR1C1ReferenceStyle) =>
        useR1C1ReferenceStyle
            ? column.ToString(CultureInfo.InvariantCulture)
            : CellAddress.NumberToColumnName(column);

    private void RenderGridLines(DrawingContext dc)
    {
        // Grid lines are drawn as cell/header rectangle borders.
    }

    private GridRange? FindMerge(uint row, uint col)
    {
        return _mergeLookup.TryGetValue((row, col), out var r) ? r : null;
    }

    private void RenderCells(DrawingContext dc)
    {
        var styleLookup = Viewport!.Cells
            .Where(c => c.Style != null)
            .ToDictionary(c => (c.Row, c.Col), c => c.Style!);

        var rowLookupAll = Viewport.RowMetrics.ToDictionary(r => r.Row);
        var colLookupAll = Viewport.ColMetrics.ToDictionary(c => c.Col);


        // Pass 1: backgrounds
        foreach (var rowMetric in Viewport.RowMetrics)
        {
            foreach (var colMetric in Viewport.ColMetrics)
            {
                var merge = FindMerge(rowMetric.Row, colMetric.Col);
                if (merge.HasValue && (rowMetric.Row != merge.Value.Start.Row || colMetric.Col != merge.Value.Start.Col))
                    continue;

                double w = colMetric.Width;
                double h = rowMetric.Height;

                if (merge.HasValue)
                {
                    for (uint c2 = merge.Value.Start.Col + 1; c2 <= merge.Value.End.Col; c2++)
                        if (colLookupAll.TryGetValue(c2, out var cm2)) w += cm2.Width;
                    for (uint r2 = merge.Value.Start.Row + 1; r2 <= merge.Value.End.Row; r2++)
                        if (rowLookupAll.TryGetValue(r2, out var rm2)) h += rm2.Height;
                }

                var rect = new Rect(
                    colMetric.LeftOffset + RowHeaderWidth, rowMetric.TopOffset + ColHeaderHeight, w, h);

                Brush fill = Brushes.White;
                if (styleLookup.TryGetValue((rowMetric.Row, colMetric.Col), out var bg)
                    && bg.FillColor.HasValue)
                {
                    fill = new SolidColorBrush(Color.FromRgb(
                        bg.FillColor.Value.R, bg.FillColor.Value.G, bg.FillColor.Value.B));
                }

                dc.DrawRectangle(fill, GridPen, rect);
            }
        }

        // Pass 2: explicit cell borders
        foreach (var cell in Viewport.Cells)
        {
            if (cell.Style == null) continue;
            var rowMetric = Viewport.RowMetrics.FirstOrDefault(r => r.Row == cell.Row);
            var colMetric = Viewport.ColMetrics.FirstOrDefault(c => c.Col == cell.Col);
            if (rowMetric is null || colMetric is null) continue;

            double x = colMetric.LeftOffset + RowHeaderWidth;
            double y = rowMetric.TopOffset   + ColHeaderHeight;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            DrawBorderEdge(dc, cell.Style.BorderTop,    new Point(x,     y),     new Point(x + w, y));
            DrawBorderEdge(dc, cell.Style.BorderBottom, new Point(x,     y + h), new Point(x + w, y + h));
            DrawBorderEdge(dc, cell.Style.BorderLeft,   new Point(x,     y),     new Point(x,     y + h));
            DrawBorderEdge(dc, cell.Style.BorderRight,  new Point(x + w, y),     new Point(x + w, y + h));
        }

        // Pass 3: text
        var rowLookup = rowLookupAll;
        var colLookup = colLookupAll;

        var occupied = new HashSet<(uint, uint)>(
            Viewport.Cells
                .Where(c => !string.IsNullOrEmpty(c.DisplayText))
                .Select(c => (c.Row, c.Col)));

        foreach (var cell in Viewport.Cells)
        {
            if (!rowLookup.TryGetValue(cell.Row, out var rowMetric)) continue;
            if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;
            if (string.IsNullOrEmpty(cell.DisplayText)) continue;

            var cellMerge = FindMerge(cell.Row, cell.Col);
            if (cellMerge.HasValue && (cell.Row != cellMerge.Value.Start.Row || cell.Col != cellMerge.Value.Start.Col))
                continue;

            var style = cell.Style;
            double w = colMetric.Width;
            double h = rowMetric.Height;

            if (cellMerge.HasValue)
            {
                for (uint c2 = cellMerge.Value.Start.Col + 1; c2 <= cellMerge.Value.End.Col; c2++)
                    if (colLookup.TryGetValue(c2, out var cm2)) w += cm2.Width;
                for (uint r2 = cellMerge.Value.Start.Row + 1; r2 <= cellMerge.Value.End.Row; r2++)
                    if (rowLookup.TryGetValue(r2, out var rm2)) h += rm2.Height;
            }

            var rect = new Rect(
                colMetric.LeftOffset + RowHeaderWidth, rowMetric.TopOffset + ColHeaderHeight, w, h);

            var hAlign   = style?.HorizontalAlignment ?? CellHAlign.General;
            bool isNumeric = cell.RawValue is NumberValue or DateTimeValue;
            bool wrapText  = style?.WrapText == true;

            double renderWidth = w;
            bool canOverflow = !wrapText && !isNumeric && !cellMerge.HasValue
                && (hAlign == CellHAlign.Left || hAlign == CellHAlign.General);

            if (canOverflow)
            {
                uint nextCol = colMetric.Col + 1;
                while (colLookup.TryGetValue(nextCol, out var nextMetric)
                       && !occupied.Contains((cell.Row, nextCol)))
                {
                    renderWidth += nextMetric.Width;
                    nextCol++;
                }
            }

            var typeface = (style?.Bold == true, style?.Italic == true) switch
            {
                (true,  true)  => new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic,  FontWeights.Bold,   FontStretches.Normal),
                (true,  false) => new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,  FontWeights.Bold,   FontStretches.Normal),
                (false, true)  => new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic,  FontWeights.Normal, FontStretches.Normal),
                _              => DefaultTypeface
            };

            double fontSize = (style?.FontSize > 0) ? style!.FontSize : 12.0;

            Brush textBrush = TextBrush;
            if (style?.FontColor is { } fc && !fc.IsBlack)
                textBrush = new SolidColorBrush(Color.FromRgb(fc.R, fc.G, fc.B));

            var text = new FormattedText(
                cell.DisplayText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, fontSize, textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            if (style?.Strikethrough == true)
                text.SetTextDecorations(TextDecorations.Strikethrough);
            if (style?.Underline == true && style?.Strikethrough != true)
                text.SetTextDecorations(TextDecorations.Underline);
            if (style?.DoubleUnderline == true && style?.Strikethrough != true)
                text.SetTextDecorations(TextDecorations.Underline);

            if (wrapText)
            {
                text.MaxTextWidth  = Math.Max(1, colMetric.Width - 4);
                text.TextAlignment = hAlign switch
                {
                    CellHAlign.Center => System.Windows.TextAlignment.Center,
                    CellHAlign.Right  => System.Windows.TextAlignment.Right,
                    _                 => System.Windows.TextAlignment.Left
                };
            }

            double indentPx = (style?.IndentLevel ?? 0) * 8.0;
            double textX = hAlign switch
            {
                CellHAlign.Right  => rect.Right - Math.Min(text.Width, colMetric.Width - 2) - 2,
                CellHAlign.Center => rect.Left  + (colMetric.Width - text.Width) / 2,
                CellHAlign.General when isNumeric
                                  => rect.Right - Math.Min(text.Width, colMetric.Width - 2) - 2,
                _                 => rect.Left + 2 + indentPx
            };

            double textY = style?.VerticalAlignment switch
            {
                CellVAlign.Top    => rect.Top + 1,
                CellVAlign.Center => rect.Top + (rect.Height - text.Height) / 2,
                CellVAlign.Bottom => rect.Bottom - text.Height - 1,
                _                 => rect.Top  + (rect.Height - text.Height) / 2
            };
            textY = Math.Max(rect.Top, textY);

            var clipRect = new Rect(rect.Left, rect.Top, renderWidth, rect.Height);
            dc.PushClip(new RectangleGeometry(clipRect));
            dc.DrawText(text, new Point(textX, textY));

            if (style?.DoubleUnderline == true)
            {
                double uY = textY + text.Height + 1;
                dc.DrawLine(new Pen(textBrush, 1), new Point(textX, uY), new Point(textX + text.Width, uY));
                dc.DrawLine(new Pen(textBrush, 1), new Point(textX, uY + 2), new Point(textX + text.Width, uY + 2));
            }
            dc.Pop();
        }
    }

    private static void DrawBorderEdge(DrawingContext dc, CellBorder border, Point p1, Point p2)
    {
        if (border.Style == BorderStyle.None) return;

        double thickness = border.Style switch
        {
            BorderStyle.Thin   => 0.5,
            BorderStyle.Medium => 1.5,
            BorderStyle.Thick  => 2.5,
            _                  => 0.5
        };

        DashStyle dash = border.Style switch
        {
            BorderStyle.Dashed => DashStyles.Dash,
            BorderStyle.Dotted => DashStyles.Dot,
            _                  => DashStyles.Solid
        };

        var pen = new Pen(
            new SolidColorBrush(Color.FromRgb(border.Color.R, border.Color.G, border.Color.B)),
            thickness) { DashStyle = dash };

        dc.DrawLine(pen, p1, p2);
    }
}
