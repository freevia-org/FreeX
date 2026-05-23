using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using Freexcel.Core.Model;
using CellHAlign  = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign  = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.UI;

public enum GridObjectDisplayMode
{
    All,
    Placeholders,
    Nothing
}

/// <summary>
/// A high-performance, virtualized spreadsheet grid control.
/// Renders only the visible portion of the workbook using low-level DrawingContext.
/// </summary>
public partial class GridView : FrameworkElement
{
    public GridView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
    }

    // Column header strip height (horizontal row of A, B, C â€¦ letters)
    public const double ColHeaderHeight = 18;
    // Row header strip width (vertical column of 1, 2, 3 ... numbers)
    public const double RowHeaderWidth = 30;
    // Dynamic width â€” grows for 4+ digit row numbers; use this for all layout
    public double ActualRowHeaderWidth => ShowHeaders ? CalculateRowHeaderWidth(Viewport) : 0.0;
    // Header height exposed for external callers respecting ShowHeaders
    public double EffectiveColHeaderHeight => ShowHeaders ? ColHeaderHeight : 0.0;

    public static double CalculateRowHeaderWidth(ViewportModel? viewport) =>
        viewport?.RowMetrics.Max(r => (uint?)r.Row) switch
        {
            >= 1_000_000 => 54,
            >= 100_000   => 48,
            >= 10_000    => 42,
            >= 1_000     => 36,
            _            => RowHeaderWidth,
        };

    private const double ResizeHitZone = 4;
    private const double SplitDividerHitZone = 4;
    private const double MinCellSize   = 5;
    private const double DefaultCellFontSizePoints = 11.0;
    private const double PageMarginGuideHitZone = 5;

    private static readonly Typeface DefaultTypeface       = new("Calibri");
    private static readonly Brush    GridLineBrush         = MakeBrush(220, 220, 220);
    private static readonly Brush    TextBrush             = Brushes.Black;
    private static readonly Brush    HeaderBackgroundBrush = MakeBrush(242, 242, 242);
    private static readonly Brush    HeaderHighlightBrush  = MakeBrush(218, 232, 218);
    private static readonly Pen      GridPen               = new(GridLineBrush, 1);
    private static readonly Brush    SelectionBrush        = MakeBrushAlpha(32, 33, 115, 70);
    private static readonly Pen      SelectionPen          = new(MakeBrush(33, 115, 70), 2);
    private static readonly Brush    QuickAnalysisPreviewBrush = MakeBrushAlpha(38, 91, 155, 213);
    private static readonly Pen      QuickAnalysisPreviewPen = new(MakeBrush(47, 117, 181), 2);
    private static readonly Pen      ResizeLinePen         = MakeResizeLinePen();
    private static readonly Pen      FreezePen             = MakeFreezePen();
    private static readonly Brush    PageBreakPreviewBrush = MakeBrushAlpha(28, 0, 103, 192);
    private static readonly Pen      PageBreakPen          = MakePageBreakPen();
    private static readonly Pen      PageLayoutPen         = MakePageLayoutPen();
    private static readonly Pen      PageMarginGuidePen    = MakePageMarginGuidePen();
    private static readonly Pen      PageMarginRulerHandlePen = new(MakeBrush(75, 75, 75), 1);
    private static readonly Brush    PageMarginRulerHandleBrush = MakeBrush(238, 238, 238);
    private static readonly Pen      SplitPanePen          = MakeSplitPanePen();
    private static readonly Brush    SplitScrollbarTrackBrush = MakeBrush(244, 244, 244);
    private static readonly Brush    SplitScrollbarThumbBrush = MakeBrush(188, 188, 188);
    private static readonly Pen      SplitScrollbarPen        = new(MakeBrush(196, 196, 196), 1);
    private static readonly Brush    FormulaTraceArrowBrush   = MakeBrush(0, 102, 204);
    private static readonly Pen      FormulaTraceArrowPen     = MakeFormulaTraceArrowPen();

    private static double ToDisplayFontSize(double pointSize) =>
        Math.Max(1.0, Math.Round(pointSize * (96.0 / 72.0), MidpointRounding.AwayFromZero));

    public static double ResolveShrinkFontSize(
        double requestedFontSize,
        double availableWidth,
        Func<double, double> measureTextWidth,
        double minimumFontSize = 6.0)
    {
        if (requestedFontSize <= minimumFontSize || availableWidth <= 0)
            return Math.Min(requestedFontSize, minimumFontSize);

        var fontSize = requestedFontSize;
        while (fontSize > minimumFontSize && measureTextWidth(fontSize) > availableWidth)
            fontSize = Math.Max(minimumFontSize, fontSize - 1);

        return fontSize;
    }

    public static bool CanOverflowCellText(CellStyle? style, ScalarValue? rawValue, string? displayText, GridRange? merge)
    {
        var hAlign = style?.HorizontalAlignment ?? CellHAlign.General;
        return !string.IsNullOrEmpty(displayText) &&
            style?.WrapText != true &&
            style?.ShrinkToFit != true &&
            rawValue is not NumberValue and not DateTimeValue &&
            !merge.HasValue &&
            (hAlign == CellHAlign.Left || hAlign == CellHAlign.General);
    }

    public static CellAddress ConstrainAutofillTarget(GridRange source, CellAddress target)
    {
        var verticalDistance = target.Row > source.End.Row ? target.Row - source.End.Row : 0;
        var horizontalDistance = target.Col > source.End.Col ? target.Col - source.End.Col : 0;

        return verticalDistance >= horizontalDistance
            ? new CellAddress(target.Sheet, target.Row, source.End.Col)
            : new CellAddress(target.Sheet, source.End.Row, target.Col);
    }

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

    private static Pen MakeFormulaTraceArrowPen()
    {
        var pen = new Pen(FormulaTraceArrowBrush, 1.5);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush MakeBrushAlpha(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }


    // â”€â”€ Marching ants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    // â”€â”€ Resize drag state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    private SplitDividerHandle _splitDividerDragHandle = SplitDividerHandle.None;
    private bool _splitPaneScrollbarDragging;
    private SplitPaneScrollbar? _splitPaneScrollbarDragSource;
    private double _splitPaneScrollbarDragPointerOffset;

    // â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    /// <summary>Fired when the user activates a rendered PivotChart field button.</summary>
    public event Action<ChartModel, string, System.Windows.Point>? PivotChartFieldButtonRequested;

    /// <summary>Fired when the user releases after dragging a Page Layout margin guide.</summary>
    public event Action<WorksheetPageMargins>? PageMarginsChanged;

    /// <summary>Fired when the user releases after dragging a split-pane divider.</summary>
    public event Action<uint?, uint?>? SplitDividerMoved;

    /// <summary>Fired when the user clicks or drags a split-pane mini scrollbar.</summary>
    public event Action<SplitPaneScrollbarScrollTarget>? SplitPaneScrollbarScrolled;

    // â”€â”€ OnRender â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    protected override void OnRender(DrawingContext dc)
    {
        if (Viewport == null) return;

        RebuildMergeLookup();
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));

        RenderHeaders(dc);
        RenderWorksheetBackground(dc);
        RenderGridLines(dc);
        RenderCells(dc);
        RenderSplitPaneCells(dc);
        RenderWorksheetViewOverlay(dc);
        RenderSparklines(dc);
        RenderQuickAnalysisPreview(dc);
        RenderSelection(dc);
        RenderFormulaTraceArrows(dc);
        RenderAutofillPreview(dc);
        RenderMarchingAnts(dc);
        RenderFreezeDivider(dc);
        RenderSplitDivider(dc);
        RenderSplitPaneScrollbarChrome(dc);
        RenderResizeLine(dc);
        if (ObjectDisplayMode == GridObjectDisplayMode.Placeholders)
        {
            RenderObjectPlaceholders(dc);
        }
        else if (ObjectDisplayMode == GridObjectDisplayMode.All)
        {
            RenderCharts(dc);
            RenderDrawingShapes(dc);
            RenderPictures(dc);
            RenderTextBoxes(dc);
        }

        dc.Pop();
    }

    // â”€â”€ Mouse: resize hit-testing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            Cursor = _splitDividerDragHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : _splitDividerDragHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : Cursors.SizeNS;
            e.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragging)
        {
            if (Viewport is not null)
            {
                if (_splitPaneScrollbarDragSource is not null &&
                    CalculateSplitPaneScrollbarThumbDragTarget(
                        _splitPaneScrollbarDragSource,
                        pos,
                        _splitPaneScrollbarDragPointerOffset) is { } target)
                    SplitPaneScrollbarScrolled?.Invoke(target);
            }

            Cursor = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_autofillDragging && Viewport != null && _autofillSourceRange.HasValue)
        {
            var src = _autofillSourceRange.Value;

            // Compute source range pixel bounds so we can build the bounding rect
            var srcTopRow    = Viewport.RowMetrics.FirstOrDefault(r => r.Row == src.Start.Row);
            var srcBottomRow = Viewport.RowMetrics.FirstOrDefault(r => r.Row == src.End.Row);
            var srcLeftCol   = Viewport.ColMetrics.FirstOrDefault(c => c.Col == src.Start.Col);
            var srcRightCol  = Viewport.ColMetrics.FirstOrDefault(c => c.Col == src.End.Col);

            if (srcTopRow != null && srcBottomRow != null && srcLeftCol != null && srcRightCol != null)
            {
                double srcTop    = srcTopRow.TopOffset    + EffectiveColHeaderHeight;
                double srcBottom = srcBottomRow.TopOffset + EffectiveColHeaderHeight + srcBottomRow.Height;
                double srcLeft   = srcLeftCol.LeftOffset  + ActualRowHeaderWidth;
                double srcRight  = srcRightCol.LeftOffset + ActualRowHeaderWidth + srcRightCol.Width;

                // Bounding rectangle from source to mouse position
                double boundTop    = Math.Min(srcTop,    pos.Y);
                double boundBottom = Math.Max(srcBottom, pos.Y);
                double boundLeft   = Math.Min(srcLeft,   pos.X);
                double boundRight  = Math.Max(srcRight,  pos.X);

                // A cell is included only when its CENTER point is within the bounding rectangle
                CellAddress? newTarget = null;
                foreach (var rm in Viewport.RowMetrics)
                {
                    double midY = rm.TopOffset + EffectiveColHeaderHeight + rm.Height / 2;
                    if (midY < boundTop || midY > boundBottom) continue;
                    foreach (var cm in Viewport.ColMetrics)
                    {
                        double midX = cm.LeftOffset + ActualRowHeaderWidth + cm.Width / 2;
                        if (midX < boundLeft || midX > boundRight) continue;
                        newTarget = new CellAddress(default, rm.Row, cm.Col);
                    }
                }

                if (newTarget.HasValue)
                    _autofillTarget = ConstrainAutofillTarget(src, newTarget.Value);
            }

            InvalidateVisual();
            return;
        }

        if (_resizeTarget == ResizeTarget.Column)
        {
            var col = Viewport!.ColMetrics.FirstOrDefault(c => c.Col == _resizeIndex);
            if (col is null) return;
            double newWidth = Math.Max(MinCellSize, _resizeSizeStart + (pos.X - _resizeDragStart));
            _resizeLinePos = col.LeftOffset + newWidth + ActualRowHeaderWidth;
            ColumnResizing?.Invoke(_resizeIndex, newWidth);
            InvalidateVisual();
        }
        else if (_resizeTarget == ResizeTarget.Row)
        {
            var row = Viewport!.RowMetrics.FirstOrDefault(r => r.Row == _resizeIndex);
            if (row is null) return;
            double newHeight = Math.Max(MinCellSize, _resizeSizeStart + (pos.Y - _resizeDragStart));
            _resizeLinePos = row.TopOffset + newHeight + EffectiveColHeaderHeight;
            RowResizing?.Invoke(_resizeIndex, newHeight);
            InvalidateVisual();
        }
        else
        {
            var (target, _, _) = HitTestResize(pos);
            var marginGuide = HitTestPageMarginGuide(pos);
            var splitHandle = Viewport is null ? SplitDividerHandle.None : HitTestSplitDividerHandle(Viewport, pos);
            var splitScrollbarHit = Viewport is null
                ? null
                : HitTestSplitPaneScrollbar(CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight), pos);
            Cursor = target == ResizeTarget.Column ? Cursors.SizeWE
                   : target == ResizeTarget.Row    ? Cursors.SizeNS
                   : splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
                   : splitHandle == SplitDividerHandle.Horizontal ? Cursors.SizeNS
                   : splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE
                   : splitScrollbarHit?.Orientation == SplitPaneScrollbarOrientation.Vertical ? Cursors.SizeNS
                   : marginGuide is WorksheetPageMarginEdge.Left or WorksheetPageMarginEdge.Right ? Cursors.SizeWE
                   : marginGuide is WorksheetPageMarginEdge.Top or WorksheetPageMarginEdge.Bottom ? Cursors.SizeNS
                   : null;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (HitTestPivotChartFieldButton(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } pivotButton)
        {
            PivotChartFieldButtonRequested?.Invoke(pivotButton.Chart, pivotButton.FieldButton, pos);
            e.Handled = true;
            return;
        }

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

        if (Viewport is not null)
        {
            var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);
            if (HitTestSplitPaneScrollbar(chrome, pos) is { } scrollbarHit)
            {
                _splitPaneScrollbarDragSource = scrollbarHit.Region == SplitPaneRegion.TopRight
                    ? chrome.HorizontalTopRight
                    : chrome.VerticalBottomLeft;
                _splitPaneScrollbarDragging = scrollbarHit.Part == SplitPaneScrollbarPart.Thumb &&
                    _splitPaneScrollbarDragSource is not null;
                _splitPaneScrollbarDragPointerOffset = _splitPaneScrollbarDragSource is null
                    ? 0
                    : scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal
                        ? pos.X - _splitPaneScrollbarDragSource.Thumb.Left
                        : pos.Y - _splitPaneScrollbarDragSource.Thumb.Top;
                if (CalculateSplitPaneScrollbarInteractionTarget(Viewport, chrome, pos) is { } scrollTarget)
                    SplitPaneScrollbarScrolled?.Invoke(scrollTarget);
                Cursor = scrollbarHit.Orientation == SplitPaneScrollbarOrientation.Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
                if (_splitPaneScrollbarDragging)
                    CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        if (Viewport is not null && HitTestSplitDividerHandle(Viewport, pos) is { } splitHandle &&
            splitHandle != SplitDividerHandle.None)
        {
            _splitDividerDragHandle = splitHandle;
            Cursor = splitHandle == SplitDividerHandle.Intersection ? Cursors.SizeAll
                   : splitHandle == SplitDividerHandle.Vertical ? Cursors.SizeWE
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
                _resizeLinePos = col.LeftOffset + col.Width + ActualRowHeaderWidth;
            }
            else
            {
                var row = Viewport!.RowMetrics.First(r => r.Row == index);
                _resizeLinePos = row.TopOffset + row.Height + EffectiveColHeaderHeight;
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
        if (HitTestPivotChartFieldButton(Charts, pos, ActualRowHeaderWidth, EffectiveColHeaderHeight) is { } pivotButton)
        {
            PivotChartFieldButtonRequested?.Invoke(pivotButton.Chart, pivotButton.FieldButton, pos);
            e.Handled = true;
            return;
        }

        if (pos.X < ActualRowHeaderWidth || pos.Y < EffectiveColHeaderHeight) { base.OnMouseRightButtonDown(e); return; }

        foreach (var rm in Viewport.RowMetrics)
        {
            double top = rm.TopOffset + EffectiveColHeaderHeight;
            if (pos.Y < top || pos.Y >= top + rm.Height) continue;
            foreach (var cm in Viewport.ColMetrics)
            {
                double left = cm.LeftOffset + ActualRowHeaderWidth;
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

        if (_splitDividerDragHandle != SplitDividerHandle.None)
        {
            var pos = e.GetPosition(this);
            if (Viewport is not null &&
                CalculateSplitDividerDragTarget(Viewport, _splitDividerDragHandle, pos) is { } target)
            {
                SplitDividerMoved?.Invoke(target.Row, target.Column);
            }

            _splitDividerDragHandle = SplitDividerHandle.None;
            Cursor = null;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_splitPaneScrollbarDragging)
        {
            var pos = e.GetPosition(this);
            if (Viewport is not null)
            {
                var chrome = CalculateSplitPaneScrollbarChrome(Viewport, ActualWidth, ActualHeight);
                if (CalculateSplitPaneScrollbarScrollTarget(chrome, pos) is { } target)
                    SplitPaneScrollbarScrolled?.Invoke(target);
            }

            _splitPaneScrollbarDragging = false;
            _splitPaneScrollbarDragSource = null;
            _splitPaneScrollbarDragPointerOffset = 0;
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
                    GridRange fillRange;
                    if (target.Row > src.End.Row)
                    {
                        // Downward drag â€” preserve source column span
                        fillRange = new GridRange(
                            new CellAddress(src.Start.Sheet, src.End.Row + 1, src.Start.Col),
                            new CellAddress(src.Start.Sheet, target.Row,      src.End.Col));
                    }
                    else
                    {
                        // Rightward drag â€” preserve source row span
                        fillRange = new GridRange(
                            new CellAddress(src.Start.Sheet, src.Start.Row, src.End.Col + 1),
                            new CellAddress(src.Start.Sheet, src.End.Row,   target.Col));
                    }
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
        if (_resizeTarget == ResizeTarget.None &&
            !_marginDragEdge.HasValue &&
            _splitDividerDragHandle == SplitDividerHandle.None &&
            !_splitPaneScrollbarDragging)
            Cursor = null;
        base.OnMouseLeave(e);
    }

    // â”€â”€ Rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}
