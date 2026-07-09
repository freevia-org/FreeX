using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Scrolling the grid moves the column header the AutoFilter flyout is anchored to without
        // changing window activation, so dismiss the flyout here (its own deactivation handler covers
        // click-away cases). Scrolling within the flyout's own list does not raise this event.
        CloseAutoFilterDropdown();
        UpdateViewport();
        BroadcastScrollOffsetToSideBySidePartner();
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
        ViewportScrollbarUpdater.TryExtendFromArrowSmallIncrement(scrollBar, absoluteLimit);
    }

    private static bool TryExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        return ViewportScrollbarUpdater.TryExtendFromArrowSmallIncrement(scrollBar, absoluteLimit);
    }

    private void SheetGrid_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        int notches = ViewportScrollCalculator.NormalizeWheelNotches(e.Delta);
        var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (SheetGrid.Viewport is { } wheelViewport)
        {
            var wheelPos = e.GetPosition(SheetGrid);
            var wheelTarget = FreeX.App.UI.GridView.ResolveSplitPaneWheelTarget(
                wheelViewport,
                _currentSheetId,
                wheelPos,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight,
                horizontal);
            _activeSplitPaneRegion = wheelTarget.Region;
            horizontal = wheelTarget.Horizontal;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+Scroll = zoom
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + notches * 10));
            e.Handled = true;
            return;
        }

        if (SheetGrid.Viewport?.SplitPanes is not null &&
            !FreeX.App.UI.GridView.CanScrollSplitPaneRegion(_activeSplitPaneRegion, horizontal))
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

    private void OnAutofillEdgeScrollRequested(FreeX.App.Presentation.GridInteraction.GridAutoScrollRequest request)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (request.HorizontalDirection != 0)
        {
            var (maximum, value) = ViewportScrollCalculator.CalculateDragAutoScroll(
                HorizontalScroll.Value,
                HorizontalScroll.Maximum,
                request.HorizontalDirection,
                step: 1,
                HorizontalScroll.ViewportSize,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Maximum = maximum;
            HorizontalScroll.Value = value;
        }

        if (request.VerticalDirection != 0)
        {
            var (maximum, value) = ViewportScrollCalculator.CalculateDragAutoScroll(
                VerticalScroll.Value,
                VerticalScroll.Maximum,
                request.VerticalDirection,
                step: 1,
                VerticalScroll.ViewportSize,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Maximum = maximum;
            VerticalScroll.Value = value;
        }
    }

    private bool TryScrollIndependentSplitPane(bool horizontal, int notches)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return false;

        if (horizontal && _activeSplitPaneRegion == FreeX.App.UI.SplitPaneRegion.TopRight)
        {
            var chrome = FreeX.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.HorizontalTopRight is not { } horizontalTopRightScrollbar)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.TopRightLeftCol
                : null;
            var target = FreeX.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                horizontalTopRightScrollbar,
                current ?? Math.Max(1, (uint)HorizontalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return true;
        }

        if (!horizontal && _activeSplitPaneRegion == FreeX.App.UI.SplitPaneRegion.BottomLeft)
        {
            var chrome = FreeX.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.VerticalBottomLeft is not { } verticalBottomLeftScrollbar)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.BottomLeftTopRow
                : null;
            var target = FreeX.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                verticalBottomLeftScrollbar,
                current ?? Math.Max(1, (uint)VerticalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { BottomLeftTopRow = target.Index };
            UpdateViewport();
            return true;
        }

        return false;
    }

    private void OnSplitPaneScrollbarScrolled(FreeX.App.UI.SplitPaneScrollbarScrollTarget target)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return;

        _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets);
        offsets ??= new SplitPaneViewportOffsets();

        if (target is
            {
                Region: FreeX.App.UI.SplitPaneRegion.TopRight,
                Orientation: FreeX.App.UI.SplitPaneScrollbarOrientation.Horizontal
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return;
        }

        if (target is
            {
                Region: FreeX.App.UI.SplitPaneRegion.BottomLeft,
                Orientation: FreeX.App.UI.SplitPaneScrollbarOrientation.Vertical
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

        var plan = ViewportScrollCalculator.PlanCellReveal(
            vp,
            sheet,
            addr,
            VerticalScroll.Maximum,
            HorizontalScroll.Maximum);
        if (plan.Vertical.ShouldScroll)
        {
            VerticalScroll.Maximum = plan.Vertical.Maximum;
            VerticalScroll.Value = plan.Vertical.Value;
        }

        if (plan.Horizontal.ShouldScroll)
        {
            HorizontalScroll.Maximum = plan.Horizontal.Maximum;
            HorizontalScroll.Value = plan.Horizontal.Value;
        }

        // The target cell may be out of view in an independently-scrolled split pane (bottom-left's
        // own vertical offset or top-right's own horizontal offset) that the main scrollbars above
        // cannot reach -- update that pane's offset directly, mirroring TryScrollIndependentSplitPane
        // / OnSplitPaneScrollbarScrolled.
        if (plan.BottomLeftTopRow is not null || plan.TopRightLeftCol is not null)
        {
            var offsets = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var existing)
                ? existing
                : new SplitPaneViewportOffsets();
            if (plan.BottomLeftTopRow is { } newBottomLeftTopRow)
                offsets = offsets with { BottomLeftTopRow = newBottomLeftTopRow };
            if (plan.TopRightLeftCol is { } newTopRightLeftCol)
                offsets = offsets with { TopRightLeftCol = newTopRightLeftCol };
            _splitPaneViewportOffsets[_currentSheetId] = offsets;
            UpdateViewport();
        }
    }

    private readonly record struct TableContextRefreshKey(
        Sheet? Sheet,
        GridRange? SelectedRange,
        ulong NavigationRevision,
        Visibility TableDesignVisibility);

    private readonly record struct ChartContextRefreshKey(
        Sheet? Sheet,
        Guid SelectedObjectId,
        FreeX.App.UI.ObjectKind SelectedObjectKind,
        bool HasVisibleNormalChart,
        ulong NavigationRevision,
        Visibility ChartDesignVisibility,
        Visibility ChartFormatVisibility);

    private readonly record struct DrawingObjectContextRefreshKey(
        Sheet? Sheet,
        Guid SelectedObjectId,
        FreeX.App.UI.ObjectKind SelectedObjectKind,
        ulong NavigationRevision,
        Visibility ShapeFormatVisibility,
        Visibility PictureFormatVisibility,
        bool ShapeGradientEnabled,
        bool ShapeEffectsEnabled,
        bool PictureCropEnabled);

    private readonly record struct PivotFieldListRefreshKey(
        Sheet? Sheet,
        GridRange? SelectedRange,
        ulong NavigationRevision,
        Visibility PaneVisibility,
        bool HasPendingLayout);

    private readonly record struct SlicerTimelineRefreshKey(
        Workbook Workbook,
        ulong NavigationRevision,
        bool Dismissed,
        Visibility PaneVisibility,
        int SlicerCount,
        int TimelineCount);

    private TableContextRefreshKey? _lastViewportTableContextRefreshKey;
    private ChartContextRefreshKey? _lastViewportChartContextRefreshKey;
    private DrawingObjectContextRefreshKey? _lastViewportDrawingObjectContextRefreshKey;
    private PivotFieldListRefreshKey? _lastViewportPivotFieldListRefreshKey;
    private SlicerTimelineRefreshKey? _lastViewportSlicerTimelineRefreshKey;

    // ── Navigation helpers ────────────────────────────────────────────────────

    private void UpdateViewport()
    {
        if (SheetGrid == null || _viewportService == null) return;

        // Dismiss the AutoFilter dropdown flyout if we've moved to a different sheet.
        CloseAutoFilterDropdownOnSheetChange();

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null)
        {
            SyncWorkbookActiveSheetIndex();
            SyncZoomFromSheet(sheet.ZoomPercent);
            SyncPageLayoutScaleToFitControls(sheet);
        }
        EnsureActiveCellSelection(sheet);

        var (topRow, leftCol) = CalculateViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);
        topRow = ClampViewportOrigin(
            topRow,
            CellAddress.MaxRow,
            SheetGrid.Viewport is null ? 40 : (uint)CountScrollableRows(SheetGrid.Viewport, sheet));
        leftCol = ClampViewportOrigin(
            leftCol,
            CellAddress.MaxCol,
            SheetGrid.Viewport is null ? 15 : (uint)CountScrollableColumns(SheetGrid.Viewport, sheet));
        if (sheet is not null)
        {
            sheet.ViewTopRow = topRow;
            sheet.ViewLeftCol = leftCol;
        }

        // Compute the correct row-header width before building the viewport so it is
        // built exactly once, even when crossing a row-digit boundary (e.g. 999→1000).
        var rowHeaderWidth = ComputeCorrectRowHeaderWidth(sheet, topRow, leftCol);
        var viewport = CreateViewport(sheet, topRow, leftCol, rowHeaderWidth);

        SheetGrid.Viewport = viewport;
        SheetGrid.PinnedNoteAddresses = sheet is null
            ? null
            : sheet.ShownComments.Count == 0
                ? null
                : sheet.ShownComments
                    .Select(a => (a.Row, a.Col))
                    .ToHashSet<(uint Row, uint Col)>();
        SheetGrid.HiddenRows = sheet?.HiddenRows;
        SheetGrid.HiddenColumns = sheet?.HiddenCols;
        // Feed the page-break preview overlay the sheet's real "effectively hidden" predicates
        // (AutoFilter-hidden rows + collapsed outline groups), not just the manual hidden sets
        // above, so its pagination matches the real print output (R15-print-preview-interaction-2).
        SheetGrid.SheetIsRowHiddenPredicate = sheet is null ? null : sheet.IsRowEffectivelyHidden;
        SheetGrid.SheetIsColHiddenPredicate = sheet is null ? null : sheet.IsColEffectivelyHidden;
        SheetGrid.AutoFilterRange = sheet is not null &&
                                    AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var autoFilterRange)
            ? autoFilterRange
            : null;
        IReadOnlyList<PivotHeaderDropdownTarget> pivotHeaderDropdownTargets = sheet is null
            ? []
            : PivotHeaderDropdownPlanner.BuildTargets(_workbook, sheet);
        _pivotHeaderDropdownTargets = BuildPivotHeaderDropdownTargetLookup(pivotHeaderDropdownTargets);
        SheetGrid.PivotHeaderDropdowns = pivotHeaderDropdownTargets
            .Select(target => new FreeX.App.UI.PivotHeaderDropdownButton(target.HeaderCell, target.IsActive))
            .ToList();
        SheetGrid.PivotRowLabelAdornments = sheet is null
            ? []
            : PivotRowLabelAdornmentPlanner.BuildAdornments(_workbook, sheet);
        SheetGrid.FormulaTraceSheetId = _currentSheetId;
        SheetGrid.FormulaTraceArrows = _formulaTraceArrows;
        SheetGrid.HyperlinkCells = sheet is null
            ? null
            : sheet.Hyperlinks.Keys
                .Select(address => new CellAddress(default, address.Row, address.Col))
                .ToHashSet();
        SheetGrid.ObjectDisplayMode = _options.ObjectsDisplay switch
        {
            FreeXObjectDisplay.Placeholders => FreeX.App.UI.GridObjectDisplayMode.Placeholders,
            FreeXObjectDisplay.Nothing => FreeX.App.UI.GridObjectDisplayMode.Nothing,
            _ => FreeX.App.UI.GridObjectDisplayMode.All
        };
        var keepObjectData = _options.ObjectsDisplay != FreeXObjectDisplay.Nothing;
        SheetGrid.Charts = keepObjectData ? sheet?.Charts : null;
        SheetGrid.TextBoxes = keepObjectData ? sheet?.TextBoxes : null;
        SheetGrid.DrawingShapes = keepObjectData ? sheet?.DrawingShapes : null;
        SheetGrid.WorkbookTheme = _workbook.Theme;
        SheetGrid.Pictures = keepObjectData ? sheet?.Pictures : null;
        SheetGrid.DrawingObjectZOrder = keepObjectData ? sheet?.DrawingObjectZOrder : null;
        var nativeVisualFilters = keepObjectData && sheet is not null
            ? SlicerTimelinePlanner.GetNativeVisualFilters(_workbook, sheet)
            : null;
        if (nativeVisualFilters is { Slicers.Count: > 0 })
        {
            // Resolve each slicer's available items (table-column distinct values or pivot cache shared
            // items) into AvailableItems just before render, mirroring the form-control selected-text pass.
            FreeX.Core.Commands.SlicerItemResolver.PopulateAvailableItems(_workbook);
        }
        SheetGrid.NativeSlicers = nativeVisualFilters?.Slicers;
        SheetGrid.NativeTimelines = nativeVisualFilters?.Timelines;
        if (keepObjectData && sheet is not null && sheet.FormControls.Count > 0)
        {
            // Resolve each list control's selected-item text (ListFillRange[SelectedIndex]) into the
            // render-model's SelectedText so the GridView draws the selection without raw workbook access.
            FreeX.Core.Commands.FormControlListResolver.PopulateSelectedText(sheet, _workbook);
        }
        SheetGrid.FormControls = keepObjectData ? sheet?.FormControls : null;
        SheetGrid.WorksheetBackground = sheet?.BackgroundImage;
        SheetGrid.ActiveSheetId = _currentSheetId;
        // Mirror the Avalonia shell (MainWindow.cs MapCellFlowDirection/MapCellTextAlignment): bind the
        // active sheet's Sheet.IsRightToLeft (Excel's sheetView rightToLeft="1") to the grid so
        // Context-reading-order cells resolve to RTL instead of always defaulting to LTR (P28).
        SheetGrid.IsSheetRightToLeft = sheet?.IsRightToLeft ?? false;
        SheetGrid.SheetRichTextRuns = sheet?.RichTextRuns;
        SheetGrid.Sparklines = sheet?.Sparklines;
        SheetGrid.SparklineValues = sheet is null
            ? null
            : _sparklineValueCache.GetOrCreate(
                sheet,
                _navigationCacheRevision,
                () => SparklineSeriesReader.BuildValues(sheet));
        SheetGrid.MergedRegions = sheet?.MergedRegions;
        SheetGrid.WorksheetViewMode = sheet?.ViewMode ?? WorksheetViewMode.Normal;
        SheetGrid.ShowGridLines = sheet?.ShowGridlines ?? true;
        SheetGrid.ShowHeaders = sheet?.ShowHeadings ?? true;
        SheetGrid.ShowRulers = sheet?.ShowRulers ?? true;
        _suppressViewOptionSync = true;
        try
        {
            // Push the worksheet's view options into the neutral state store; the renderer-bound
            // View / Page Layout check boxes update from it. ("Gridlines"/"Headings"/"Ruler" live on
            // the View tab; "View Gridlines"/"View Headings" on the Page Layout tab.)
            _ribbonState.SetChecked("Gridlines", SheetGrid.ShowGridLines);
            _ribbonState.SetChecked("View Gridlines", SheetGrid.ShowGridLines);
            _ribbonState.SetChecked("Headings", SheetGrid.ShowHeaders);
            _ribbonState.SetChecked("View Headings", SheetGrid.ShowHeaders);
            _ribbonState.SetChecked("Ruler", SheetGrid.ShowRulers);
            _ribbonState.SetEnabled("Ruler", sheet?.ViewMode == WorksheetViewMode.PageLayout);
            _ribbonState.SetChecked("Split", sheet?.SplitRow is not null || sheet?.SplitColumn is not null);
            SyncWorkbookViewModeToggleState(SheetGrid.WorksheetViewMode);
            RefreshViewWindowCommandState();
        }
        finally
        {
            _suppressViewOptionSync = false;
        }
        _ribbonState.SetChecked("Print Gridlines", sheet?.PrintGridlines ?? false);
        _ribbonState.SetChecked("Print Headings", sheet?.PrintHeadings ?? false);
        SheetGrid.RowPageBreaks = sheet?.RowPageBreaks;
        SheetGrid.ColumnPageBreaks = sheet?.ColumnPageBreaks;
        SheetGrid.PrintArea = sheet?.PrintArea;
        SheetGrid.PagePreviewRange = CalculatePagePreviewRange(sheet, viewport);
        SheetGrid.SplitRow = sheet?.SplitRow;
        SheetGrid.SplitColumn = sheet?.SplitColumn;
        SheetGrid.PageMargins = sheet?.PageMargins ?? WorksheetPageMargins.Narrow;
        SheetGrid.PageOrientation = sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait;
        SheetGrid.PaperSize = sheet?.PaperSize ?? WorksheetPaperSize.A4;
        SheetGrid.PageOrder = sheet?.PageOrder ?? WorksheetPageOrder.DownThenOver;
        SheetGrid.ScaleToFit = sheet?.ScaleToFit ?? WorksheetScaleToFit.Default;
        SheetGrid.PrintTitleRows = sheet?.PrintTitleRows;
        SheetGrid.PrintTitleColumns = sheet?.PrintTitleColumns;
        SheetGrid.SheetRowHeights = sheet?.RowHeights;
        SheetGrid.SheetDefaultRowHeight = sheet?.DefaultRowHeight ?? PagePaginationPlanner.NominalRowHeight;
        SheetGrid.SheetColumnWidths = sheet?.ColumnWidths;
        SheetGrid.SheetDefaultColumnWidth = sheet?.DefaultColumnWidth ?? 8.43;
        SheetGrid.SheetHeaderMargin = sheet?.HeaderMargin ?? 0.3;
        SheetGrid.SheetFooterMargin = sheet?.FooterMargin ?? 0.3;

        // Adjust scrollbar range to the used data range + buffer, thumb to visible area
        UpdateScrollbarMaximums(sheet);
        var scrollableRowCount = CountScrollableRows(viewport, sheet);
        var scrollableColumnCount = CountScrollableColumns(viewport, sheet);
        VerticalScroll.ViewportSize   = scrollableRowCount;
        HorizontalScroll.ViewportSize = scrollableColumnCount;
        VerticalScroll.LargeChange    = Math.Max(1, scrollableRowCount);
        HorizontalScroll.LargeChange  = Math.Max(1, scrollableColumnCount);
        RefreshViewportValidationDropdown(sheet);
        RefreshViewportFormulaReferenceHighlights();
        RefreshViewportTableContextualTab(sheet);
        RefreshViewportDrawingObjectContextualTabs(sheet);
        RefreshViewportChartContextualTabs(sheet);
        RefreshViewportPivotFieldListPane(sheet);
        RefreshViewportSlicerTimelinePane();
        RefreshTextBoxInlineEditorPosition();
        UpdateChartsheetPresentation(sheet);
    }

    private static IReadOnlyDictionary<(uint Row, uint Col), PivotHeaderDropdownTarget> BuildPivotHeaderDropdownTargetLookup(
        IReadOnlyList<PivotHeaderDropdownTarget> targets)
    {
        var lookup = new Dictionary<(uint Row, uint Col), PivotHeaderDropdownTarget>(targets.Count);
        foreach (var target in targets)
            lookup[(target.HeaderCell.Row, target.HeaderCell.Col)] = target;

        return lookup;
    }

    private void RefreshViewportValidationDropdown(Sheet? sheet)
    {
        if (_validationDropdown?.Visibility == Visibility.Visible ||
            sheet?.DataValidations.Count > 0)
        {
            RefreshValidationDropdown();
            RefreshDvInputMessage();
        }
    }

    private void RefreshViewportFormulaReferenceHighlights()
    {
        if (GetFormulaReferenceHighlightEditor() is not null ||
            _formulaReferenceGridOverlayActiveCount != 0)
        {
            RefreshFormulaReferenceHighlights();
        }
    }

    private void RefreshViewportTableContextualTab(Sheet? sheet)
    {
        var key = CreateTableContextRefreshKey(sheet);
        if (_lastViewportTableContextRefreshKey == key)
            return;

        RefreshTableContextualTab();
        _lastViewportTableContextRefreshKey = CreateTableContextRefreshKey(sheet);
    }

    private TableContextRefreshKey CreateTableContextRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedRange,
            _navigationCacheRevision,
            TableDesignTab?.Visibility ?? Visibility.Collapsed);

    private void RefreshViewportChartContextualTabs(Sheet? sheet)
    {
        var key = CreateChartContextRefreshKey(sheet);
        if (_lastViewportChartContextRefreshKey == key)
            return;

        RefreshChartContextualTabs();
        _lastViewportChartContextRefreshKey = CreateChartContextRefreshKey(sheet);
    }

    private ChartContextRefreshKey CreateChartContextRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedObjectId,
            SheetGrid.SelectedObjectKind,
            ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet()),
            _navigationCacheRevision,
            ChartDesignTab?.Visibility ?? Visibility.Collapsed,
            ChartFormatTab?.Visibility ?? Visibility.Collapsed);

    private void RefreshViewportDrawingObjectContextualTabs(Sheet? sheet)
    {
        var key = CreateDrawingObjectContextRefreshKey(sheet);
        if (_lastViewportDrawingObjectContextRefreshKey == key)
            return;

        RefreshDrawingObjectContextualTabs();
        _lastViewportDrawingObjectContextRefreshKey = CreateDrawingObjectContextRefreshKey(sheet);
    }

    private DrawingObjectContextRefreshKey CreateDrawingObjectContextRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedObjectId,
            SheetGrid.SelectedObjectKind,
            _navigationCacheRevision,
            ShapeFormatTab?.Visibility ?? Visibility.Collapsed,
            PictureFormatTab?.Visibility ?? Visibility.Collapsed,
            _ribbonState.GetState("Shape Gradient").IsEnabled,
            _ribbonState.GetState("Shape Effects").IsEnabled,
            _ribbonState.GetState("Crop Picture").IsEnabled);

    private void RefreshViewportPivotFieldListPane(Sheet? sheet)
    {
        var key = CreatePivotFieldListRefreshKey(sheet);
        if (_lastViewportPivotFieldListRefreshKey == key)
            return;

        RefreshPivotFieldListPane();
        _lastViewportPivotFieldListRefreshKey = CreatePivotFieldListRefreshKey(sheet);
    }

    private PivotFieldListRefreshKey CreatePivotFieldListRefreshKey(Sheet? sheet) =>
        new(
            sheet,
            SheetGrid.SelectedRange,
            _navigationCacheRevision,
            PivotFieldListPane?.Visibility ?? Visibility.Collapsed,
            _pendingPivotLayout is not null);

    private void RefreshViewportSlicerTimelinePane()
    {
        var key = CreateSlicerTimelineRefreshKey();
        if (_lastViewportSlicerTimelineRefreshKey == key)
            return;

        RefreshSlicerTimelinePane();
        _lastViewportSlicerTimelineRefreshKey = CreateSlicerTimelineRefreshKey();
    }

    private SlicerTimelineRefreshKey CreateSlicerTimelineRefreshKey() =>
        new(
            _workbook,
            _navigationCacheRevision,
            _slicerTimelinePaneDismissed,
            SlicerTimelinePane?.Visibility ?? Visibility.Collapsed,
            _workbook.Slicers.Count,
            _workbook.Timelines.Count);

    /// <summary>
    /// Returns the row-header width that will be needed for the given top row, by querying
    /// only the cheap row-metric and outline-group data — no cell materialization occurs.
    /// This prevents the viewport from being built twice when crossing a row-digit boundary
    /// (e.g. row 999→1000).
    /// </summary>
    private double ComputeCorrectRowHeaderWidth(Sheet? sheet, uint topRow, uint leftCol)
    {
        if (!SheetGrid.ShowHeaders)
            return 0.0;

        // Use a placeholder width for the first pass — the available width passed here
        // does not affect row metrics, so any reasonable value works.
        var placeholderWidth = SheetGrid.ActualRowHeaderWidth;
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel,
            AvailableWidth: CalculateViewportAvailableWidth(SheetGrid.ActualWidth, placeholderWidth, _zoomLevel),
            IncludeObjects: false,
            SplitPaneOffsets: null);

        var (lastVisibleRow, rowOutlineGroups) =
            _viewportService.ComputeRowMetricsSummary(_workbook, _currentSheetId, request);
        return FreeX.App.UI.GridView.CalculateRowHeaderWidth(lastVisibleRow, rowOutlineGroups);
    }

    private ViewportModel CreateViewport(Sheet? sheet, uint topRow, uint leftCol, double rowHeaderWidth)
    {
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel,
            AvailableWidth: CalculateViewportAvailableWidth(SheetGrid.ActualWidth, rowHeaderWidth, _zoomLevel),
            IncludeObjects: _options.ObjectsDisplay == FreeXObjectDisplay.All,
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

    private static GridRange? CalculatePagePreviewRange(Sheet? sheet, ViewportModel viewport)
    {
        if (sheet is null || sheet.PrintArea is not null)
            return null;

        var usedRange = sheet.GetUsedRange();
        if (viewport.RowMetrics.Count == 0 || viewport.ColMetrics.Count == 0)
            return usedRange;

        var firstRow = uint.MaxValue;
        var lastRow = 0u;
        foreach (var row in viewport.RowMetrics)
        {
            firstRow = Math.Min(firstRow, row.Row);
            lastRow = Math.Max(lastRow, row.Row);
        }

        var firstColumn = uint.MaxValue;
        var lastColumn = 0u;
        foreach (var column in viewport.ColMetrics)
        {
            firstColumn = Math.Min(firstColumn, column.Col);
            lastColumn = Math.Max(lastColumn, column.Col);
        }

        if (firstRow == uint.MaxValue || firstColumn == uint.MaxValue || lastRow == 0 || lastColumn == 0)
            return usedRange;

        var visibleRowSpan = lastRow - firstRow + 1;
        var visibleColumnSpan = lastColumn - firstColumn + 1;
        var startRow = Math.Min(usedRange?.Start.Row ?? 1u, firstRow);
        var startColumn = Math.Min(usedRange?.Start.Col ?? 1u, firstColumn);
        var endRow = Math.Max(
            Math.Max(usedRange?.End.Row ?? 1u, lastRow),
            AddWithLimit(lastRow, visibleRowSpan, CellAddress.MaxRow));
        var endColumn = Math.Max(
            Math.Max(usedRange?.End.Col ?? 1u, lastColumn),
            AddWithLimit(lastColumn, visibleColumnSpan, CellAddress.MaxCol));

        return new GridRange(
            new CellAddress(sheet.Id, startRow, startColumn),
            new CellAddress(sheet.Id, endRow, endColumn));
    }

    private static uint AddWithLimit(uint value, uint addend, uint limit)
    {
        if (value >= limit)
            return limit;

        var remaining = limit - value;
        return addend >= remaining ? limit : value + addend;
    }

    private static int CountScrollableRows(ViewportModel viewport, Sheet? sheet)
    {
        var frozenRows = sheet?.FrozenRows ?? 0;
        var count = 0;
        foreach (var row in viewport.RowMetrics)
        {
            if (row.Row > frozenRows)
                count++;
        }

        return Math.Max(1, count);
    }

    private static int CountScrollableColumns(ViewportModel viewport, Sheet? sheet)
    {
        var frozenCols = sheet?.FrozenCols ?? 0;
        var count = 0;
        foreach (var column in viewport.ColMetrics)
        {
            if (column.Col > frozenCols)
                count++;
        }

        return Math.Max(1, count);
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

    public static (double Maximum, double Value) CalculateDragAutoScroll(
        double currentValue,
        double currentMaximum,
        int direction,
        double step,
        double visibleSpan,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateDragAutoScroll(
            currentValue,
            currentMaximum,
            direction,
            step,
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

    public static (uint UsedMaxRow, uint UsedMaxCol) CalculateUsedRangeExtents(Sheet? sheet)
    {
        var usedRange = sheet?.GetUsedRange();
        return usedRange is null
            ? (1u, 1u)
            : (usedRange.Value.End.Row, usedRange.Value.End.Col);
    }

    private void UpdateScrollbarMaximums(Sheet? sheet)
    {
        var (usedMaxRow, usedMaxCol) = CalculateUsedRangeExtents(sheet);

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
