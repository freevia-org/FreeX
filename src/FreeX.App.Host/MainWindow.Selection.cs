using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Services;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void SelectRow(uint row)
    {
        var range = GridSelectionNavigationPlanner.CreateWholeRowsRange(_currentSheetId, row, row);
        if (TryApplyFormulaRangeSelection(range, range.Start, range.End))
            return;

        ClearSelectionTransientOverlays();
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(null);
        var sheet = _workbook.GetSheet(_currentSheetId);
        // A row that a merge only partially spans (vertically) must expand to the merge's full
        // row footprint, exactly like drag-select/Shift-extend/Ctrl-click already do via this same
        // helper -- Excel never allows a header click to select only part of a merged cell
        // (R99-render-header-select-merge-expand).
        var expandedRange = MergedSelectionRangePlanner.ExpandToFullyContainMerges(sheet, range);
        SheetGrid.SelectedRange = expandedRange;
        CellAddressBox.Text = expandedRange.Start.Row == expandedRange.End.Row
            ? $"{row}:{row}"
            : $"{expandedRange.Start.Row}:{expandedRange.End.Row}";
        var cell = sheet?.GetCell(_selectionAnchor.Value);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void SelectColumn(uint col)
    {
        var range = GridSelectionNavigationPlanner.CreateWholeColumnsRange(_currentSheetId, col, col);
        if (TryApplyFormulaRangeSelection(range, range.Start, range.End))
            return;

        ClearSelectionTransientOverlays();
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(null);
        var sheet = _workbook.GetSheet(_currentSheetId);
        // Column counterpart of the row expansion above (R99-render-header-select-merge-expand).
        var expandedRange = ExpandRangeToFullyContainMerges(sheet, range);
        SheetGrid.SelectedRange = expandedRange;
        CellAddressBox.Text = expandedRange.Start.Col == expandedRange.End.Col
            ? $"{FormatColumnReference(col)}:{FormatColumnReference(col)}"
            : $"{FormatColumnReference(expandedRange.Start.Col)}:{FormatColumnReference(expandedRange.End.Col)}";
        var cell = sheet?.GetCell(_selectionAnchor.Value);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    // Ctrl+clicking a second column header must ADD col as a disjoint area (Excel's "Report
    // Connections"-style multi-area header selection), not wipe the existing selection down to
    // just this column the way a plain click does (R49-render-multiarea-selection-3-2).
    //
    // Header Ctrl+click always starts a new disjoint area; header drag continuation is handled by
    // ExtendHeaderSelection, so this route uses the shared planner's append-only operation.
    private void AddAdditionalColumnSelection(uint col)
    {
        var range = GridSelectionNavigationPlanner.CreateWholeColumnsRange(_currentSheetId, col, col);
        if (TryAppendDisjointFormulaRangeReference(range))
            return;

        if (TryApplyFormulaRangeSelection(range, range.Start, range.End))
            return;

        ClearSelectionTransientOverlays();
        var sheet = _workbook.GetSheet(_currentSheetId);
        // The newly Ctrl-clicked area must also fully absorb any merge it only partially spans,
        // same as the plain-click path (R99-render-header-select-merge-expand).
        var expandedRange = ExpandRangeToFullyContainMerges(sheet, range);
        var ranges = GridSelectionNavigationPlanner.AppendDisjointSelectionArea(
            SheetGrid.SelectedRanges,
            SheetGrid.SelectedRange,
            expandedRange);
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(ranges);
        SheetGrid.SelectedRange = expandedRange;
        CellAddressBox.Text = expandedRange.Start.Col == expandedRange.End.Col
            ? $"{FormatColumnReference(col)}:{FormatColumnReference(col)}"
            : $"{FormatColumnReference(expandedRange.Start.Col)}:{FormatColumnReference(expandedRange.End.Col)}";
        var cell = sheet?.GetCell(_selectionAnchor.Value);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    // Row-header counterpart of AddAdditionalColumnSelection (R49-render-multiarea-selection-3-2).
    private void AddAdditionalRowSelection(uint row)
    {
        var range = GridSelectionNavigationPlanner.CreateWholeRowsRange(_currentSheetId, row, row);
        if (TryAppendDisjointFormulaRangeReference(range))
            return;

        if (TryApplyFormulaRangeSelection(range, range.Start, range.End))
            return;

        ClearSelectionTransientOverlays();
        var sheet = _workbook.GetSheet(_currentSheetId);
        // Row counterpart of AddAdditionalColumnSelection's expansion above
        // (R99-render-header-select-merge-expand).
        var expandedRange = ExpandRangeToFullyContainMerges(sheet, range);
        var ranges = GridSelectionNavigationPlanner.AppendDisjointSelectionArea(
            SheetGrid.SelectedRanges,
            SheetGrid.SelectedRange,
            expandedRange);
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(ranges);
        SheetGrid.SelectedRange = expandedRange;
        CellAddressBox.Text = expandedRange.Start.Row == expandedRange.End.Row
            ? $"{row}:{row}"
            : $"{expandedRange.Start.Row}:{expandedRange.End.Row}";
        var cell = sheet?.GetCell(_selectionAnchor.Value);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void SelectAll()
    {
        var range = GridSelectionNavigationPlanner.CreateWholeGridRange(_currentSheetId);
        if (TryApplyFormulaRangeSelection(range, range.Start, range.End))
            return;

        ClearSelectionTransientOverlays();
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatCellReference(_selectionAnchor.Value);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    // Ctrl+click during in-formula point-mode reference entry must append a NEW, comma-separated
    // disjoint area after whatever was previously inserted, rather than replacing it the way a
    // plain click (or TryApplyFormulaRangeSelection, which only ever replaces/extends the single
    // tracked reference span) does (R52-render-formula-bar-ref-3-3). Requires an existing tracked
    // reference span to append after; the very first click in point mode has no prior span and
    // falls through to the normal (replacing) path.
    private bool TryAppendDisjointFormulaReference(CellAddress newAddr)
        => TryAppendDisjointFormulaRangeReference(new GridRange(newAddr, newAddr));

    private bool TryAppendDisjointFormulaRangeReference(GridRange range)
        => TryAppendDisjointFormulaRangeReference(range, null, null);

    private bool TryAppendDisjointFormulaRangeReference(
        GridRange range,
        string? selectedSheetNameOverride,
        string? selectedWorkbookName)
    {
        var editor = GetFormulaRangeEntryEditor();
        if (editor is null)
            return TryRouteFormulaPointModeSelection(range, append: true);

        if (_formulaEditCell is not { } formulaCell ||
            !FormulaRangeEntryPlanner.TryGetReferenceSpanForPointEntry(
                editor.Text,
                _formulaRangeEditingSession.ReferenceSpan?.Start,
                _formulaRangeEditingSession.ReferenceSpan?.Length,
                editor.CaretIndex,
                editor.SelectionLength,
                out var referenceStart,
                out var referenceLength) ||
            !FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(
                editor.Text,
                referenceStart,
                referenceLength,
                range,
                formulaCell,
                _options.UseR1C1ReferenceStyle,
                out var edit,
                selectedSheetNameOverride ?? _workbook.GetSheet(range.Start.Sheet)?.Name,
                selectedWorkbookName: selectedWorkbookName))
        {
            return false;
        }

        ApplyFormulaEditorTextEdit(editor, edit.TextEdit);

        _formulaRangeEditingSession.ApplyPlannerEdit(edit, range.Start, range.End);

        HideValidationDropdown();
        ClearCommentPreview();
        if (selectedWorkbookName is null)
        {
            _selectionAnchor = range.Start;
            _selectionCursor = range.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = range;
            CellAddressBox.Text = range.Start == range.End
                ? FormatCellReference(range.Start)
                : FormatRangeReference(range.Start, range.End);
        }
        RefreshStatusBar();
        RefreshFormulaReferenceHighlights();
        SetFormulaEditStatusBarMode(pointMode: true);
        editor.Focus();
        return true;
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(SheetGrid);
        // Must be the EFFECTIVE header height (includes the column-outline gutter when columns are
        // grouped), matching what DrawColumnHeader/DrawRowHeader actually render at
        // (GridView.CalculateColumnHeaderHeight) -- the bare ColHeaderHeight constant used here
        // previously made the select-all corner and every column header unclickable across the
        // gutter's height once any column outline group existed (R49-render-header-frozen-
        // corner-3-2; GridHeaderContextMenuHitPlanner's right-click path a few hundred lines below
        // already uses SheetGrid.EffectiveColHeaderHeight correctly).
        double colHeaderH = SheetGrid.EffectiveColHeaderHeight;
        double rowHeaderW = SheetGrid.ActualRowHeaderWidth;

        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;
        _dragSelectAddsAdditionalRange = false;

        // ── Header area ───────────────────────────────────────────────────────
        if (pos.X >= 0 && pos.Y >= 0 && (pos.X < rowHeaderW || pos.Y < colHeaderH))
        {
            // Top-left corner: select all
            if (pos.X < rowHeaderW && pos.Y < colHeaderH)
            {
                SelectAll();
                e.Handled = true;
                return;
            }
            // Column header: select entire column
            if (pos.Y < colHeaderH)
            {
                foreach (var cm in viewport.ColMetrics)
                {
                    double left = cm.LeftOffset + rowHeaderW;
                    if (pos.X >= left && pos.X < left + cm.Width)
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                        {
                            uint anchorCol = _selectionAnchor.Value.Col;
                            var anchor = new CellAddress(_currentSheetId, 1, anchorCol);
                            var cursor = new CellAddress(_currentSheetId, CellAddress.MaxRow, cm.Col);
                            var range = new GridRange(
                                new CellAddress(_currentSheetId, 1, Math.Min(anchorCol, cm.Col)),
                                new CellAddress(_currentSheetId, CellAddress.MaxRow, Math.Max(anchorCol, cm.Col)));
                            if (TryApplyFormulaRangeSelection(range, anchor, cursor))
                            {
                                BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Column, anchorCol);
                                e.Handled = true;
                                return;
                            }

                            HideValidationDropdown();
                            ClearCommentPreview();
                            _selectionCursor = cursor;
                            SetSelectedRangesIfChanged(null);
                            // Shift+click header extend must also fully absorb any merge the swept
                            // columns only partially span, matching every other header-selection
                            // path (R99-render-header-select-merge-expand).
                            range = ExpandRangeToFullyContainMerges(_workbook.GetSheet(_currentSheetId), range);
                            SheetGrid.SelectedRange = range;
                            var c1 = FormatColumnReference(range.Start.Col);
                            var c2 = FormatColumnReference(range.End.Col);
                            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
                            var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
                            SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
                            SheetGrid.Focus();
                            RefreshToolbarAfterSelectionChange();
                            RefreshStatusBar();
                            BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Column, anchorCol);
                        }
                        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                        {
                            // Ctrl+click a column header adds it as a disjoint area, matching
                            // Excel's multi-area column selection (R49-render-multiarea-
                            // selection-3-2) instead of wiping the existing selection like a plain
                            // click.
                            AddAdditionalColumnSelection(cm.Col);
                            BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Column, cm.Col);
                        }
                        else
                        {
                            SelectColumn(cm.Col);
                            BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Column, cm.Col);
                        }
                        e.Handled = true;
                        return;
                    }
                }
                return;
            }
            // Row header: select entire row
            foreach (var rm in viewport.RowMetrics)
            {
                double top = rm.TopOffset + colHeaderH;
                if (pos.Y >= top && pos.Y < top + rm.Height)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                    {
                        uint anchorRow = _selectionAnchor.Value.Row;
                        var anchor = new CellAddress(_currentSheetId, anchorRow, 1);
                        var cursor = new CellAddress(_currentSheetId, rm.Row, CellAddress.MaxCol);
                        var range = new GridRange(
                            new CellAddress(_currentSheetId, Math.Min(anchorRow, rm.Row), 1),
                            new CellAddress(_currentSheetId, Math.Max(anchorRow, rm.Row), CellAddress.MaxCol));
                        if (TryApplyFormulaRangeSelection(range, anchor, cursor))
                        {
                            BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Row, anchorRow);
                            e.Handled = true;
                            return;
                        }

                        HideValidationDropdown();
                        ClearCommentPreview();
                        _selectionCursor = cursor;
                        SetSelectedRangesIfChanged(null);
                        // Row counterpart of the column expansion above
                        // (R99-render-header-select-merge-expand).
                        range = ExpandRangeToFullyContainMerges(_workbook.GetSheet(_currentSheetId), range);
                        SheetGrid.SelectedRange = range;
                        var r1 = range.Start.Row;
                        var r2 = range.End.Row;
                        CellAddressBox.Text = r1 == r2 ? $"{r1}:{r1}" : $"{r1}:{r2}";
                        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
                        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
                        SheetGrid.Focus();
                        RefreshToolbarAfterSelectionChange();
                        RefreshStatusBar();
                        BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Row, anchorRow);
                    }
                    else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        // Row-header counterpart of the column-header Ctrl+click fix above
                        // (R49-render-multiarea-selection-3-2).
                        AddAdditionalRowSelection(rm.Row);
                        BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Row, rm.Row);
                    }
                    else
                    {
                        SelectRow(rm.Row);
                        BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Row, rm.Row);
                    }
                    e.Handled = true;
                    return;
                }
            }
            return;
        }

        // ── Cell area ─────────────────────────────────────────────────────────

        if (_formulaTraceArrows.Count > 0 &&
            FreeX.App.UI.GridView.HitTestFormulaTraceMarker(viewport, _formulaTraceArrows, _currentSheetId, pos) is { } traceTarget)
        {
            NavigateToCell(traceTarget);
            RefreshSheetTabs();
            RefreshToolbarAfterSelectionChange();
            RefreshStatusBar();
            e.Handled = true;
            return;
        }

        var hitAddress = FreeX.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
        if (hitAddress is { } newAddr)
        {
            _activeSplitPaneRegion = FreeX.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);

            // Ctrl+click while entering a formula reference in point mode must APPEND a disjoint,
            // comma-separated area (Excel: click A1 then Ctrl+click C3 -> "A1,C3") instead of
            // replacing the previously-inserted reference like a plain click
            // (R52-render-formula-bar-ref-3-3).
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                TryAppendDisjointFormulaReference(newAddr))
            {
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                TryRouteFormulaPointModeSelection(new GridRange(newAddr, newAddr), append: true))
            {
                _selectionAnchor = newAddr;
                _selectionCursor = newAddr;
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (TryApplyFormulaRangeSelection(newAddr, extendSelection: (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (TryRouteFormulaPointModeSelection(new GridRange(newAddr, newAddr)))
            {
                _selectionAnchor = newAddr;
                _selectionCursor = newAddr;
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_inlineEditor?.IsVisible == true)
            {
                FormulaBar.Text = _inlineEditor.Text;
                var committed = CommitEdit();
                HideInlineEditor(commit: false);
                if (!committed)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (_formatPainterActive)
            {
                if (SheetGrid.SelectedRange is { } selectedRange &&
                    selectedRange.Contains(newAddr) &&
                    (selectedRange.Start != selectedRange.End || e.ClickCount > 1))
                {
                    TryApplyFormatPainter(selectedRange);
                    UpdateCommentPreview(newAddr);
                    e.Handled = true;
                    return;
                }

                SetActiveCell(newAddr);
                _formatPainterTargetSelectionActive = true;
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_borderDrawMode != BorderDrawMode.None)
            {
                SetActiveCell(newAddr);
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if (TryHandleCellAreaExtendClick(newAddr))
            {
                // Handled inside TryHandleCellAreaExtendClick; falls through to the shared
                // e.Handled = true below, same as every other branch here.
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (TryOpenHyperlink(newAddr))
                {
                    e.Handled = true;
                    return;
                }

                AddOrMoveAdditionalSelection(newAddr, extendSelection: false);
                _dragSelectAddsAdditionalRange = true;
                _dragSelectionTransientOverlaysCleared = false;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
            }
            else
            {
                // A plain click onto a locked cell on a protected sheet with "Select locked cells"
                // unchecked must be refused outright -- Excel neither moves the active cell there
                // nor opens it for editing (R75-services-protection-security-4-1). Shift-click/F8
                // extend (TryHandleCellAreaExtendClick above) and Ctrl+click (Add mode) are left
                // ungated for now.
                if (!CanSelectCellForClick(newAddr))
                {
                    e.Handled = true;
                    return;
                }

                SetActiveCell(newAddr);
                if (e.ClickCount == 2)
                {
                    // R61-render-formula-bar-6-2: thread the double-click pointer X through so the
                    // inline editor's caret lands at the clicked pixel (mirroring Excel and FreeX's
                    // own Avalonia shell) instead of always at the end of the text.
                    if (!TryShowPivotTableDetails(showMessage: false))
                        EnterEditMode(pos.X);
                    e.Handled = true;
                }
                else
                {
                    // Start drag-select
                    _dragSelectionTransientOverlaysCleared = false;
                    _dragSelectActive = true;
                    SheetGrid.CaptureMouse();
                }
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Whether a plain (unmodified) click on <paramref name="newAddr"/> may select it, per
    /// <see cref="CommandGuards.CanSelectCell"/> -- false when the current sheet is protected, the
    /// cell is locked, and "Select locked cells" is unchecked (R75-services-protection-security-4-1).
    /// Split out of SheetGrid_MouseDown's plain-click branch so this decision is directly testable
    /// without driving a real, pixel-accurate WPF MouseButtonEventArgs through hit-testing (matching
    /// the R49-render-multiarea-selection-3-2 precedent for <see cref="TryHandleCellAreaExtendClick"/>
    /// below).
    /// </summary>
    private bool CanSelectCellForClick(CellAddress newAddr)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        return sheet is null || CommandGuards.CanSelectCell(_workbook, sheet, newAddr);
    }

    /// <summary>
    /// Handles a plain cell-area click when Shift is held OR F8 "Extend Selection" mode
    /// (<see cref="_selectionMode"/> == <see cref="ExcelSelectionMode.Extend"/>) is active, by
    /// extending from the current anchor to <paramref name="newAddr"/> exactly like Shift+click --
    /// mirroring the keyboard-navigation extend path (ExcelSelectionModePlanner.ShouldExtendSelection,
    /// used a few hundred lines below for arrow-key movement). Returns false (does nothing) for a
    /// Ctrl+click or an unmodified click with F8 inactive, leaving those to the caller's other
    /// branches. Split out of SheetGrid_MouseDown so this decision is directly testable without
    /// driving a real, pixel-accurate WPF MouseButtonEventArgs through hit-testing (matching the
    /// R49-render-multiarea-selection-3-2 precedent for header clicks).
    ///
    /// Before R68-app-selection-navigation-6-1, only Shift was checked here, so an F8-mode plain
    /// click fell through to the ordinary click branch and collapsed the selection to the clicked
    /// cell instead of extending it, and F8 mode had no effect on mouse clicks at all.
    /// </summary>
    private bool TryHandleCellAreaExtendClick(CellAddress newAddr)
    {
        if (!ExcelSelectionModePlanner.ShouldExtendSelection(_selectionMode, Keyboard.Modifiers) || !_selectionAnchor.HasValue)
            return false;

        // A Shift-click/F8-extend click onto a locked cell on a protected sheet with "Select
        // locked cells" unchecked must be refused outright, exactly like a plain click onto that
        // same cell is refused by CanSelectCellForClick above -- Excel never lets the highlighted
        // selection extend onto a locked cell at all (R87-commands-protection-lock-5-1). Still
        // reports the click as handled (returns true) so the caller's other click branches below
        // don't also run and collapse the selection instead.
        if (!CanSelectCellForClick(newAddr))
            return true;

        HideValidationDropdown();
        ExtendSelection(_selectionAnchor.Value, newAddr);
        return true;
    }

    private void MainWindow_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Don't steal input from text boxes or combo boxes (formula bar, toolbar dropdowns)
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (SheetGrid.SelectedRange == null) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;

        if (_selectionAnchor.HasValue)
        {
            ShowInlineEditor(_selectionAnchor.Value);
            // R78-render-inplace-editor-5-1: ShowInlineEditor defaults every session to Edit mode
            // (F2/double-click's semantics); a typed character overtypes the selection instead, so
            // this is Excel's "Enter" mode -- override the flag it just set.
            _formulaEditEnteredViaEditKey = false;
            if (_inlineEditor != null)
            {
                _inlineEditor.Text = e.Text;
                _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
                var typedEntryPlan = _formulaRangeEditingSession.ApplyTypedEntry(e.Text);
                ApplyFormulaEditStatusBarPlan(typedEntryPlan.StatusBarPlan);
                RefreshFormulaReferenceHighlights();
            }
        }
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (TryHandleShellFocusCyclePreview(e))
            return;

        if (TryHandleShowKeyTipsPreview(e, sender))
            return;

        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && IsStartScreenVisible())
        {
            HideStartScreen();
            e.Handled = true;
            return;
        }

        if (!KeyboardShortcutMatcher.TryGetCommandShortcut(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                out var commandShortcut))
        {
            return;
        }

        if (commandShortcut is not (KeyboardCommandShortcut.ShowKeyTips or KeyboardCommandShortcut.OpenContextMenu))
            return;

        if (commandShortcut == KeyboardCommandShortcut.OpenContextMenu && TryOpenFocusedBackstageContextMenu())
        {
            e.Handled = true;
            return;
        }

        ExecuteCommandShortcut(commandShortcut, sender, e);
        e.Handled = true;
    }

    private bool TryHandleShowKeyTipsPreview(System.Windows.Input.KeyEventArgs e, object sender)
    {
        if (!KeyboardShortcutMatcher.TryGetCommandShortcut(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                out var commandShortcut) ||
            commandShortcut != KeyboardCommandShortcut.ShowKeyTips)
        {
            return false;
        }

        ExecuteCommandShortcut(commandShortcut, sender, e);
        e.Handled = true;
        return true;
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsControlModifierKey(e))
            SheetGrid.RefreshPointerCursor();

        if (e.Key is System.Windows.Input.Key.CapsLock or System.Windows.Input.Key.NumLock)
            RefreshKeyLockIndicators();

        if (Keyboard.Modifiers == ModifierKeys.None && TryRouteFormulaPointModeKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is not TextBox and not ComboBox)
        {
            var keyTipKey = GetEffectiveKey(e);
            if (IsStandaloneAltKey(keyTipKey) && _ribbonKeyTipMode.IsActive)
            {
                _standaloneAltKeyTipTracker.BeginStandaloneAltCandidate();
                e.Handled = true;
                return;
            }

            if (_ribbonKeyTipMode.IsActive &&
                IsRibbonKeyTipContinuationModifierState(Keyboard.Modifiers))
            {
                HandleActiveRibbonKeyTip(keyTipKey);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt && IsStandaloneAltKey(keyTipKey))
            {
                _standaloneAltKeyTipTracker.BeginStandaloneAltCandidate();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt && TryHandleDirectRibbonKeyTip(keyTipKey))
            {
                _standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();
                e.Handled = true;
                return;
            }

            _standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();

            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (IsStartScreenVisible())
                {
                    HideStartScreen();
                    e.Handled = true;
                    return;
                }

                CancelCopyAndTransientModes();
                e.Handled = true;
                return;
            }

            if (ExcelSelectionModePlanner.TryToggle(e.Key, Keyboard.Modifiers, _selectionMode, out var nextSelectionMode))
            {
                SetSelectionMode(nextSelectionMode);
                e.Handled = true;
                return;
            }

            if (ExcelWorksheetNavigationPlanner.TryToggleEndMode(e.Key, Keyboard.Modifiers, _endMode, out var nextEndMode))
            {
                SetEndMode(nextEndMode);
                e.Handled = true;
                return;
            }

            if (KeyboardShortcutMatcher.TryGetCommandShortcut(e.Key, e.SystemKey, Keyboard.Modifiers, out var commandShortcut))
            {
                if ((commandShortcut == KeyboardCommandShortcut.ClearSelection ||
                     commandShortcut == KeyboardCommandShortcut.ClearSelectionAndEdit) &&
                    Keyboard.FocusedElement is TextBox)
                {
                    return;
                }

                ExecuteCommandShortcut(commandShortcut, sender, e);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetNumberFormatShortcut(e.Key, Keyboard.Modifiers, out var numberFormatShortcut))
            {
                ApplyNumberFormatShortcut(numberFormatShortcut);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetBorderShortcut(e.Key, Keyboard.Modifiers, out var borderShortcut))
            {
                if (borderShortcut == BorderKeyboardShortcut.Outline)
                    ApplyOutlineBorderShortcut();
                else
                    ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());

                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.IsCtrlPlus(e.Key, e.SystemKey, Keyboard.Modifiers))
            {
                ExecuteKeyboardInsert();
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.IsCtrlMinus(e.Key, e.SystemKey, Keyboard.Modifiers))
            {
                ExecuteKeyboardDelete();
                e.Handled = true;
                return;
            }
        }

        if (TryHandleFocusedRibbonKeyboardNavigation(e))
            return;

        if (TryHandleFocusedSheetTabKeyboardNavigation(e))
            return;

        if (TryHandleFocusedTaskPaneKeyboardNavigation(e))
            return;

        if (TryHandleFocusedStatusBarKeyboardNavigation(e))
            return;

        if (TryHandleWholeCellKeyboardShortcuts(sender, e, Keyboard.Modifiers))
            return;

        if (SheetGrid.SelectedRange == null) return;

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool extendSelection = ExcelSelectionModePlanner.ShouldExtendSelection(_selectionMode, Keyboard.Modifiers);
        bool useDataBoundary = ExcelWorksheetNavigationPlanner.ShouldUseDataBoundary(e.Key, Keyboard.Modifiers, _endMode);
        bool ctrlHeld  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (!ExcelWorksheetNavigationPlanner.ShouldHandleWorksheetNavigationKey(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                _endMode))
        {
            return;
        }

        // When Shift or F8 extend mode is active the moving end is _selectionCursor; otherwise it's the active cell.
        var current = extendSelection && _selectionCursor.HasValue
            ? _selectionCursor.Value
            : SheetGrid.SelectedRange.Value.Start;

        var sheet = _workbook.GetSheet(_currentSheetId);
        // Freeze Panes is this window's own state (R89-freeze-split-per-window-1): resolve
        // against GetEffectiveViewState instead of the shared Sheet.FrozenRows/FrozenCols so
        // Page Up/Down pages by THIS window's own scrollable-row/column count.
        var pagingViewState = GetEffectiveViewState(sheet);
        var pagingViewport = SheetGrid.Viewport;
        int pageSize = Math.Max(1, (pagingViewport is null ? 25 : CountScrollableRows(pagingViewport, pagingViewState.FrozenRows)) - 1);
        int colPageSize = Math.Max(1, (pagingViewport is null ? 12 : CountScrollableColumns(pagingViewport, pagingViewState.FrozenCols)) - 1);

        CellAddress? target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            e.Key,
            e.SystemKey,
            Keyboard.Modifiers,
            current,
            colPageSize);

        // Plain (non-data-boundary) single-step navigation off a merged cell must clear the whole
        // merge's far edge, not just current+/-1 -- otherwise the raw +/-1 step still lands inside
        // the same merge, SetActiveCell's own merge lookup re-snaps right back to it, and the key
        // press is silently absorbed (R51-render-merged-cell-edit-nav-3-1/3-2). Wrap every plain
        // step (including Tab/Enter, which never routed through this at all outside of in-edit
        // navigation -- see MainWindow.Editing.cs's AdjustTargetPastMerge) so navigation always
        // steps past a merge in the direction of travel, matching Excel.
        target ??= e.Key switch
        {
            Key.Up    => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, -1)
                                  : AdjustTargetPastMerge(sheet, current,
                                        new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col)),
            Key.Down  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, +1)
                                  : AdjustTargetPastMerge(sheet, current,
                                        new CellAddress(_currentSheetId, Math.Min(current.Row + 1, FreeX.Core.Model.CellAddress.MaxRow), current.Col)),
            Key.Left  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1)
                                  : AdjustTargetPastMerge(sheet, current,
                                        new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u)),
            Key.Right => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, +1)
                                  : AdjustTargetPastMerge(sheet, current,
                                        new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, FreeX.Core.Model.CellAddress.MaxCol))),

            // Home target, including the "End, Home" -> Ctrl+End jump (R82-app-keyboard-nav-5-2).
            // GetHomeNavigationTarget reads _endMode; it must be called here, before that flag is
            // cleared below.
            Key.Home     => GetHomeNavigationTarget(sheet, current, ctrlHeld),
            Key.End      => ctrlHeld ? ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, _currentSheetId) : null,
            Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Row + (uint)pageSize), current.Col),

            // Ready-mode Enter (pressed on an already-selected, non-edited cell) must honor the
            // "After pressing Enter, move selection" option -- both its enable/disable flag and
            // its configured direction -- the same as the in-edit commit path
            // (ExcelEditKeyPlanner.GetEnterTarget via MainWindow.Editing.cs), instead of always
            // hardcoding Down/Up (R82-app-keyboard-nav-5-1).
            Key.Enter => _options.MoveSelectionAfterEnter
                ? AdjustTargetPastMerge(sheet, current, ExcelEditKeyPlanner.GetEnterTarget(
                    current,
                    shiftHeld,
                    FormulaBarWpfInputAdapter.ToFormulaEditorEnterDirection(_options.AfterEnterDirection)))
                : current,
            Key.Tab   => AdjustTargetPastMerge(sheet, current, shiftHeld
                ? new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, FreeX.Core.Model.CellAddress.MaxCol))),
            _         => null
        };

        if (target == null) return;

        if (_endMode)
            SetEndMode(false);

        // Enter and Tab (including Shift variants) move the active cell; they don't extend selection
        bool moveOnly = e.Key is Key.Enter or Key.Tab;

        // The shared planner owns range traversal, cross-area wrapping, and the exact-merged-cell
        // exclusion. WPF retains key conversion and applies only the returned active-cell target.
        var cyclePlan = moveOnly && _selectionMode == ExcelSelectionMode.Normal
            ? GridSelectionNavigationPlanner.PlanCycle(
                sheet,
                SheetGrid.SelectedRange,
                SheetGrid.SelectedRanges,
                _selectionAnchor ?? SheetGrid.SelectedRange.Value.Start,
                e.Key == Key.Tab ? GridSelectionCycleKey.Tab : GridSelectionCycleKey.Enter,
                forward: !shiftHeld)
            : null;
        if (cyclePlan is { } cycle)
        {
            MoveActiveCellWithinSelection(cycle.Target);
            EnsureCellVisible(cycle.Target);
            e.Handled = true;
            return;
        }

        // Arrow/Tab/Enter navigation on a protected sheet must SKIP a locked cell it would
        // otherwise land the active cell on (when "Select locked cells" is unchecked), stepping
        // further in the same direction of travel until a selectable cell is found -- matching
        // Excel -- rather than landing on it like a plain click would refuse to
        // (R75-services-protection-security-4-1). Applies to the plain "move the active cell"
        // outcome below (SetActiveCell) AND to Shift/F8 "extend selection" (ExtendSelection) --
        // Shift+Arrow must not let the extending end of the selection land on/pass a locked cell
        // either (R87-commands-protection-lock-5-1). Add-mode range-extension is left as-is.
        bool willSetActiveCell = !(_selectionMode == ExcelSelectionMode.Add && !moveOnly) &&
            !(extendSelection && !moveOnly && _selectionAnchor.HasValue);
        bool willExtendSelection = _selectionMode != ExcelSelectionMode.Add &&
            extendSelection && !moveOnly && _selectionAnchor.HasValue;
        if ((willSetActiveCell || willExtendSelection) &&
            sheet is { IsProtected: true })
        {
            var adjustedTarget = ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
                _workbook,
                sheet,
                target.Value,
                e.Key,
                shiftHeld);
            if (adjustedTarget is null)
            {
                // No selectable cell exists further in this direction (e.g. every remaining cell
                // to the edge is locked) -- Excel simply doesn't move; consume the key anyway so
                // it doesn't fall through to any other handler.
                e.Handled = true;
                return;
            }

            target = adjustedTarget.Value;
        }

        if (_selectionMode == ExcelSelectionMode.Add && !moveOnly)
            AddOrMoveAdditionalSelection(target.Value, extendSelection);
        else if (extendSelection && !moveOnly && _selectionAnchor.HasValue)
            ExtendSelection(_selectionAnchor.Value, target.Value);
        else
            SetActiveCell(target.Value);

        EnsureCellVisible(target.Value);
        e.Handled = true;
    }

    /// <summary>
    /// R91-app-keyboard-routing-5-1: dispatches the whole-cell/whole-selection keyboard shortcuts
    /// (Ctrl+B/I/U/5 font toggles, Ctrl+Shift+V Paste Special, Ctrl+Space/Shift+Space/Ctrl+Shift+Space
    /// selection, Ctrl+9/Ctrl+0 hide rows/columns) -- but ONLY when the in-place cell editor (or a
    /// ComboBox) does NOT have focus. While editing, Ctrl+B/I/U/5 must not silently mutate the whole
    /// cell's style mid-edit (Excel applies them to the selected text run inside the editor, or
    /// defers to the pending entry -- never a bulk style command while unrelated uncommitted text is
    /// still open), and the rest are equally meaningless (or actively destructive) mid-edit. Returning
    /// false leaves the key unhandled so falls through to the focused TextBox's own default handling
    /// (e.g. Ctrl+A selects the in-box text) instead. Mirrors the Avalonia shell's
    /// IsTextEditingEventSource gate (MainWindow.KeyboardParity.cs) that already suppresses the
    /// equivalent WorkbookShortcutRoute dispatch while its inline cell editor is focused.
    ///
    /// <paramref name="modifiers"/> is threaded explicitly (rather than reading the static
    /// <see cref="Keyboard.Modifiers"/> internally) so this dispatch decision is unit-testable
    /// without depending on real OS-level keyboard state -- only <see cref="Keyboard.FocusedElement"/>
    /// (ordinary WPF logical focus, fully controllable in a test) gates the block.
    /// </summary>
    private bool TryHandleWholeCellKeyboardShortcuts(object sender, System.Windows.Input.KeyEventArgs e, ModifierKeys modifiers)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return false;

        if (KeyboardShortcutMatcher.TryGetFontToggleShortcut(e.Key, modifiers, out var fontToggleShortcut))
        {
            ApplyFontToggleShortcut(fontToggleShortcut);
            e.Handled = true;
            return true;
        }

        if (KeyboardShortcutMatcher.IsPasteSpecialShortcut(e.Key, e.SystemKey, modifiers))
        {
            PasteSpecialBtn_Click(sender, e);
            e.Handled = true;
            return true;
        }
        if (KeyboardShortcutMatcher.TryGetSelectionShortcut(e.Key, modifiers, out var selectionShortcut))
        {
            switch (selectionShortcut)
            {
                case KeyboardSelectionShortcut.SelectAll:
                    SelectAll();
                    break;
                case KeyboardSelectionShortcut.SelectCurrentRegion:
                    SelectCurrentRegionOnly();
                    break;
                case KeyboardSelectionShortcut.SelectWholeColumns:
                    SelectWholeColumnsFromSelection();
                    break;
                case KeyboardSelectionShortcut.SelectWholeRows:
                    SelectWholeRowsFromSelection();
                    break;
            }

            e.Handled = true;
            return true;
        }
        if (KeyboardShortcutMatcher.TryGetGridShortcut(e.Key, modifiers, out var gridShortcut))
        {
            switch (gridShortcut)
            {
                case KeyboardGridShortcut.HideRows:
                    ExecuteRowsHidden(hidden: true);
                    break;
                case KeyboardGridShortcut.UnhideRows:
                    ExecuteRowsHidden(hidden: false);
                    break;
                case KeyboardGridShortcut.HideColumns:
                    ExecuteColumnsHidden(hidden: true);
                    break;
                case KeyboardGridShortcut.UnhideColumns:
                    ExecuteColumnsHidden(hidden: false);
                    break;
            }

            e.Handled = true;
            return true;
        }

        return false;
    }

    // Moves the active cell to `addr` within the CURRENT selection without touching
    // SheetGrid.SelectedRange, so the pre-existing multi-cell marquee stays highlighted
    // (unlike SetActiveCell, which always collapses the selection to a single cell).
    private void MoveActiveCellWithinSelection(CellAddress addr)
    {
        _selectionAnchor = addr;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null)
        {
            sheet.ActiveRow = addr.Row;
            sheet.ActiveCol = addr.Col;
        }

        SetCellAddressBoxSelectionText(FormatCellReference(addr));
        var cell = sheet?.GetCell(addr);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, addr));
        FocusSheetGridIfNeeded();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        UpdateCommentPreview(addr);
    }

    private void CycleSelectionCorner()
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var currentCorner = _selectionCursor ?? _selectionAnchor ?? range.Start;
        var nextCorner = SelectionCornerNavigator.GetNextCorner(range, currentCorner);
        _selectionAnchor = nextCorner;
        _selectionCursor = nextCorner;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        SetFormulaBarSelectionText(FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(nextCorner), nextCorner));
        EnsureCellVisible(nextCorner);
        FocusSheetGridIfNeeded();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void ScrollActiveCellIntoView()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell)
            return;

        EnsureCellVisible(activeCell);
        FocusSheetGridIfNeeded();
    }

    private void SetActiveCell(CellAddress addr)
    {
        if (GetFormulaRangeEntryEditor() is null && GetFormulaReferenceHighlightEditor() is null)
            ClearFormulaRangeEntryState();

        // If the cell belongs to a merged region, select the whole region
        var sheet = _workbook.GetSheet(_currentSheetId);
        var merge = sheet is { MergedRegions.Count: > 0 }
            ? sheet.GetMergeRegion(addr)
            : null;
        if (merge.HasValue)
        {
            _selectionAnchor = merge.Value.Start;
            _selectionCursor = merge.Value.End;
            sheet!.ActiveRow = merge.Value.Start.Row;
            sheet.ActiveCol = merge.Value.Start.Col;
            SetSelectedRangesIfChanged(null);
            SheetGrid.SelectedRange = merge.Value;
            CellAddressBox.Text = FormatNameBoxSelectionText(merge.Value);
            var mergedCell = sheet!.GetCell(merge.Value.Start);
            SetFormulaBarSelectionText(FormatFormulaBarText(mergedCell, merge.Value.Start));
            FocusSheetGridIfNeeded();
            RefreshToolbarAfterSelectionChange();
            RefreshStatusBar();
            RefreshValidationDropdown();
            RefreshDvInputMessage();
            UpdateCommentPreview(merge.Value.Start);
            return;
        }

        _selectionAnchor = addr;
        _selectionCursor = addr;
        if (sheet is not null)
        {
            sheet.ActiveRow = addr.Row;
            sheet.ActiveCol = addr.Col;
        }

        SetSelectedRangesIfChanged(null);
        var selectionRange = new GridRange(addr, addr);
        SheetGrid.SelectedRange = selectionRange;
        SetCellAddressBoxSelectionText(FormatNameBoxSelectionText(selectionRange));

        var cell = sheet?.GetCell(addr);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, addr));
        FocusSheetGridIfNeeded();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        UpdateCommentPreview(addr);
    }

    private void EnsureActiveCellSelection(Sheet? sheet)
    {
        if (sheet is null)
            return;

        if (SheetGrid.SelectedRange is { } selectedRange &&
            selectedRange.Start.Sheet == _currentSheetId &&
            selectedRange.End.Sheet == _currentSheetId)
        {
            return;
        }

        // The selection still describes the sheet we navigated away from. Remember its full
        // selection (range + multi-ranges) so the sheet keeps it, then restore this sheet's
        // selection. When sheets are grouped, mirror the outgoing selection onto this sheet so
        // every grouped sheet shows the same selection, the way Excel does.
        var outgoing = CaptureOutgoingSelection();

        if (outgoing is { } mirrored &&
            _groupedSheetIds.Count > 1 &&
            _groupedSheetIds.Contains(_currentSheetId))
        {
            ApplyWorksheetSelectionSnapshot(mirrored.Remap(_currentSheetId));
            return;
        }

        if (_worksheetSelections.TryGet(_currentSheetId, out var saved))
        {
            ApplyWorksheetSelectionSnapshot(saved);
            return;
        }

        var row = Math.Clamp(sheet.ActiveRow ?? 1u, 1u, CellAddress.MaxRow);
        var col = Math.Clamp(sheet.ActiveCol ?? 1u, 1u, CellAddress.MaxCol);
        SetActiveCell(new CellAddress(_currentSheetId, row, col));
    }

    // Saves the selection that still describes the sheet being navigated away from, so it can be
    // restored later. Returns the captured snapshot, or null when there is nothing coherent to save.
    private WorksheetSelectionSnapshot? CaptureOutgoingSelection()
    {
        if (SheetGrid.SelectedRange is not { } range)
            return null;
        if (_selectionAnchor is not { } anchor || _selectionCursor is not { } cursor)
            return null;

        var outgoingSheet = range.Start.Sheet;
        if (outgoingSheet == _currentSheetId || anchor.Sheet != outgoingSheet)
            return null;

        var snapshot = new WorksheetSelectionSnapshot(anchor, cursor, range, SheetGrid.SelectedRanges);
        _worksheetSelections.Save(outgoingSheet, snapshot);
        return snapshot;
    }

    // Applies a remembered (or mirrored) selection to the grid for the current sheet.
    private void ApplyWorksheetSelectionSnapshot(WorksheetSelectionSnapshot snapshot)
    {
        _selectionMode = ExcelSelectionMode.Normal;
        _selectionAnchor = snapshot.Anchor;
        _selectionCursor = snapshot.Cursor;
        SetSelectedRangesIfChanged(snapshot.AdditionalRanges);
        SheetGrid.SelectedRange = snapshot.PrimaryRange;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null)
        {
            sheet.ActiveRow = snapshot.Anchor.Row;
            sheet.ActiveCol = snapshot.Anchor.Col;
        }

        CellAddressBox.Text = snapshot.PrimaryRange.Start == snapshot.PrimaryRange.End
            ? FormatCellReference(snapshot.Anchor)
            : FormatRangeReference(snapshot.Anchor, snapshot.Cursor);
        SetFormulaBarSelectionText(FormatFormulaBarText(sheet?.GetCell(snapshot.Anchor), snapshot.Anchor));

        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        UpdateCommentPreview(snapshot.Anchor);
    }

    private void ActivateNewWorksheetAtA1(SheetId sheetId)
    {
        _currentSheetId = sheetId;
        CaptureOutgoingSelection();

        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        _worksheetSelections.Remove(_currentSheetId);

        var sheet = _workbook.GetSheet(_currentSheetId);
        sheet?.ResetViewStateToA1();

        VerticalScroll.Value = 1;
        HorizontalScroll.Value = 1;
        SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
    }

    private void SelectCurrentRegionOrAll()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeCell = SheetGrid.SelectedRange?.Start;
        if (sheet is not null &&
            activeCell is { } cell &&
            SelectionRangeService.GetCurrentRegion(sheet, cell) is { } currentRegion &&
            SheetGrid.SelectedRange != currentRegion)
        {
            _selectionAnchor = currentRegion.Start;
            _selectionCursor = currentRegion.End;
            SetSelectedRangesIfChanged(null);
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            var activeCellModel = sheet.GetCell(cell);
            SetFormulaBarSelectionText(FormatFormulaBarText(activeCellModel, cell));
            SheetGrid.Focus();
            RefreshToolbarAfterSelectionChange();
            RefreshStatusBar();
            return;
        }

        SelectAll();
    }

    private void SelectCurrentRegionOnly()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeCell = SheetGrid.SelectedRange?.Start;
        if (sheet is not null &&
            activeCell is { } cell &&
            SelectionRangeService.GetCurrentRegion(sheet, cell) is { } currentRegion)
        {
            _selectionAnchor = currentRegion.Start;
            _selectionCursor = currentRegion.End;
            SetSelectedRangesIfChanged(null);
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            SetFormulaBarSelectionText(FormatFormulaBarText(sheet.GetCell(cell), cell));
            SheetGrid.Focus();
            RefreshToolbarAfterSelectionChange();
            RefreshStatusBar();
        }
    }

    /// <summary>
    /// R91-calc-selection-semantics-5-2: Shift+Space (whole row(s)) inside a structured Table must
    /// scope to the table FIRST -- Excel's 1st press selects just the table row(s) (the table's own
    /// column span, not the whole sheet row), and only a 2nd press on the already-table-scoped
    /// selection escalates to the entire worksheet row(s). Unlike columns there is no header/totals
    /// dimension to step through for rows, so this is a two-step (table row, then sheet row), not
    /// three-step like <see cref="SelectWholeColumnsFromSelection"/>.
    /// </summary>
    private void SelectWholeRowsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null && TryGetTableForSelection(sheet, range, out var table))
        {
            var tableRowRange = new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, table.Range.Start.Col),
                new CellAddress(range.Start.Sheet, range.End.Row, table.Range.End.Col));

            if (range != tableRowRange)
            {
                SetSelectionRange(tableRowRange, range.Start);
                return;
            }
        }

        SetSelectionRange(SelectionRangeService.GetWholeRows(range), range.Start);
    }

    /// <summary>
    /// R91-calc-selection-semantics-5-2: Ctrl+Space (whole column(s)) inside a structured Table must
    /// scope to the table FIRST, matching Excel's documented three-press escalation: 1st press
    /// selects just the table column's DATA cells (excluding the header row(s) and totals row); 2nd
    /// press extends to the whole table column including the header; only a 3rd press escalates to
    /// the entire worksheet column. Previously this jumped straight to the whole sheet column on the
    /// very first press, sweeping in unrelated data below/above the table.
    /// </summary>
    private void SelectWholeColumnsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null && TryGetTableForSelection(sheet, range, out var table))
        {
            var tableRowCount = (int)table.Range.RowCount;
            var headerRows = (uint)Math.Clamp(table.HeaderRowCount.GetValueOrDefault(1), 0, tableRowCount);
            var totalsRows = (uint)Math.Clamp(table.TotalsRowCount ?? (table.TotalsRowShown ? 1 : 0), 0, tableRowCount);
            var dataStartRow = table.Range.Start.Row + headerRows;
            var dataEndRow = table.Range.End.Row - totalsRows;

            if (dataStartRow <= dataEndRow)
            {
                var dataRange = new GridRange(
                    new CellAddress(range.Start.Sheet, dataStartRow, range.Start.Col),
                    new CellAddress(range.Start.Sheet, dataEndRow, range.End.Col));
                var fullTableColumnRange = new GridRange(
                    new CellAddress(range.Start.Sheet, table.Range.Start.Row, range.Start.Col),
                    new CellAddress(range.Start.Sheet, table.Range.End.Row, range.End.Col));

                if (range == fullTableColumnRange)
                {
                    SetSelectionRange(SelectionRangeService.GetWholeColumns(range), range.Start);
                    return;
                }

                if (range != dataRange)
                {
                    SetSelectionRange(dataRange, range.Start);
                    return;
                }

                SetSelectionRange(fullTableColumnRange, range.Start);
                return;
            }
        }

        SetSelectionRange(SelectionRangeService.GetWholeColumns(range), range.Start);
    }

    /// <summary>Finds the structured Table that fully contains <paramref name="range"/> (both
    /// corners), if any -- used to scope Ctrl+Space/Shift+Space's first press(es) to the table
    /// before escalating to the whole sheet column/row.</summary>
    private static bool TryGetTableForSelection(Sheet sheet, GridRange range, out StructuredTableModel table)
    {
        foreach (var candidate in sheet.StructuredTables)
        {
            if (candidate.Range.Contains(range.Start) && candidate.Range.Contains(range.End))
            {
                table = candidate;
                return true;
            }
        }

        table = null!;
        return false;
    }

    // R92-commands-merge-edge-5-2: navigating (Name Box / Go To / hyperlink / any other caller of
    // this shared setter) to a cell COVERED by a merged region must select the WHOLE merge, exactly
    // like clicking that covered cell with the mouse does -- Excel has no independently-selectable
    // sub-cell inside a merge. Expanding here (the single choke point every SetSelectionRange caller
    // funnels through) matches ExtendSelection's ExpandRangeToFullyContainMerges call and
    // AddOrMoveAdditionalSelection/SetActiveCell's GetMergeRegion snap, without needing every
    // call site to know about merges individually. ExpandRangeToFullyContainMerges is a no-op when
    // there are no merges or the range already fully contains every merge it overlaps, so plain
    // single-cell/whole-range navigation is unaffected.
    private void SetSelectionRange(GridRange range, CellAddress activeCell)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var expandedRange = ExpandRangeToFullyContainMerges(sheet, range);
        // If the requested active cell itself sits inside a merge (covered, not the anchor), Excel
        // shows the merge's anchor as the active cell -- e.g. Name Box "B2" into an A1:C3 merge
        // selects A1:C3 with A1 (not B2) as the active/formula-bar cell.
        var effectiveActiveCell = sheet is { MergedRegions.Count: > 0 } && sheet.GetMergeRegion(activeCell) is { } activeMerge
            ? activeMerge.Start
            : activeCell;
        _selectionAnchor = expandedRange.Start;
        _selectionCursor = expandedRange.End;
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = expandedRange;
        CellAddressBox.Text = FormatNameBoxSelectionText(expandedRange);
        var activeCellModel = sheet?.GetCell(effectiveActiveCell);
        SetFormulaBarSelectionText(FormatFormulaBarText(activeCellModel, effectiveActiveCell));
        FocusSheetGridIfNeeded();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void ExtendSelection(CellAddress anchor, CellAddress to)
    {
        if (IsSelectionExtensionUnchanged(anchor, to))
            return;

        ClearSelectionTransientOverlays();

        _selectionCursor = to;
        SetSelectedRangesIfChanged(null);
        var rawRange = new GridRange(
            new CellAddress(_currentSheetId,
                Math.Min(anchor.Row, to.Row), Math.Min(anchor.Col, to.Col)),
            new CellAddress(_currentSheetId,
                Math.Max(anchor.Row, to.Row), Math.Max(anchor.Col, to.Col)));
        // Excel guarantees a selection rectangle never bisects a merged cell: grow the raw
        // rectangle to fully absorb any merge it only partially overlaps
        // (R51-render-merged-cell-edit-nav-3-4).
        var sheet = _workbook.GetSheet(_currentSheetId);
        var range = ExpandRangeToFullyContainMerges(sheet, rawRange);
        SheetGrid.SelectedRange = range;
        // While a mouse-drag selection is in progress, Excel's Name Box shows a live "{rows}R x
        // {cols}C" dimension readout instead of the range address, reverting to the address once
        // the drag ends (CompleteDragSelectionStatusRefresh) (R69-render-active-cell-selection-6-2).
        SetCellAddressBoxSelectionText(_dragSelectActive
            ? GridSelectionNavigationPlanner.FormatDragDimensionText(range)
            : FormatRangeReference(range.Start, range.End));
        if (!_dragSelectActive)
            RefreshPivotFieldListPaneAfterSelectionChange();
        RefreshStatusBarAfterDragSelectionChange();
    }

    // Grows `range` until it fully contains every merged region it partially overlaps, since
    // absorbing one merge can bring a new merge into partial overlap
    // (R51-render-merged-cell-edit-nav-3-4).
    private static GridRange ExpandRangeToFullyContainMerges(Sheet? sheet, GridRange range) =>
        MergedSelectionRangePlanner.ExpandToFullyContainMerges(sheet, range);

    // Excel's Ctrl+Home jumps to the top-left cell of the *scrollable* region -- the first
    // unfrozen row/column -- rather than always to A1 once panes are frozen; plain Home (no
    // Ctrl) still moves to column A of the current row regardless of freeze
    // (R52-render-scroll-viewport-nav-3-1). When End's sticky mode is active, "End, Home"
    // reproduces Ctrl+End instead -- the last used cell on the worksheet -- matching how
    // "End, <arrow>" reproduces Ctrl+<arrow> (R82-app-keyboard-nav-5-2).
    private CellAddress GetHomeNavigationTarget(Sheet? sheet, CellAddress current, bool ctrlHeld) =>
        ExcelWorksheetNavigationPlanner.GetHomeTarget(sheet, _currentSheetId, current, ctrlHeld, _endMode);

    private void AddOrMoveAdditionalSelection(CellAddress target, bool extendSelection)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);

        // Ctrl+clicking anywhere inside a merged cell (not just its own anchor) must add the
        // WHOLE merged block as the new selection area -- Excel has no independently-selectable
        // sub-cell inside a merge (R51-render-merged-cell-edit-nav-3-3). Only applies to a fresh
        // Ctrl+click (not a Ctrl+Shift extension of an already-started area).
        var clickedMerge = !extendSelection && sheet is { MergedRegions.Count: > 0 }
            ? sheet.GetMergeRegion(target)
            : null;
        if (clickedMerge is { } snapTo)
            target = snapTo.End;

        if (IsAdditionalSelectionExtensionUnchanged(target, extendSelection))
            return;

        ClearSelectionTransientOverlays();

        if (!extendSelection)
            _selectionAnchor = clickedMerge?.Start ?? target;

        var anchor = _selectionAnchor ?? target;
        if (anchor.Sheet != target.Sheet)
        {
            anchor = target;
            _selectionAnchor = target;
        }

        _selectionCursor = target;
        var rawActiveRange = new GridRange(anchor, target);
        // Excel guarantees a selection rectangle never bisects a merged cell: mirror the
        // ExtendSelection fix (R51-render-merged-cell-edit-nav-3-4) here too, so extending an
        // in-progress additional (Ctrl+click) selection area also snaps to fully contain any
        // merge it only partially overlaps -- the fresh-click merge-snap above only handles the
        // single clicked cell/merge itself, not a rectangle stretched across it while dragging
        // (R52-meta-2).
        var activeRange = ExpandRangeToFullyContainMerges(sheet, rawActiveRange);
        // Whether this call is starting a genuinely new disjoint area (a fresh Ctrl+click) or
        // extending the area already being drawn (a Ctrl+drag continuation) is known directly from
        // `extendSelection` -- the caller already distinguishes the two (mouse-down passes false,
        // SheetGrid_MouseMove's drag-continuation passes true) -- so pass that through explicitly
        // instead of trying to re-derive it from selection state after the fact
        // (R112-render-cellarea-multiselect-append-fix).
        var ranges = GridSelectionNavigationPlanner.UpdateDisjointSelectionAreas(
            SheetGrid.SelectedRanges,
            SheetGrid.SelectedRange,
            activeRange,
            startNewArea: !extendSelection);

        SetSelectedRangesIfChanged(ranges);
        SheetGrid.SelectedRange = activeRange;
        SetCellAddressBoxSelectionText(FormatRangeReference(activeRange.Start, activeRange.End));

        var formulaBarCell = clickedMerge?.Start ?? target;
        SetFormulaBarSelectionText(FormatFormulaBarText(sheet?.GetCell(formulaBarCell), formulaBarCell));
        FocusSheetGridIfNeeded();
        RefreshToolbarAfterDragSelectionChange();
        RefreshStatusBarAfterDragSelectionChange();
    }

    private bool IsSelectionExtensionUnchanged(CellAddress anchor, CellAddress target) =>
        _selectionCursor == target &&
        SheetGrid.SelectedRanges is null &&
        SheetGrid.SelectedRange is { } range &&
        IsSameNormalizedRange(range, _currentSheetId, anchor, target);

    private bool IsAdditionalSelectionExtensionUnchanged(CellAddress target, bool extendSelection)
    {
        if (!extendSelection || _selectionCursor != target)
            return false;

        var anchor = _selectionAnchor ?? target;
        if (SheetGrid.SelectedRange is not { } activeRange ||
            !IsSameNormalizedRange(activeRange, _currentSheetId, anchor, target))
        {
            return false;
        }

        return true;
    }

    private static bool IsSameNormalizedRange(
        GridRange range,
        SheetId sheetId,
        CellAddress anchor,
        CellAddress target) =>
        anchor.Sheet == sheetId &&
        target.Sheet == sheetId &&
        range.Start.Sheet == sheetId &&
        range.End.Sheet == sheetId &&
        range.Start.Row == Math.Min(anchor.Row, target.Row) &&
        range.Start.Col == Math.Min(anchor.Col, target.Col) &&
        range.End.Row == Math.Max(anchor.Row, target.Row) &&
        range.End.Col == Math.Max(anchor.Col, target.Col);

    private void RefreshToolbarAfterDragSelectionChange()
    {
        if (_dragSelectActive)
        {
            if (_dragSelectToolbarRefreshPending)
                return;

            if (!CanSkipSelectionDragToolbarRefresh())
                _dragSelectToolbarRefreshPending = true;
            return;
        }

        RefreshToolbarAfterSelectionChange();
    }

    private void CompleteDragSelectionToolbarRefresh()
    {
        RefreshPivotFieldListPaneAfterSelectionChange();

        if (!_dragSelectToolbarRefreshPending)
            return;

        _dragSelectToolbarRefreshPending = false;
        if (CanSkipSelectionDragToolbarRefresh())
            return;

        RefreshToolbarAfterSelectionChange();
    }

    private void RefreshStatusBarAfterDragSelectionChange()
    {
        if (_dragSelectActive)
        {
            if (_dragSelectStatusRefreshPending)
                return;

            _dragSelectStatusRefreshPending = true;
            return;
        }

        RefreshStatusBar();
    }

    private void CompleteDragSelectionStatusRefresh()
    {
        // The Name Box shows a live "{rows}R x {cols}C" dimension readout while the drag was in
        // progress (ExtendSelection); now that the drag has ended, revert it to the plain range
        // address, matching Excel (R69-render-active-cell-selection-6-2).
        if (SheetGrid.SelectedRange is { } activeRange)
            SetCellAddressBoxSelectionText(FormatRangeReference(activeRange.Start, activeRange.End));

        if (!_dragSelectStatusRefreshPending)
            return;

        _dragSelectStatusRefreshPending = false;
        RefreshStatusBar();
    }

    private void BeginHeaderSelectionDrag(GridHeaderContextMenuTarget target, uint index)
    {
        _dragHeaderSelectionTarget = target;
        _dragHeaderSelectionAnchor = index;
        _dragSelectionTransientOverlaysCleared = false;
        _dragSelectActive = true;
        SheetGrid.CaptureMouse();
    }

    private GridHeaderContextMenuHit? HitTestHeaderSelection(System.Windows.Point pos)
    {
        var viewport = SheetGrid.Viewport;
        if (viewport is null)
            return null;

        return GridHeaderContextMenuHitPlanner.HitTest(
            viewport,
            pos,
            SheetGrid.ActualRowHeaderWidth,
            SheetGrid.EffectiveColHeaderHeight);
    }

    private void ExtendHeaderSelection(GridHeaderContextMenuTarget target, uint anchorIndex, uint targetIndex)
    {
        if (IsHeaderSelectionExtensionUnchanged(target, anchorIndex, targetIndex))
            return;

        ClearSelectionTransientOverlays();
        SetSelectedRangesIfChanged(null);

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (target == GridHeaderContextMenuTarget.Column)
        {
            var firstCol = Math.Min(anchorIndex, targetIndex);
            var lastCol = Math.Max(anchorIndex, targetIndex);
            var anchor = new CellAddress(_currentSheetId, 1, anchorIndex);
            var cursor = new CellAddress(_currentSheetId, CellAddress.MaxRow, targetIndex);
            var range = new GridRange(
                new CellAddress(_currentSheetId, 1, firstCol),
                new CellAddress(_currentSheetId, CellAddress.MaxRow, lastCol));
            if (TryApplyFormulaRangeSelection(range, anchor, cursor))
                return;

            // Dragging across headers must also fully absorb any merge the swept columns only
            // partially span, matching the plain/Ctrl-click header paths
            // (R99-render-header-select-merge-expand).
            var expandedRange = ExpandRangeToFullyContainMerges(sheet, range);
            _selectionAnchor = anchor;
            _selectionCursor = cursor;
            SheetGrid.SelectedRange = expandedRange;
            var c1 = FormatColumnReference(expandedRange.Start.Col);
            var c2 = FormatColumnReference(expandedRange.End.Col);
            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
        }
        else
        {
            var firstRow = Math.Min(anchorIndex, targetIndex);
            var lastRow = Math.Max(anchorIndex, targetIndex);
            var anchor = new CellAddress(_currentSheetId, anchorIndex, 1);
            var cursor = new CellAddress(_currentSheetId, targetIndex, CellAddress.MaxCol);
            var range = new GridRange(
                new CellAddress(_currentSheetId, firstRow, 1),
                new CellAddress(_currentSheetId, lastRow, CellAddress.MaxCol));
            if (TryApplyFormulaRangeSelection(range, anchor, cursor))
                return;

            var expandedRange = ExpandRangeToFullyContainMerges(sheet, range);
            _selectionAnchor = anchor;
            _selectionCursor = cursor;
            SheetGrid.SelectedRange = expandedRange;
            CellAddressBox.Text = expandedRange.Start.Row == expandedRange.End.Row
                ? $"{expandedRange.Start.Row}:{expandedRange.Start.Row}"
                : $"{expandedRange.Start.Row}:{expandedRange.End.Row}";
        }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        SetFormulaBarSelectionText(FormatFormulaBarText(cell, _selectionAnchor.Value));
        SheetGrid.Focus();
        RefreshToolbarAfterDragSelectionChange();
        RefreshStatusBarAfterDragSelectionChange();
    }

    private bool IsHeaderSelectionExtensionUnchanged(
        GridHeaderContextMenuTarget target,
        uint anchorIndex,
        uint targetIndex)
    {
        if (SheetGrid.SelectedRanges is not null ||
            SheetGrid.SelectedRange is not { } range)
        {
            return false;
        }

        if (target == GridHeaderContextMenuTarget.Column)
        {
            var firstCol = Math.Min(anchorIndex, targetIndex);
            var lastCol = Math.Max(anchorIndex, targetIndex);
            return _selectionAnchor == new CellAddress(_currentSheetId, 1, anchorIndex) &&
                _selectionCursor == new CellAddress(_currentSheetId, CellAddress.MaxRow, targetIndex) &&
                range.Start == new CellAddress(_currentSheetId, 1, firstCol) &&
                range.End == new CellAddress(_currentSheetId, CellAddress.MaxRow, lastCol);
        }

        var firstRow = Math.Min(anchorIndex, targetIndex);
        var lastRow = Math.Max(anchorIndex, targetIndex);
        return _selectionAnchor == new CellAddress(_currentSheetId, anchorIndex, 1) &&
            _selectionCursor == new CellAddress(_currentSheetId, targetIndex, CellAddress.MaxCol) &&
            range.Start == new CellAddress(_currentSheetId, firstRow, 1) &&
            range.End == new CellAddress(_currentSheetId, lastRow, CellAddress.MaxCol);
    }

    private void SetSelectedRangesIfChanged(IReadOnlyList<GridRange>? ranges)
    {
        if (!ReferenceEquals(SheetGrid.SelectedRanges, ranges))
            SheetGrid.SelectedRanges = ranges;
    }

    /// <summary>
    /// Projects the native WPF grid selection into the shared application session. GridView
    /// remains the input/rendering surface during this migration, while WorkbookSession is the
    /// authoritative portable view state consumed by future command slices.
    /// </summary>
    private void SynchronizeWorkbookSessionSelection()
    {
        if (_workbookSessionDisposed)
            return;

        if (!ReferenceEquals(_session.Workbook, _workbook))
            throw new InvalidOperationException("The WPF workbook mirror diverged from WorkbookSession.");

        if (SheetGrid.SelectedRange is not { } primaryRange ||
            primaryRange.Start.Sheet != _currentSheetId ||
            primaryRange.End.Sheet != _currentSheetId)
        {
            return;
        }

        var selectedRanges = SheetGrid.SelectedRanges?
            .Where(range =>
                range.Start.Sheet == _currentSheetId &&
                range.End.Sheet == _currentSheetId)
            .ToList() ?? [primaryRange];
        if (!selectedRanges.Contains(primaryRange))
            selectedRanges.Add(primaryRange);

        var activeCell = _selectionAnchor is { } anchor && primaryRange.Contains(anchor)
            ? anchor
            : primaryRange.Start;
        _session.SynchronizeSelectionState(
            _currentSheetId,
            primaryRange,
            selectedRanges,
            activeCell,
            _groupedSheetIds,
            _sheetGroupAnchor,
            _formulaEditCell);
    }

    /// <summary>
    /// Projects authoritative selection changes made by WorkbookSession (notably Undo/Redo and
    /// formula commits) back into the native WPF grid without moving input/render ownership out
    /// of GridView.
    /// </summary>
    private void ApplyWorkbookSessionSelectionToRenderer()
    {
        if (_workbookSessionDisposed)
            return;
        if (!ReferenceEquals(_session.Workbook, _workbook))
            throw new InvalidOperationException("The WPF workbook mirror diverged from WorkbookSession.");

        var previousSheetId = _currentSheetId;
        _currentSheetId = _session.ActiveSheet.Id;
        var primaryRange = _session.SelectedRange;
        var selectedRanges = _session.SelectedRanges.Count > 0
            ? _session.SelectedRanges.ToArray()
            : [primaryRange];

        _selectionAnchor = _session.ActiveCell;
        _selectionCursor = primaryRange.End;
        SheetGrid.SelectedRange = primaryRange;
        SetSelectedRangesIfChanged(selectedRanges.Length > 1 ? selectedRanges : null);
        CellAddressBox.Text = FormatNameBoxSelectionText(primaryRange);
        SetFormulaBarSelectionText(FormatFormulaBarText(
            _workbook.GetSheet(_currentSheetId)?.GetCell(_session.ActiveCell),
            _session.ActiveCell));

        if (!previousSheetId.Equals(_currentSheetId))
            RefreshSheetTabs();

        EnsureCellVisible(_session.ActiveCell);
    }

    private void SetCellAddressBoxSelectionText(string text)
    {
        if (CellAddressBox.Text == text)
            return;

        if (CellAddressBox.IsKeyboardFocusWithin || !CellAddressBox.IsEditableTextUndoEnabled())
        {
            CellAddressBox.Text = text;
            return;
        }

        CellAddressBox.SetEditableTextUndoEnabled(false);
        try
        {
            CellAddressBox.Text = text;
        }
        finally
        {
            CellAddressBox.SetEditableTextUndoEnabled(true);
        }
    }

    private void SetFormulaBarSelectionText(string text)
    {
        if (FormulaBar.Text == text)
            return;

        if (FormulaBar.IsKeyboardFocusWithin || !FormulaBar.IsUndoEnabled)
        {
            FormulaBar.Text = text;
            return;
        }

        FormulaBar.IsUndoEnabled = false;
        try
        {
            FormulaBar.Text = text;
        }
        finally
        {
            FormulaBar.IsUndoEnabled = true;
        }
    }

    private void ClearSelectionTransientOverlays()
    {
        if (_dragSelectActive)
        {
            if (_dragSelectionTransientOverlaysCleared)
                return;

            _dragSelectionTransientOverlaysCleared = true;
        }

        HideValidationDropdown();
        ClearCommentPreview();
    }

    private CellAddress? HitTestCell(System.Windows.Point pos)
    {
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return null;
        return FreeX.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
    }

    private void SheetGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(SheetGrid);
        var hitAddr = _dragHeaderSelectionTarget.HasValue ? null : HitTestCell(pos);
        if (!_dragSelectActive)
        {
            if (hitAddr.HasValue)
                UpdateCommentPreview(hitAddr.Value);
            else
                ClearCommentPreview();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _formatPainterTargetSelectionActive = false;
            _dragSelectActive = false;
            _dragSelectionTransientOverlaysCleared = false;
            _dragSelectAddsAdditionalRange = false;
            _dragHeaderSelectionTarget = null;
            _dragHeaderSelectionAnchor = 0;
            SheetGrid.ReleaseMouseCapture();
            CompleteDragSelectionToolbarRefresh();
            CompleteDragSelectionStatusRefresh();
            if (hitAddr.HasValue)
                UpdateCommentPreview(hitAddr.Value);
            else
                ClearCommentPreview();
            e.Handled = true;
            return;
        }

        e.Handled = true;
        if (_dragHeaderSelectionTarget is { } headerTarget)
        {
            var headerHit = HitTestHeaderSelection(pos);
            if (headerHit is { } hit && hit.Target == headerTarget)
                ExtendHeaderSelection(headerTarget, _dragHeaderSelectionAnchor, hit.Index);
            return;
        }

        RequestSelectionDragAutoScroll(pos);
        if (!hitAddr.HasValue)
            ClearCommentPreview();

        if (_selectionAnchor is not { } anchor) return;
        if (hitAddr.HasValue && GetFormulaRangeEntryEditor() is not null)
            TryApplyFormulaRangeSelection(hitAddr.Value, extendSelection: true);
        else if (hitAddr.HasValue && _selectionAnchor is { } formulaSourceAnchor &&
                 TryRouteFormulaPointModeSelection(
                     new GridRange(
                         new CellAddress(_currentSheetId,
                             Math.Min(formulaSourceAnchor.Row, hitAddr.Value.Row),
                             Math.Min(formulaSourceAnchor.Col, hitAddr.Value.Col)),
                         new CellAddress(_currentSheetId,
                             Math.Max(formulaSourceAnchor.Row, hitAddr.Value.Row),
                             Math.Max(formulaSourceAnchor.Col, hitAddr.Value.Col))),
                     extendSelection: true))
        {
            _selectionCursor = hitAddr.Value;
        }
        else if (hitAddr.HasValue && _dragSelectAddsAdditionalRange)
            AddOrMoveAdditionalSelection(hitAddr.Value, extendSelection: true);
        else if (hitAddr.HasValue)
            ExtendSelection(anchor, hitAddr.Value);
    }

    private void RequestSelectionDragAutoScroll(System.Windows.Point pos)
    {
        var request = FreeX.App.UI.GridView.CalculateAutofillEdgeScrollIntent(
            pos.X,
            pos.Y,
            SheetGrid.ActualWidth,
            SheetGrid.ActualHeight,
            SheetGrid.ActualRowHeaderWidth,
            SheetGrid.EffectiveColHeaderHeight);

        if (request.HasAnyDirection)
            OnAutofillEdgeScrollRequested(request);
    }

    private void UpdateCommentPreview(CellAddress address)
    {
        SetCommentPreview(null);
    }

    private void ClearCommentPreview()
    {
        SheetGrid.HideCommentPreview();
        SetCommentPreview(null);
    }

    private void SetCommentPreview(string? preview)
    {
        if (SheetGrid.ToolTip is not null)
            SheetGrid.ToolTip = null;
    }

    private void SheetGrid_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(SheetGrid);
        var hitAddr = HitTestCell(pos);

        if (_formatPainterTargetSelectionActive)
        {
            _formatPainterTargetSelectionActive = false;
            _dragSelectActive = false;
            _dragSelectionTransientOverlaysCleared = false;
            _dragSelectAddsAdditionalRange = false;
            _dragHeaderSelectionTarget = null;
            _dragHeaderSelectionAnchor = 0;
            SheetGrid.ReleaseMouseCapture();
            CompleteDragSelectionToolbarRefresh();
            CompleteDragSelectionStatusRefresh();

            if (SheetGrid.SelectedRange is { } selectedRange)
                TryApplyFormatPainter(selectedRange);
            if (hitAddr.HasValue)
                UpdateCommentPreview(hitAddr.Value);
            else
                ClearCommentPreview();

            e.Handled = true;
            return;
        }

        if (!_dragSelectActive) return;
        _dragSelectActive = false;
        _dragSelectionTransientOverlaysCleared = false;
        _dragSelectAddsAdditionalRange = false;
        _dragHeaderSelectionTarget = null;
        _dragHeaderSelectionAnchor = 0;
        SheetGrid.ReleaseMouseCapture();
        CompleteDragSelectionToolbarRefresh();
        CompleteDragSelectionStatusRefresh();
        if (hitAddr.HasValue)
            UpdateCommentPreview(hitAddr.Value);
        else
            ClearCommentPreview();
        if (_borderDrawMode != BorderDrawMode.None && SheetGrid.SelectedRange is { } borderDrawRange)
        {
            ApplyBorderDrawMode(borderDrawRange);
            e.Handled = true;
            return;
        }
        GetFormulaRangeEntryEditor()?.Focus();
        e.Handled = true;
    }

    private void SheetGrid_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_dragSelectActive &&
            !_formatPainterTargetSelectionActive &&
            !_dragSelectAddsAdditionalRange &&
            !_dragHeaderSelectionTarget.HasValue)
        {
            return;
        }

        _formatPainterTargetSelectionActive = false;
        _dragSelectActive = false;
        _dragSelectAddsAdditionalRange = false;
        _dragSelectionTransientOverlaysCleared = false;
        _dragHeaderSelectionTarget = null;
        _dragHeaderSelectionAnchor = 0;
        CompleteDragSelectionToolbarRefresh();
        CompleteDragSelectionStatusRefresh();
    }

}
