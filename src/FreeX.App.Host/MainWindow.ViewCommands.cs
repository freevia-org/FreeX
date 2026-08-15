using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_SIZE = 0xF000;
    private const int SC_MOVE = 0xF010;

    private void ViewGridlinesChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (!TryExecuteGroupedWorksheetViewState(
                targetSheetIds,
                () => _session.SetShowGridlines(chk.IsChecked == true),
                "Gridlines"))
            return;

        UpdateViewport();
    }

    private void ViewHeadersChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (!TryExecuteGroupedWorksheetViewState(
                targetSheetIds,
                () => _session.SetShowHeadings(chk.IsChecked == true),
                "Headings"))
            return;

        UpdateViewport();
    }

    private void ViewRulerChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;
        if (GetEffectiveViewState(sheet).ViewMode != WorksheetViewMode.PageLayout)
        {
            chk.IsChecked = GetEffectiveViewState(sheet).ShowRulers;
            return;
        }

        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (!TryExecuteGroupedWorksheetViewState(
                targetSheetIds,
                () => _session.SetShowRulers(chk.IsChecked == true),
                "Ruler"))
            return;

        UpdateViewport();
    }

    private void ViewFormulaBarChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAppViewOptionSync) return;
        if (sender is not System.Windows.Controls.CheckBox chk || FormulaBarBorder is null) return;

        MutateRuntimeOptions(options => options.ShowFormulaBar = chk.IsChecked == true);
        FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;

        // Show Formula Bar is an Excel-instance-wide display preference, not scoped to this
        // window's own document -- every other open window (any document) must reflect it
        // immediately too, exactly like real Excel (R83-app-view-modes-5-2).
        _windowRegistry?.BroadcastFormulaBarVisibility(this, _options.ShowFormulaBar);
    }

    private void ToggleOutlineSymbolsShortcut()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var next = !(sheet.ShowOutlineSymbols ?? true);
        if (!TryExecuteWorksheetLayout(
                () => _session.SetShowOutlineSymbols(next),
                "Show Outline Symbols"))
            return;

        UpdateViewport();
    }

    private void NormalViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.Normal);

    private void PageBreakPreviewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);

    private void PageLayoutViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageLayout);

    private bool TryExecuteGroupedWorksheetViewState(
        IReadOnlyList<SheetId> targetSheetIds,
        Func<WorkbookCellEditResult> execute,
        string title)
    {
        SynchronizeWorkbookSessionSelection();
        SynchronizeWorkbookSessionViewState(targetSheetIds);
        return CompleteWorksheetSessionCommand(execute(), title, targetSheetIds);
    }

    private void SetWorksheetViewMode(WorksheetViewMode viewMode)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (!TryExecuteGroupedWorksheetViewState(
                targetSheetIds,
                () => _session.SetWorksheetViewMode(viewMode),
                "Workbook View"))
            return;

        UpdateViewport();
    }

    private void SyncWorkbookViewModeToggleState(WorksheetViewMode viewMode)
    {
        var state = WorksheetViewModeUiStatePlanner.Build(viewMode);
        _ribbonState.SetChecked("Normal", state.NormalChecked);
        _ribbonState.SetChecked("Page Layout", state.PageLayoutChecked);
        _ribbonState.SetChecked("Page Break Preview", state.PageBreakPreviewChecked);

        SyncStatusViewShortcutState(state);
    }

    private void SyncStatusViewShortcutState(WorksheetViewModeUiState state)
    {
        if (StatusNormalViewButton is not null)
            StatusNormalViewButton.IsChecked = state.NormalChecked;
        if (StatusPageLayoutViewButton is not null)
            StatusPageLayoutViewButton.IsChecked = state.PageLayoutChecked;
        if (StatusPageBreakPreviewButton is not null)
            StatusPageBreakPreviewButton.IsChecked = state.PageBreakPreviewChecked;
    }

    private void CustomViewsBtn_Click(object sender, RoutedEventArgs e)
    {
        SyncWorkbookActiveSheetIndex();
        var dialog = new CustomViewsDialog(_workbook, ExecuteCustomViewDialogCommand) { Owner = this };
        dialog.ShowDialog();
        if (dialog.ViewApplied)
        {
            ApplyCustomViewWorkbookViewState();
            RefreshStatusBar();
            FocusSheetGridIfNeeded();
        }
    }

    private void ApplyCustomViewWorkbookViewState()
    {
        var selectedActiveSheet = TrySelectWorkbookActiveSheet();
        if (selectedActiveSheet)
            RefreshSheetTabs();

        // A Custom View can restore ViewMode/Zoom/Gridlines/Headings/Rulers on ANY sheet named in
        // the saved view (ApplyCustomViewCommand loops over every WorksheetCustomViewState it
        // holds), not just the active one -- so unlike a single-sheet View-tab toggle, this window
        // can't hand a narrow target-sheet-id list to SyncWindowViewState. Forget this window's
        // entire per-sheet view-state cache instead, so GetEffectiveViewState (via
        // ApplyOpenedWorksheetViewState -> UpdateViewport below) reseeds fresh from the just-applied
        // Sheet fields rather than replaying whatever this window had cached before Apply
        // (R88-window-seed-order-guard-sweep-2) -- the same invalidation every other View-tab
        // handler performs (via SyncWindowViewState) right after mutating
        // Sheet.ViewMode/ZoomPercent/ShowGridlines/ShowHeadings/ShowRulers.
        _worksheetViewStates.Clear();

        _worksheetSelections.Remove(_currentSheetId);
        ApplyOpenedWorksheetViewState();
    }

    private void ArrangeAllPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void ArrangeAllContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsChecked = ArrangeAllMenuPlanner.IsChecked(item.Tag, _workbook.WindowArrangement);
    }

    private void ArrangeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!ArrangeAllMenuPlanner.TryParseArrangement(
                (sender as System.Windows.Controls.MenuItem)?.Tag,
                out var arrangement))
            return;

        if (!TryExecuteCommand(new SetWorkbookWindowArrangementCommand(arrangement), "Arrange Windows"))
            return;

        var workArea = SystemParameters.WorkArea;
        _windowRegistry?.ArrangeVisibleWindows(arrangement, workArea.Width, workArea.Height);
        RefreshViewWindowCommandState();
    }

    private void RefreshViewWindowCommandState()
    {
        ApplyLiveWindowCommandState();
        InvalidateVisibleKeyTipElementCache();
    }

    private void ApplyLiveWindowCommandState()
    {
        ApplyRibbonWindowCommandState(
            "New Window",
            isEnabled: true,
            UiText.Get("MainWindow_TooltipDescription_OpenAnotherLiveWindowForThisWorkbook"));

        var canSwitchWindows = (_windowRegistry?.VisibleCount ?? 1) > 1;
        ApplyRibbonWindowCommandState(
            "Switch Windows",
            canSwitchWindows,
            UiText.Get(canSwitchWindows
                ? "MainWindow_TooltipDescription_SwitchToAnotherVisibleWorkbookWindow"
                : "MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow"));

        // Hide is available only while another window would remain visible.
        var canHide = (_windowRegistry?.VisibleCount ?? 1) > 1;
        ApplyRibbonWindowCommandState(
            "Hide",
            canHide,
            UiText.Get(canHide
                ? "MainWindow_TooltipDescription_HideThisWorkbookWindowFromView"
                : "MainWindow_TooltipDescription_UnavailableHideRequiresSecondVisibleWindow"));

        // Unhide is available only when at least one window is hidden.
        var canUnhide = (_windowRegistry?.HiddenWindows.Count ?? 0) > 0;
        ApplyRibbonWindowCommandState(
            "Unhide",
            canUnhide,
            UiText.Get(canUnhide
                ? "MainWindow_TooltipDescription_RestoreAHiddenWorkbookWindow"
                : "MainWindow_TooltipDescription_UnavailableUnhideRequiresAHiddenWindow"));

        var anySideBySideActive = _windowRegistry?.IsSideBySideActive ?? false;
        var sideBySideActive = _windowRegistry?.IsSideBySideActiveFor(this) ?? false;
        var sideBySideState = WorkbookSideBySideCommandStatePlanner.Build(
            _windowRegistry?.VisibleCount ?? 1,
            anySideBySideActive,
            sideBySideActive,
            _windowRegistry?.IsSynchronousScrollActiveFor(this) ?? false);

        // Reset Window Position belongs to the active pair and resets both members together.
        ApplyRibbonWindowCommandState(
            "Reset Window Position",
            sideBySideState.ResetWindowPositionEnabled,
            UiText.Get("MainWindow_TooltipDescription_ResetThisWindowToAStandardSizeAndPosition"));

        // View Side by Side needs a second visible window to pair with.
        ApplyRibbonWindowToggleState(
            "View Side by Side",
            sideBySideState.ViewSideBySideEnabled,
            sideBySideState.ViewSideBySideChecked,
            UiText.Get(sideBySideState.ViewSideBySideEnabled
                ? "MainWindow_TooltipDescription_TileThisWindowAndAnotherSideBySideToCompareThem"
                : "MainWindow_TooltipDescription_UnavailableViewSideBySideRequiresSecondVisibleWindow"));

        // Synchronous Scrolling is only meaningful while side-by-side is active.
        ApplyRibbonWindowToggleState(
            "Synchronous Scrolling",
            sideBySideState.SynchronousScrollingEnabled,
            sideBySideState.SynchronousScrollingChecked,
            UiText.Get(sideBySideState.SynchronousScrollingEnabled
                ? "MainWindow_TooltipDescription_ScrollBothSideBySideWindowsTogether"
                : "MainWindow_TooltipDescription_UnavailableSynchronousScrollingRequiresViewSideBySide"));
    }

    /// <summary>Reflects the registry's side-by-side state onto the toggle button without re-toggling it.</summary>
    private void SyncViewSideBySideToggleState() => ApplyLiveWindowCommandState();

    // Window commands carry both enablement (store-driven) and a context-specific help/tooltip
    // description. Enablement flows through the neutral store; the description (not part of
    // RibbonCommandState) is set on the rendered control directly.
    private void ApplyRibbonWindowCommandState(
        string commandId,
        bool isEnabled,
        string description)
    {
        _ribbonState.SetEnabled(commandId, isEnabled);
        ApplyRibbonCommandDescription(commandId, description);
    }

    private void ApplyRibbonWindowToggleState(
        string commandId,
        bool isEnabled,
        bool isChecked,
        string description)
    {
        _ribbonState.SetEnabled(commandId, isEnabled);
        _ribbonState.SetChecked(commandId, isChecked);
        ApplyRibbonCommandDescription(commandId, description);
    }

    /// <summary>Updates a rendered ribbon control's tooltip/help description (not part of
    /// <see cref="Free.Shared.Ribbon.RibbonCommandState"/>) for the current command context.</summary>
    private void ApplyRibbonCommandDescription(string commandId, string description)
    {
        if (FindRenderedRibbonControl(commandId) is not { } control)
            return;

        RibbonTooltip.SetDescription(control, description);
        AutomationProperties.SetHelpText(control, description);
    }

    private void FreezePanesPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        ApplyFreezePanes(_session.FreezePanesAtActiveCell);
    }
    private void FreezeTopRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyFreezePanes(_session.FreezeTopRow);
    }
    private void FreezeFirstColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyFreezePanes(_session.FreezeFirstColumn);
    }
    private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyFreezePanes(_session.UnfreezePanes);
    }

    private void ApplyFreezePanes(Func<WorkbookCellEditResult> execute)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);

        // Freezing/unfreezing panes must not relocate the viewport (Excel keeps the same
        // rows/columns on screen, modulo the newly-pinned band). The scrollbar Values are
        // interpreted relative to FrozenRows/FrozenCols (WorkbookViewportScrollPlanner.
        // ScrollbarValueToWorksheetIndex), so capture the absolute top-left row/col currently
        // in view under the OLD frozen counts before the command changes them, then re-derive
        // fresh scrollbar Values for the NEW frozen counts that resolve back to that same
        // absolute row/col (WorksheetIndexToScrollbarValue naturally clamps to just below the
        // new frozen band when the old top-left now falls inside it). When unscrolled (Value
        // == 1, absolute row/col == 1) this maps back to 1 either way, so freeze-at-A1 is
        // unaffected.
        var (preTopRow, preLeftCol) = GetEffectiveViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);

        if (!TryExecuteWorksheetLayout(
                execute,
                "Freeze Panes"))
            return;

        var frozenRows = _session.GetEffectiveFrozenRows();
        var frozenCols = _session.GetEffectiveFrozenCols();

        // This window chose the new Freeze Panes state -- remember it as THIS window's own state
        // (R89-freeze-split-per-window-1), exactly like SetWorksheetViewMode/the View-tab toggles:
        // a sibling "New Window" over the same document must keep showing whatever it had before.
        SyncWindowViewState([_currentSheetId]);

        var newVerticalValue = WorksheetIndexToScrollbarValue(preTopRow, frozenRows);
        var newHorizontalValue = WorksheetIndexToScrollbarValue(preLeftCol, frozenCols);

        // Bump Maximum first if needed so assigning Value below isn't silently clamped by a
        // range still sized for the old frozen counts; UpdateViewport() (called next)
        // recalculates the real Maximum for the new frozen counts right after.
        if (newVerticalValue > VerticalScroll.Maximum)
            VerticalScroll.Maximum = newVerticalValue;
        if (newHorizontalValue > HorizontalScroll.Maximum)
            HorizontalScroll.Maximum = newHorizontalValue;

        VerticalScroll.Value = newVerticalValue;
        HorizontalScroll.Value = newHorizontalValue;

        UpdateViewport();
    }

    private void SetFreezePanes(uint frozenRows, uint frozenCols) =>
        ApplyFreezePanes(() => _session.SetFreezePanes(frozenRows, frozenCols));

    private void SplitViewBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        // This window's own effective Split state (R89-freeze-split-per-window-1), not the
        // shared Sheet's -- a sibling "New Window" may have Split set/cleared without this
        // window ever touching it.
        var viewState = GetEffectiveViewState(sheet);
        var wasSplit = viewState.SplitRow is not null || viewState.SplitColumn is not null;

        var targetSheetIds = CurrentGroupedEditSheetIds();
        var viewportRows = SheetGrid.Viewport?.RowMetrics;
        var viewportColumns = SheetGrid.Viewport?.ColMetrics;
        if (!TryExecuteWorksheetLayout(
                () => _session.ToggleSplitPanesAtActiveCell(viewportRows, viewportColumns),
                "Split"))
            return;

        // This window chose the new Split state -- remember it as THIS window's own state
        // (R89-freeze-split-per-window-1), same reasoning as Freeze Panes/the View-tab toggles.
        SyncWindowViewState(targetSheetIds);

        // Toggling the split off (or recreating it fresh) must not leak the previous split's
        // per-pane scroll offsets into whatever split comes next -- otherwise a brand-new split
        // inherits stale TopRightLeftCol/BottomLeftTopRow and renders scrolled deep into the sheet
        // instead of starting at the split origin the way Excel does (see GetSplitPaneViewportOffsets
        // in MainWindow.Viewport.cs). OnSplitDividerMoved already clears this for drag-resizes; the
        // ribbon-driven create/clear/recreate cycle needs the same treatment.
        if (wasSplit)
            _splitPaneViewportOffsets.Remove(_currentSheetId);

        UpdateViewport();
    }

    private void OnSplitDividerMoved(uint? splitRow, uint? splitColumn)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        // This window's own effective Split state (R89-freeze-split-per-window-1), not the
        // shared Sheet's.
        var viewState = GetEffectiveViewState(sheet);
        var nextRow = splitRow ?? viewState.SplitRow;
        var nextColumn = splitColumn ?? viewState.SplitColumn;
        if (nextRow == viewState.SplitRow && nextColumn == viewState.SplitColumn)
            return;

        if (!TryExecuteWorksheetLayout(
                () => _session.SetSplitPanes(nextRow, nextColumn),
                "Split"))
            return;

        SyncWindowViewState([_currentSheetId]);
        _splitPaneViewportOffsets.Remove(_currentSheetId);
        UpdateViewport();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void RestoreWorkbookWindow()
    {
        if (WindowState != WindowState.Normal)
            SystemCommands.RestoreWindow(this);
    }

    private void BeginSystemWindowMove() => BeginSystemWindowCommand(SC_MOVE);

    private void BeginSystemWindowSize() => BeginSystemWindowCommand(SC_SIZE);

    private void BeginSystemWindowCommand(int command)
    {
        if (WindowState != WindowState.Normal)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SendMessage(handle, WM_SYSCOMMAND, (IntPtr)command, IntPtr.Zero);
    }

    private void MaxRestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseSysBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void ConfigureStatusZoomSlider()
    {
        var plan = StatusBarZoomSliderPlanner.Build((int)ZoomLevelMapper.DefaultZoomPercent);
        ZoomSlider.Minimum = plan.MinimumSliderValue;
        ZoomSlider.Maximum = plan.MaximumSliderValue;
        ZoomSlider.SmallChange = plan.SmallChange;
        ZoomSlider.LargeChange = plan.LargeChange;
        ZoomSlider.Ticks = new System.Windows.Media.DoubleCollection(plan.SliderTickValues);
        ZoomSlider.Value = plan.SliderValue;
    }

    private static double StatusZoomSliderValueForPercent(double zoomPercent) =>
        StatusBarZoomSliderPlanner.Build((int)Math.Round(zoomPercent)).SliderValue;

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + StatusBarZoomSliderPlanner.SmallChange);
    }
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - StatusBarZoomSliderPlanner.SmallChange);
    }
    private void ZoomPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomCustomMenuItem_Click(sender, e);
    }
    private void ZoomPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.Tag is not string tag ||
            !FreeX.App.Services.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent))
            return;

        ZoomSlider.Value = StatusZoomSliderValueForPercent(zoomPercent);
    }
    private void ZoomCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var current = (int)Math.Round(_zoomLevel * 100);
        var dialog = new ZoomDialog(current) { Owner = this };
        try
        {
            if (dialog.ShowDialog() != true)
                return;

            var (selectedColumnWidths, selectedRowHeights) = GetSelectionPixelMetrics(SheetGrid.SelectedRange);
            var zoomPercent = ZoomSelectionPlanner.CalculateZoomPercent(
                dialog.Result.ZoomPercent,
                dialog.Result.FitSelection,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight,
                selectedColumnWidths,
                selectedRowHeights);
            ZoomSlider.Value = StatusZoomSliderValueForPercent(zoomPercent);
        }
        finally
        {
            FocusSheetGridIfNeeded();
        }
    }

    private void StatusZoomText_OpenZoomDialog(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ZoomCustomMenuItem_Click(sender, e);
    }

    private void StatusZoomText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        e.Handled = true;
        ZoomCustomMenuItem_Click(sender, e);
    }

    private void Zoom100Btn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = StatusZoomSliderValueForPercent(ZoomLevelMapper.DefaultZoomPercent);
    }
    private void ZoomSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } activeRange) return;
        // R79-render-namebar-statusbar-5-4: fit the bounding box of the WHOLE multi-area selection
        // (Ctrl+click-added disjoint ranges), not just the last-clicked active range.
        var range = ZoomSelectionPlanner.ResolveFitRange(activeRange, SheetGrid.SelectedRanges);
        var (selectedColumnWidths, selectedRowHeights) = GetSelectionPixelMetrics(range);
        var fitPct = ZoomSelectionPlanner.CalculateFitPercent(
            SheetGrid.ActualWidth,
            SheetGrid.ActualHeight,
            selectedColumnWidths,
            selectedRowHeights);
        ZoomSlider.Value = StatusZoomSliderValueForPercent(fitPct);
        // R79-render-namebar-statusbar-5-1: changing the zoom % alone never moves the scrollbars --
        // scroll the (now-correctly-sized) selection into view the same way every other
        // selection-driven navigation command does.
        EnsureCellVisible(range.Start);
    }

    /// <summary>
    /// Builds the selection's actual per-column pixel widths and per-row pixel heights (honoring
    /// custom <see cref="Sheet.ColumnWidths"/>/<see cref="Sheet.RowHeights"/> and skipping
    /// effectively-hidden columns/rows, matching <c>ViewportService</c>'s metrics builder), for use
    /// with Excel-accurate Zoom-to-Selection fitting. Falls back to the sheet defaults when there is
    /// no active sheet or range.
    /// </summary>
    private (IReadOnlyList<double> ColumnWidths, IReadOnlyList<double> RowHeights) GetSelectionPixelMetrics(
        GridRange? range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || range is not { } selection)
            return (Array.Empty<double>(), Array.Empty<double>());

        var columnWidths = new List<double>();
        for (var col = selection.Start.Col; col <= selection.End.Col; col++)
        {
            if (sheet.IsColEffectivelyHidden(col)) continue;
            var widthChars = sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth);
            columnWidths.Add(ColumnWidthPixelMapper.ColumnWidthToPixels(widthChars));
        }

        var rowHeights = new List<double>();
        for (var row = selection.Start.Row; row <= selection.End.Row; row++)
        {
            if (sheet.IsRowEffectivelyHidden(row)) continue;
            rowHeights.Add(sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight));
        }

        return (columnWidths, rowHeights);
    }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomSlider == null || SheetGrid == null || StatusZoomText == null) return;
        if (_snapInProgress || _suppressZoomSync) return;
        var inputPlan = StatusBarZoomSliderPlanner.BuildInput(e.NewValue);

        if (inputPlan.SnappedToDefault)
        {
            _snapInProgress = true;
            try
            {
                ZoomSlider.Value = inputPlan.SliderValue;
            }
            finally
            {
                _snapInProgress = false;
            }
        }

        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (!TryExecuteGroupedWorksheetViewState(
                targetSheetIds,
                () => _session.SetZoomPercent(inputPlan.ZoomPercent),
                "Zoom"))
            return;

        SyncZoomFromSheet(inputPlan.ZoomPercent, updateSlider: false);
        UpdateViewport();
    }

    private void SyncZoomFromSheet(int zoomPercent, bool updateSlider = true)
    {
        var plan = StatusBarZoomSliderPlanner.Build(zoomPercent);
        _zoomLevel = plan.ZoomPercent / 100.0;
        if (SheetGrid is not null)
        {
            SheetGrid.ZoomFactor = _zoomLevel;
            SheetGrid.RenderTransform = new System.Windows.Media.ScaleTransform(_zoomLevel, _zoomLevel, 0, 0);
        }
        if (StatusZoomText is not null)
        {
            StatusZoomText.Text = plan.ZoomText;
            AutomationProperties.SetName(StatusZoomText, plan.ZoomText);
        }

        if (!updateSlider || ZoomSlider is null)
            return;

        _suppressZoomSync = true;
        try
        {
            ZoomSlider.Value = plan.SliderValue;
        }
        finally
        {
            _suppressZoomSync = false;
        }
    }

    private void FormulaBarExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        MutateRuntimeOptions(options => options.FormulaBarExpanded = _formulaBarExpanded);
        ApplyFormulaBarExpansion();
    }

    private void ApplyFormulaBarExpansion()
    {
        var plan = FormulaBarChromePlanner.BuildExpansion(_formulaBarExpanded);

        FormulaBar.Height = plan.EditorHeight;
        FormulaBar.AcceptsReturn = plan.AcceptsReturn;
        FormulaBarExpandBtn.Content = CreateFormulaBarChevron(pointsUp: plan.ChevronPointsUp);
        AutomationProperties.SetName(FormulaBarExpandBtn, UiText.Get(plan.Button.AutomationNameResourceKey));
        AutomationProperties.SetHelpText(FormulaBarExpandBtn, UiText.Get(plan.Button.HelpTextResourceKey));
        RibbonTooltip.SetTitle(FormulaBarExpandBtn, UiText.Get(plan.TooltipTitleResourceKey));
        RibbonTooltip.SetDescription(FormulaBarExpandBtn, UiText.Get(plan.TooltipDescriptionResourceKey));
    }

    private static FrameworkElement CreateFormulaBarChevron(bool pointsUp) =>
        CreateRibbonChevronGlyph(12, 8, BrushFromRgb(31, 31, 31), pointsUp);

    // ── Ribbon horizontal scroll via mouse wheel ─────────────────────────────

    private void RibbonScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.5);
        e.Handled = true;
    }
}
