using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void ApplyOptionsToView()
    {
        SheetGrid.UseR1C1ReferenceStyle = _options.UseR1C1ReferenceStyle;
        SheetGrid.EnableFillHandleAndCellDragAndDrop = _options.EnableFillHandleAndCellDragAndDrop;
        ApplyFormulaBarVisibility(_options.ShowFormulaBar);
        _suppressAppViewOptionSync = true;
        try
        {
            _formulaBarExpanded = _options.FormulaBarExpanded;
            ApplyFormulaBarExpansion();
            ApplyStatusBarInteractiveDisplayState();
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }

        if (SheetGrid.SelectedRange is { } range)
        {
            CellAddressBox.Text = FormatNameBoxSelectionText(range);
            var sheet = _workbook.GetSheet(_currentSheetId);
            FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(range.Start), range.Start);
        }
    }

    /// <summary>
    /// Applies a Formula Bar visibility value to THIS window's chrome (the checked ribbon toggle
    /// and the actual FormulaBarBorder visibility). Show Formula Bar is a genuine Excel-instance
    /// display preference (R83-app-view-modes-5-2), so this is called both when this window loads
    /// its own options and when the <see cref="WorkbookWindowRegistry"/> broadcasts a change made
    /// in another window of the same process (see <see cref="IWorkbookWindow.ApplyFormulaBarVisibility"/>).
    /// </summary>
    public void ApplyFormulaBarVisibility(bool visible)
    {
        _suppressAppViewOptionSync = true;
        try
        {
            // The Formula Bar toggle's checked state follows the app option via the neutral store,
            // which drives the rendered View > Formula Bar check box.
            _ribbonState.SetChecked("Formula Bar", visible);
            if (FormulaBarBorder is not null)
                FormulaBarBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }
    }

    private void RecalculateWorkbook()
    {
        _session.RecalculateWorkbook();
        InvalidateNavigationCaches();
        // R88-app-formula-auditing-5-1: the Watch Window is a modeless, non-closed dialog whose
        // Value/Formula columns must track the workbook live -- refresh it at every recalculation
        // choke point rather than only from its own Add/Refresh/Delete button handlers. Safe against
        // re-entrancy: the dialog's own getEntries callback calls RecalculateWorkbook() before
        // reading values, and WatchWindowDialog.Refresh() guards against being re-entered from here.
        _watchWindowDialog?.Refresh();
    }

    /// <summary>
    /// Plain F9's "Calculate Now" scope: recalculate only what is actually dirty using the
    /// existing dependency graph -- volatile cells (always re-evaluated every calc pass) and
    /// anything their formulas touch. In Automatic mode this is normally a cheap no-op, since
    /// RecalculateIfAutomatic already keeps every edited cell's dependents current as edits
    /// happen; unlike <see cref="RecalculateWorkbook"/> (Ctrl+Alt+F9's "Calculate Full" scope)
    /// it does NOT rebuild the dependency graph or re-evaluate every formula cell in the
    /// workbook, matching Excel's distinct F9 vs Ctrl+Alt+F9 cost/scope.
    /// </summary>
    private void RecalculateDirtyCells()
    {
        _session.RecalculateDirtyCells();
        InvalidateNavigationCaches();
        // See RecalculateWorkbook above (R88-app-formula-auditing-5-1).
        _watchWindowDialog?.Refresh();
    }

    private void RebuildDependenciesAndCalculate()
    {
        _session.RecalculateWorkbook();
        InvalidateNavigationCaches();
        // See RecalculateWorkbook above (R88-app-formula-auditing-5-1).
        _watchWindowDialog?.Refresh();
        UpdateViewport();
    }

    private void RecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
    {
        var report = _session.RecalculateChangedCells(changedCells);
        if (report is not null)
        {
            InvalidateNavigationCaches();
            // The session owns calculation and linked-picture refresh; WPF only updates its
            // native modeless audit surface and renderer caches afterward.
            _watchWindowDialog?.Refresh();
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            NormalizeRibbonSurfaceAfterResize();
        ScheduleViewportResizeRefresh();
    }

    private void ScheduleViewportResizeRefresh()
    {
        SheetGrid.IsLiveResizing = true;
        _resizeViewportRefreshPending = true;
        _resizeViewportRefreshGeneration++;
        _resizeViewportRefreshTimer ??= CreateResizeViewportRefreshTimer();
        _resizeViewportRefreshTimer.Stop();
        if (_isInWindowResizeMoveLoop)
            return;

        _resizeViewportRefreshTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateResizeViewportRefreshTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = System.TimeSpan.FromMilliseconds(ResizeViewportRefreshDelayMilliseconds)
        };

        timer.Tick += (_, _) => QueueViewportResizeRefreshCompletion();

        return timer;
    }

    private void QueueViewportResizeRefreshCompletion()
    {
        _resizeViewportRefreshTimer?.Stop();
        var generation = _resizeViewportRefreshGeneration;
        Dispatcher.BeginInvoke(
            new System.Action(() =>
            {
                if (!_resizeViewportRefreshPending ||
                    _isInWindowResizeMoveLoop ||
                    generation != _resizeViewportRefreshGeneration)
                {
                    return;
                }

                CompleteViewportResizeRefresh();
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CompleteViewportResizeRefresh()
    {
        _resizeViewportRefreshTimer?.Stop();
        _resizeViewportRefreshPending = false;
        SheetGrid.IsLiveResizing = false;
        UpdateViewport();
    }

    private void CancelPendingViewportResizeRefresh()
    {
        if (!_resizeViewportRefreshPending)
            return;

        _resizeViewportRefreshTimer?.Stop();
        _resizeViewportRefreshPending = false;
        _resizeViewportRefreshGeneration++;
        SheetGrid.IsLiveResizing = false;
    }

    private string FormatCellReference(CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatCellReference(address, _options.UseR1C1ReferenceStyle);

    private string FormatColumnReference(uint column) =>
        SpreadsheetDisplayFormatter.FormatColumnReference(column, _options.UseR1C1ReferenceStyle);

    private string FormatRangeReference(CellAddress start, CellAddress end) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, _options.UseR1C1ReferenceStyle);

    // R118: reverse (range -> best name) index for the Name Box, mirroring the revision-keyed
    // memoization WorkbookSelectionStatsCache already uses for the status bar. Before this, every
    // selection change (SetActiveCell -- plain clicks and every arrow-key move -- plus paste, sort,
    // and merge) re-walked the full ScopedNamedRanges and NamedRanges dictionaries from scratch, so
    // a workbook with hundreds/thousands of defined names (a well-documented Excel copy/paste bloat
    // pattern) made ordinary navigation perceptibly laggier than in Excel. The index is rebuilt only
    // when _navigationCacheRevision (already bumped by TryExecuteCommand/TryExecuteRepeatableCommand
    // on every successful model-changing command, including DefineNamedRangeCommand/MoveRangeCommand/
    // InsertDeleteCellsCommand/the row-column shift helpers) or the active sheet changes, so repeated
    // selection changes between real edits cost an O(1) dictionary lookup instead of an O(name count)
    // scan.
    private ulong _nameBoxRangeIndexRevision = ulong.MaxValue;
    private SheetId _nameBoxRangeIndexSheetId;
    private Dictionary<GridRange, string>? _nameBoxScopedRangeIndex;
    private Dictionary<GridRange, string>? _nameBoxGlobalRangeIndex;

    private string FormatNameBoxSelectionText(GridRange range)
    {
        EnsureNameBoxRangeIndex();

        // Sheet-scoped names on the active sheet take precedence over a same-named workbook-global
        // name, matching formula evaluation's resolution order (Workbook.TryGetNamedRange) and the
        // Name Box's own reference-resolution precedence (TryParseNameBoxReferenceRange).
        if (_nameBoxScopedRangeIndex!.TryGetValue(range, out var scopedName))
            return scopedName;

        if (_nameBoxGlobalRangeIndex!.TryGetValue(range, out var globalName))
            return globalName;

        return FormatRangeReference(range.Start, range.End);
    }

    /// <summary>
    /// (Re)builds the Name Box's range-to-name reverse index when the workbook's defined names (or
    /// the active sheet, which changes which sheet-scoped names apply) may have changed since the
    /// last build. Each rebuild is a single O(name count) pass over both dictionaries -- exactly the
    /// same total work FormatNameBoxSelectionText used to redo on every call -- but it only happens
    /// once per real change instead of once per selection change.
    /// </summary>
    private void EnsureNameBoxRangeIndex()
    {
        if (_nameBoxScopedRangeIndex is not null &&
            _nameBoxGlobalRangeIndex is not null &&
            _nameBoxRangeIndexRevision == _navigationCacheRevision &&
            _nameBoxRangeIndexSheetId.Equals(_currentSheetId))
        {
            return;
        }

        var scopedIndex = new Dictionary<GridRange, string>();
        foreach (var (key, namedRange) in _workbook.ScopedNamedRanges)
        {
            if (!key.Sheet.Equals(_currentSheetId))
                continue;

            if (!scopedIndex.TryGetValue(namedRange, out var existing) ||
                string.Compare(key.Name, existing, StringComparison.OrdinalIgnoreCase) < 0)
            {
                scopedIndex[namedRange] = key.Name;
            }
        }

        var globalIndex = new Dictionary<GridRange, string>();
        foreach (var (name, namedRange) in _workbook.NamedRanges)
        {
            if (!globalIndex.TryGetValue(namedRange, out var existing) ||
                string.Compare(name, existing, StringComparison.OrdinalIgnoreCase) < 0)
            {
                globalIndex[namedRange] = name;
            }
        }

        _nameBoxScopedRangeIndex = scopedIndex;
        _nameBoxGlobalRangeIndex = globalIndex;
        _nameBoxRangeIndexRevision = _navigationCacheRevision;
        _nameBoxRangeIndexSheetId = _currentSheetId;
    }

    private string FormatFormulaBarText(Cell? cell, CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatFormulaBarText(
            cell,
            address,
            _options.UseR1C1ReferenceStyle,
            _workbook.GetSheet(address.Sheet),
            _workbook);

    private void InvalidateToolbarVisualState()
    {
        _toolbarVisualStateCache.Clear();
        _lastToolbarVisualState = null;
    }

    private void RefreshToolbar()
    {
        RefreshQuickAccessToolbarCommandStates();
        RefreshToolbarVisualState();
    }

    private void RefreshToolbarAfterSelectionChange()
    {
        SynchronizeWorkbookSessionSelection();
        RefreshPivotFieldListPaneAfterSelectionChange();

        if (CanSkipSelectionToolbarRefresh())
            return;

        RefreshQuickAccessToolbarCommandStatesAfterSelectionChange();
        RefreshToolbarVisualState();
    }

    private void RefreshToolbarVisualState()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            InvalidateToolbarVisualState();
            return;
        }
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
        {
            InvalidateToolbarVisualState();
            return;
        }
        // R91-app-ribbon-state-5-1: read the TRUE active/anchor cell (SheetGrid.ActiveCell), not
        // SelectedRange's normalized top-left Start -- those differ whenever the selection was
        // extended upward or leftward (e.g. click C3 then Shift+click A1). Excel's Home-tab toggles
        // (Bold/Italic/Underline/alignment/Wrap Text) always reflect the active cell, matching the
        // same ActiveCell-over-Start correction already applied to Backspace/Clear Contents
        // (R76-meta-1; see MainWindow.ClipboardCommands.cs's ExecuteClearActiveCell).
        var activeCell = SheetGrid.ActiveCell ?? range.Start;
        var styleId = sheet.GetCell(activeCell)?.StyleId ?? StyleId.Default;
        var state = _toolbarVisualStateCache.TryGet(_workbook.Id, styleId, out var cachedState)
            ? cachedState
            : _toolbarVisualStateCache.AddOrUpdate(
                _workbook.Id,
                styleId,
                ToolbarVisualState.From(_workbook.GetStyle(styleId)));
        if (state == _lastToolbarVisualState)
            return;

        _suppressToolbarSync = true;
        try
        {
            // Write the neutral state store (the source of truth); the renderer binds each rendered
            // control to it. The store dedups no-op writes, so this never churns bound controls.
            _ribbonState.SetChecked("Bold", state.Bold);
            _ribbonState.SetChecked("Italic", state.Italic);
            _ribbonState.SetChecked("Underline", state.Underline);
            _ribbonState.SetChecked("Strikethrough", state.Strikethrough);
            _ribbonState.SetChecked("Top Align", state.VerticalAlignment == CellVAlign.Top);
            _ribbonState.SetChecked("Middle Align", state.VerticalAlignment == CellVAlign.Center);
            _ribbonState.SetChecked("Bottom Align", state.VerticalAlignment == CellVAlign.Bottom);
            _ribbonState.SetChecked("Align Left", state.HorizontalAlignment == CellHAlign.Left);
            _ribbonState.SetChecked("Center", state.HorizontalAlignment == CellHAlign.Center);
            _ribbonState.SetChecked("Align Right", state.HorizontalAlignment == CellHAlign.Right);
            _ribbonState.SetChecked("Wrap Text", state.WrapText);
            SetRibbonComboValue("Font", state.FontName);
            SetRibbonComboValue("Font Size", state.FontSizeText);
            _lastToolbarVisualState = state;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private bool CanSkipSelectionDragToolbarRefresh() =>
        CanSkipSelectionToolbarRefresh();

    private bool CanSkipSelectionToolbarRefresh() =>
        IsQuickAccessToolbarCommandStateStableForSelectionDrag() &&
        IsToolbarVisualStateCurrentForSelection();

    private bool IsToolbarVisualStateCurrentForSelection()
    {
        if (SheetGrid.SelectedRange is not { } range)
            return _lastToolbarVisualState is null;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return _lastToolbarVisualState is null;

        var activeCell = SheetGrid.ActiveCell ?? range.Start;
        var styleId = sheet.GetCell(activeCell)?.StyleId ?? StyleId.Default;
        return _toolbarVisualStateCache.TryGetCurrent(_workbook.Id, styleId, out var state) &&
            state == _lastToolbarVisualState;
    }

    /// <summary>Pushes a combo's display value into the neutral state store, which drives the rendered
    /// ribbon combo's <c>Text</c> via the renderer's store binding. The store dedups, so unchanged
    /// values are no-ops. There is no hidden backplane combo to mirror onto anymore.</summary>
    private void SetRibbonComboValue(string commandId, object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        _ribbonState.SetValue(commandId, text);
    }

    private void ApplyStyleDiff(StyleDiff diff)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableApplyStyle(diff, "Apply Style"))
            return;

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private FindReplaceDialog? _findReplaceDialog;

    private void FindButton_Click(object sender, RoutedEventArgs e) =>
        OpenFindReplaceDialog(replaceMode: false);

    private void ReplaceButton_Click(object sender, RoutedEventArgs e) =>
        OpenFindReplaceDialog(replaceMode: true);

    private void OpenFindReplaceDialog(bool replaceMode)
    {
        if (_findReplaceDialog is not null)
        {
            // Reuse the already-open dialog: just switch tabs and bring it to the front.
            _findReplaceDialog.SwitchMode(replaceMode);
            _findReplaceDialog.Activate();
            return;
        }

        var dlg = new FindReplaceDialog(
            () => _workbook,
            _commandBus,
            NavigateToCell,
            replaceMode: replaceMode,
            () => _currentSheetId,
            () => SheetGrid.SelectedRange?.Start,
            RefreshAfterFindReplaceEdit)
        {
            Owner = this
        };
        dlg.Closed += (_, _) => _findReplaceDialog = null;
        _findReplaceDialog = dlg;
        dlg.Show();
    }

    /// <summary>
    /// Closes the Find/Replace dialog if it is open. Called whenever the active workbook is
    /// replaced (New, Open, drag-drop) so the dialog cannot operate on a stale workbook reference.
    /// </summary>
    private void CloseFindReplaceDialogIfOpen()
    {
        _findReplaceDialog?.Close();
        // _findReplaceDialog is nulled by the Closed handler above.
    }

    private void NavigateToCell(CellAddress addr)
    {
        _currentSheetId = addr.Sheet;
        SetActiveCell(addr);
        EnsureCellVisible(addr);
        UpdateViewport();
    }

    private void RefreshAfterFindReplaceEdit()
    {
        MarkWorkbookDirty();
        InvalidateNavigationCaches();
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        NotifyOtherWindowsOfWorkbookChange();
    }

    private void RefreshSheetProtectionUi()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        // The Protect Sheet button toggles its label/tooltip between Protect and Unprotect. That
        // dynamic content is not part of RibbonCommandState, so update the rendered ribbon button
        // directly (no hidden backplane control). Skipped until the declarative ribbon is built.
        if (FindRenderedRibbonControl("Protect Sheet") is not ButtonBase protectSheet)
            return;

        var uiText = SheetProtectionWorkflow.GetUiText(sheet);
        SetRibbonCommandButtonLabel(protectSheet, uiText.ButtonContent);
        RibbonTooltip.SetTitle(protectSheet, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(protectSheet, uiText.TooltipDescription);
    }

    private void RefreshWorkbookProtectionUi()
    {
        var uiText = WorkbookProtectionWorkflow.GetUiText(_workbook);
        if (FindRenderedRibbonControl("Protect Workbook") is ButtonBase protectWorkbook)
        {
            SetRibbonCommandButtonLabel(protectWorkbook, uiText.ButtonContent);
            RibbonTooltip.SetTitle(protectWorkbook, uiText.TooltipTitle);
            RibbonTooltip.SetDescription(protectWorkbook, uiText.TooltipDescription);
        }

        RefreshBackstageInfoProtectionButton();
    }
}
