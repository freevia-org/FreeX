using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Freexcel.Core.Calc;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateViewport();
    }

    private void VerticalScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType == ScrollEventType.SmallIncrement)
            ExtendScrollRangeFromScrollbarArrow(VerticalScroll, GetScrollableRowLimit(_workbook.GetSheet(_currentSheetId)));
    }

    private void HorizontalScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType == ScrollEventType.SmallIncrement)
            ExtendScrollRangeFromScrollbarArrow(HorizontalScroll, GetScrollableColumnLimit(_workbook.GetSheet(_currentSheetId)));
    }

    private void ScrollBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollBar scrollBar ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<RepeatButton>(source) is not { } button)
            return;

        var isForwardLineButton =
            scrollBar.Orientation == Orientation.Vertical && Equals(button.Command, ScrollBar.LineDownCommand) ||
            scrollBar.Orientation == Orientation.Horizontal && Equals(button.Command, ScrollBar.LineRightCommand);
        if (!isForwardLineButton)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var absoluteLimit = scrollBar.Orientation == Orientation.Vertical
            ? GetScrollableRowLimit(sheet)
            : GetScrollableColumnLimit(sheet);
        if (!TryExtendScrollRangeFromScrollbarArrow(scrollBar, absoluteLimit))
            return;

        e.Handled = true;
    }

    private static void ExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        TryExtendScrollRangeFromScrollbarArrow(scrollBar, absoluteLimit);
    }

    private static bool TryExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        var (maximum, value) = CalculateScrollbarArrowSmallIncrement(
            scrollBar.Value,
            scrollBar.Maximum,
            scrollBar.SmallChange,
            scrollBar.ViewportSize,
            absoluteLimit);
        if (maximum <= scrollBar.Maximum && value <= scrollBar.Value)
            return false;

        scrollBar.Maximum = maximum;
        scrollBar.Value = value;
        return true;
    }

    private void SheetGrid_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        int notches = e.Delta / 120;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+Scroll = zoom
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + notches * 10));
            e.Handled = true;
            return;
        }

        var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (SheetGrid.Viewport?.SplitPanes is not null &&
            !Freexcel.App.UI.GridView.CanScrollSplitPaneRegion(_activeSplitPaneRegion, horizontal))
        {
            e.Handled = true;
            return;
        }

        if (TryScrollIndependentSplitPane(horizontal, notches))
        {
            e.Handled = true;
            return;
        }

        if (horizontal)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var (maximum, value) = CalculateWheelScroll(
                HorizontalScroll.Value,
                HorizontalScroll.Maximum,
                notches,
                3,
                HorizontalScroll.ViewportSize,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Maximum = maximum;
            HorizontalScroll.Value = value;
        }
        else
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var (maximum, value) = CalculateWheelScroll(
                VerticalScroll.Value,
                VerticalScroll.Maximum,
                notches,
                3,
                VerticalScroll.ViewportSize,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Maximum = maximum;
            VerticalScroll.Value = value;
        }
        e.Handled = true;
    }

    private bool TryScrollIndependentSplitPane(bool horizontal, int notches)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return false;

        if (horizontal && _activeSplitPaneRegion == Freexcel.App.UI.SplitPaneRegion.TopRight)
        {
            var chrome = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.HorizontalTopRight is null)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.TopRightLeftCol
                : null;
            var target = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.HorizontalTopRight,
                current ?? Math.Max(1, (uint)HorizontalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return true;
        }

        if (!horizontal && _activeSplitPaneRegion == Freexcel.App.UI.SplitPaneRegion.BottomLeft)
        {
            var chrome = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.VerticalBottomLeft is null)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.BottomLeftTopRow
                : null;
            var target = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.VerticalBottomLeft,
                current ?? Math.Max(1, (uint)VerticalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { BottomLeftTopRow = target.Index };
            UpdateViewport();
            return true;
        }

        return false;
    }

    private void OnSplitPaneScrollbarScrolled(Freexcel.App.UI.SplitPaneScrollbarScrollTarget target)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return;

        _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets);
        offsets ??= new SplitPaneViewportOffsets();

        if (target is
            {
                Region: Freexcel.App.UI.SplitPaneRegion.TopRight,
                Orientation: Freexcel.App.UI.SplitPaneScrollbarOrientation.Horizontal
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return;
        }

        if (target is
            {
                Region: Freexcel.App.UI.SplitPaneRegion.BottomLeft,
                Orientation: Freexcel.App.UI.SplitPaneScrollbarOrientation.Vertical
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { BottomLeftTopRow = target.Index };
            UpdateViewport();
        }
    }

    private void EnsureCellVisible(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp == null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var frozenRows = sheet?.FrozenRows ?? 0;
        var frozenCols = sheet?.FrozenCols ?? 0;

        var rows = vp.RowMetrics.Where(row => row.Row > frozenRows).ToList();
        if (addr.Row > frozenRows && rows.Count > 0 && !rows.Any(r => r.Row == addr.Row))
        {
            uint firstRow = rows[0].Row;
            uint lastRow  = rows[^1].Row;
            var scrollValue = CalculateScrollValueToRevealCell(
                WorksheetIndexToScrollbarValue(addr.Row, frozenRows),
                WorksheetIndexToScrollbarValue(firstRow, frozenRows),
                WorksheetIndexToScrollbarValue(lastRow, frozenRows),
                GetScrollableRowLimit(sheet),
                (uint)rows.Count);
            VerticalScroll.Maximum = CalculateScrollbarMaximumForKeyboardReveal(
                VerticalScroll.Maximum,
                scrollValue,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Value = scrollValue;
        }

        var cols = vp.ColMetrics.Where(col => col.Col > frozenCols).ToList();
        if (addr.Col > frozenCols && cols.Count > 0 && !cols.Any(c => c.Col == addr.Col))
        {
            uint firstCol = cols[0].Col;
            uint lastCol  = cols[^1].Col;
            var scrollValue = CalculateScrollValueToRevealCell(
                WorksheetIndexToScrollbarValue(addr.Col, frozenCols),
                WorksheetIndexToScrollbarValue(firstCol, frozenCols),
                WorksheetIndexToScrollbarValue(lastCol, frozenCols),
                GetScrollableColumnLimit(sheet),
                (uint)cols.Count);
            HorizontalScroll.Maximum = CalculateScrollbarMaximumForKeyboardReveal(
                HorizontalScroll.Maximum,
                scrollValue,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Value = scrollValue;
        }
    }

    // ── Navigation helpers ────────────────────────────────────────────────────

    private void UpdateViewport()
    {
        if (SheetGrid == null || _viewportService == null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null)
            SyncZoomFromSheet(sheet.ZoomPercent);

        var (topRow, leftCol) = CalculateViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);
        topRow = ClampViewportOrigin(
            topRow,
            CellAddress.MaxRow,
            SheetGrid.Viewport is null ? 40 : (uint)CountScrollableRows(SheetGrid.Viewport, sheet));
        leftCol = ClampViewportOrigin(
            leftCol,
            CellAddress.MaxCol,
            SheetGrid.Viewport is null ? 15 : (uint)CountScrollableColumns(SheetGrid.Viewport, sheet));

        var rowHeaderWidth = SheetGrid.ActualRowHeaderWidth;
        var viewport = CreateViewport(sheet, topRow, leftCol, rowHeaderWidth);
        var actualRowHeaderWidth = SheetGrid.ShowHeaders
            ? Freexcel.App.UI.GridView.CalculateRowHeaderWidth(viewport)
            : 0.0;
        if (Math.Abs(actualRowHeaderWidth - rowHeaderWidth) > 0.1)
            viewport = CreateViewport(sheet, topRow, leftCol, actualRowHeaderWidth);

        SheetGrid.Viewport = viewport;
        SheetGrid.FormulaTraceSheetId = _currentSheetId;
        SheetGrid.FormulaTraceArrows = _formulaTraceArrows;
        SheetGrid.ObjectDisplayMode = _options.ObjectsDisplay switch
        {
            FreexcelObjectDisplay.Placeholders => Freexcel.App.UI.GridObjectDisplayMode.Placeholders,
            FreexcelObjectDisplay.Nothing => Freexcel.App.UI.GridObjectDisplayMode.Nothing,
            _ => Freexcel.App.UI.GridObjectDisplayMode.All
        };
        var keepObjectData = _options.ObjectsDisplay != FreexcelObjectDisplay.Nothing;
        SheetGrid.Charts = keepObjectData ? sheet?.Charts : null;
        SheetGrid.TextBoxes = keepObjectData ? sheet?.TextBoxes : null;
        SheetGrid.DrawingShapes = keepObjectData ? sheet?.DrawingShapes : null;
        SheetGrid.WorkbookTheme = _workbook.Theme;
        SheetGrid.Pictures = keepObjectData ? sheet?.Pictures : null;
        SheetGrid.WorksheetBackground = sheet?.BackgroundImage;
        SheetGrid.Sparklines = sheet?.Sparklines;
        SheetGrid.SparklineValues = sheet is null ? null : SparklineValuePlanner.BuildValues(sheet);
        SheetGrid.MergedRegions = sheet?.MergedRegions;
        SheetGrid.WorksheetViewMode = sheet?.ViewMode ?? WorksheetViewMode.Normal;
        SheetGrid.ShowGridLines = sheet?.ShowGridlines ?? true;
        SheetGrid.ShowHeaders = sheet?.ShowHeadings ?? true;
        SheetGrid.ShowRulers = sheet?.ShowRulers ?? true;
        _suppressViewOptionSync = true;
        try
        {
            if (ViewGridlinesChk is not null)
                ViewGridlinesChk.IsChecked = SheetGrid.ShowGridLines;
            if (PageLayoutViewGridlinesChk is not null)
                PageLayoutViewGridlinesChk.IsChecked = SheetGrid.ShowGridLines;
            if (ViewHeadersChk is not null)
                ViewHeadersChk.IsChecked = SheetGrid.ShowHeaders;
            if (PageLayoutViewHeadingsChk is not null)
                PageLayoutViewHeadingsChk.IsChecked = SheetGrid.ShowHeaders;
            if (ViewRulerChk is not null)
                ViewRulerChk.IsChecked = SheetGrid.ShowRulers;
            if (SplitViewBtn is not null)
                SplitViewBtn.IsChecked = sheet?.SplitRow is not null || sheet?.SplitColumn is not null;
        }
        finally
        {
            _suppressViewOptionSync = false;
        }
        if (PageLayoutPrintGridlinesChk is not null)
            PageLayoutPrintGridlinesChk.IsChecked = sheet?.PrintGridlines ?? false;
        if (PageLayoutPrintHeadingsChk is not null)
            PageLayoutPrintHeadingsChk.IsChecked = sheet?.PrintHeadings ?? false;
        SheetGrid.RowPageBreaks = sheet?.RowPageBreaks;
        SheetGrid.ColumnPageBreaks = sheet?.ColumnPageBreaks;
        SheetGrid.PrintArea = sheet?.PrintArea;
        SheetGrid.SplitRow = sheet?.SplitRow;
        SheetGrid.SplitColumn = sheet?.SplitColumn;
        SheetGrid.PageMargins = sheet?.PageMargins ?? WorksheetPageMargins.Narrow;
        SheetGrid.PageOrientation = sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait;
        SheetGrid.PaperSize = sheet?.PaperSize ?? WorksheetPaperSize.A4;

        // Adjust scrollbar range to the used data range + buffer, thumb to visible area
        UpdateScrollbarMaximums(sheet);
        var scrollableRowCount = CountScrollableRows(viewport, sheet);
        var scrollableColumnCount = CountScrollableColumns(viewport, sheet);
        VerticalScroll.ViewportSize   = scrollableRowCount;
        HorizontalScroll.ViewportSize = scrollableColumnCount;
        VerticalScroll.LargeChange    = Math.Max(1, scrollableRowCount);
        HorizontalScroll.LargeChange  = Math.Max(1, scrollableColumnCount);
        RefreshValidationDropdown();
        RefreshFormulaReferenceHighlights();
        RefreshPivotFieldListPane();
        RefreshSlicerTimelinePane();
    }

    private ViewportModel CreateViewport(Sheet? sheet, uint topRow, uint leftCol, double rowHeaderWidth)
    {
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel,
            AvailableWidth: CalculateViewportAvailableWidth(SheetGrid.ActualWidth, rowHeaderWidth, _zoomLevel),
            SplitPaneOffsets: GetSplitPaneViewportOffsets(sheet, topRow, leftCol));

        return _viewportService.GetViewport(_workbook, _currentSheetId, request);
    }

    private SplitPaneViewportOffsets? GetSplitPaneViewportOffsets(Sheet? sheet, uint topRow, uint leftCol)
    {
        if (sheet is null || (!sheet.SplitRow.HasValue && !sheet.SplitColumn.HasValue))
            return null;

        _splitPaneViewportOffsets.TryGetValue(sheet.Id, out var offsets);
        return new SplitPaneViewportOffsets(
            sheet.SplitColumn.HasValue ? offsets?.TopRightLeftCol ?? leftCol : null,
            sheet.SplitRow.HasValue ? offsets?.BottomLeftTopRow ?? topRow : null);
    }

    private static int CountScrollableRows(ViewportModel viewport, Sheet? sheet)
    {
        var frozenRows = sheet?.FrozenRows ?? 0;
        return Math.Max(1, viewport.RowMetrics.Count(row => row.Row > frozenRows));
    }

    private static int CountScrollableColumns(ViewportModel viewport, Sheet? sheet)
    {
        var frozenCols = sheet?.FrozenCols ?? 0;
        return Math.Max(1, viewport.ColMetrics.Count(column => column.Col > frozenCols));
    }

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue) =>
        ViewportScrollCalculator.CalculateViewportOrigin(sheet, verticalScrollValue, horizontalScrollValue);

    public static uint ScrollbarValueToWorksheetIndex(
        double scrollbarValue,
        uint frozenCount,
        uint absoluteLimit) =>
        ViewportScrollCalculator.ScrollbarValueToWorksheetIndex(scrollbarValue, frozenCount, absoluteLimit);

    public static uint WorksheetIndexToScrollbarValue(
        uint worksheetIndex,
        uint frozenCount) =>
        ViewportScrollCalculator.WorksheetIndexToScrollbarValue(worksheetIndex, frozenCount);

    public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount)
        => ViewportScrollCalculator.CalculateScrollableLimit(absoluteLimit, frozenCount);

    private static uint GetScrollableRowLimit(Sheet? sheet) =>
        ViewportScrollCalculator.GetScrollableRowLimit(sheet);

    private static uint GetScrollableColumnLimit(Sheet? sheet) =>
        ViewportScrollCalculator.GetScrollableColumnLimit(sheet);

    public static uint ClampViewportOrigin(double rawValue, uint absoluteLimit, uint visibleSpan)
        => ViewportScrollCalculator.ClampViewportOrigin(rawValue, absoluteLimit, visibleSpan);

    public static double CalculateViewportAvailableWidth(
        double gridWidth,
        double rowHeaderWidth,
        double zoomLevel) =>
        ViewportScrollCalculator.CalculateViewportAvailableWidth(gridWidth, rowHeaderWidth, zoomLevel);

    public static uint CalculateOpenedWorksheetScrollValue(
        uint? savedTopLeftIndex,
        uint fallbackIndex,
        uint absoluteLimit,
        uint frozenCount = 0) =>
        ViewportScrollCalculator.CalculateOpenedWorksheetScrollValue(
            savedTopLeftIndex,
            fallbackIndex,
            absoluteLimit,
            frozenCount);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit,
        uint visibleSpan) =>
        ViewportScrollCalculator.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit,
            visibleSpan);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex) =>
        ViewportScrollCalculator.CalculateScrollValueToRevealCell(targetIndex, firstVisibleIndex, lastVisibleIndex);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarMaximumForKeyboardReveal(
            currentMaximum,
            desiredScrollValue,
            absoluteLimit);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue) =>
        ViewportScrollCalculator.CalculateScrollbarMaximumForKeyboardReveal(currentMaximum, desiredScrollValue);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        double visibleSpan,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            visibleSpan,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateWheelScroll(
        double currentValue,
        double currentMaximum,
        int wheelNotches,
        double stepPerNotch,
        double visibleSpan,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateWheelScroll(
            currentValue,
            currentMaximum,
            wheelNotches,
            stepPerNotch,
            visibleSpan,
            absoluteLimit);

    public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan)
        => ViewportScrollCalculator.CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan);

    public static uint CalculateScrollbarMaximumForUsedRange(
        uint usedMax,
        uint visibleSpan,
        uint currentScrollValue,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarMaximumForUsedRange(
            usedMax,
            visibleSpan,
            currentScrollValue,
            absoluteLimit);

    private void UpdateScrollbarMaximums(Sheet? sheet)
    {
        // Compute the farthest cell with data
        uint usedMaxRow = 1, usedMaxCol = 1;
        if (sheet != null)
            foreach (var (addr, _) in sheet.GetUsedCells())
            {
                if (addr.Row > usedMaxRow) usedMaxRow = addr.Row;
                if (addr.Col > usedMaxCol) usedMaxCol = addr.Col;
            }

        var vp = SheetGrid.Viewport;
        uint visRows = (uint)Math.Max(10, vp is null ? 40 : CountScrollableRows(vp, sheet));
        uint visCols = (uint)Math.Max(5,  vp is null ? 15 : CountScrollableColumns(vp, sheet));

        var frozenRows = sheet?.FrozenRows ?? 0;
        var frozenCols = sheet?.FrozenCols ?? 0;
        uint currentRow = Math.Max(1, (uint)VerticalScroll.Value);
        uint currentCol = Math.Max(1, (uint)HorizontalScroll.Value);
        uint vMaxRow = CalculateScrollbarMaximumForUsedRange(
            WorksheetIndexToScrollbarValue(usedMaxRow, frozenRows),
            visRows,
            currentRow,
            GetScrollableRowLimit(sheet));
        uint vMaxCol = CalculateScrollbarMaximumForUsedRange(
            WorksheetIndexToScrollbarValue(usedMaxCol, frozenCols),
            visCols,
            currentCol,
            GetScrollableColumnLimit(sheet));

        VerticalScroll.Maximum   = Math.Min(vMaxRow, GetScrollableRowLimit(sheet));
        HorizontalScroll.Maximum = Math.Min(vMaxCol, GetScrollableColumnLimit(sheet));
    }
}
