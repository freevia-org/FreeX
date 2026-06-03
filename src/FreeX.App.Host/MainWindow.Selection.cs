using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void SelectRow(uint row)
    {
        ClearSelectionTransientOverlays();
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, row, 1);
        _selectionCursor = new CellAddress(_currentSheetId, row, maxCol);
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = $"{row}:{row}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void SelectColumn(uint col)
    {
        ClearSelectionTransientOverlays();
        const uint maxRow = 1_048_576;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, col);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, col);
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        var colName = FormatColumnReference(col);
        CellAddressBox.Text = $"{colName}:{colName}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void SelectAll()
    {
        ClearSelectionTransientOverlays();
        const uint maxRow = 1_048_576;
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, 1);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, maxCol);
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = FormatCellReference(_selectionAnchor.Value);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        var pos = e.GetPosition(SheetGrid);
        const double colHeaderH = FreeX.App.UI.GridView.ColHeaderHeight;
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
                            HideValidationDropdown();
                            ClearCommentPreview();
                            uint anchorCol = _selectionAnchor.Value.Col;
                            _selectionCursor = new CellAddress(_currentSheetId, 1_048_576, cm.Col);
                            SetSelectedRangesIfChanged(null);
                            SheetGrid.SelectedRange = new GridRange(
                                new CellAddress(_currentSheetId, 1, Math.Min(anchorCol, cm.Col)),
                                new CellAddress(_currentSheetId, 1_048_576, Math.Max(anchorCol, cm.Col)));
                            var c1 = FormatColumnReference(Math.Min(anchorCol, cm.Col));
                            var c2 = FormatColumnReference(Math.Max(anchorCol, cm.Col));
                            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
                            var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
                            FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
                            SheetGrid.Focus();
                            RefreshToolbarAfterSelectionChange();
                            RefreshStatusBar();
                            BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Column, anchorCol);
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
                        HideValidationDropdown();
                        ClearCommentPreview();
                        uint anchorRow = _selectionAnchor.Value.Row;
                        _selectionCursor = new CellAddress(_currentSheetId, rm.Row, 16_384);
                        SetSelectedRangesIfChanged(null);
                        SheetGrid.SelectedRange = new GridRange(
                            new CellAddress(_currentSheetId, Math.Min(anchorRow, rm.Row), 1),
                            new CellAddress(_currentSheetId, Math.Max(anchorRow, rm.Row), 16_384));
                        var r1 = Math.Min(anchorRow, rm.Row);
                        var r2 = Math.Max(anchorRow, rm.Row);
                        CellAddressBox.Text = r1 == r2 ? $"{r1}:{r1}" : $"{r1}:{r2}";
                        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
                        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
                        SheetGrid.Focus();
                        RefreshToolbarAfterSelectionChange();
                        RefreshStatusBar();
                        BeginHeaderSelectionDrag(GridHeaderContextMenuTarget.Row, anchorRow);
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

            if (TryApplyFormulaRangeSelection(newAddr, extendSelection: (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
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

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
            {
                HideValidationDropdown();
                ExtendSelection(_selectionAnchor.Value, newAddr);
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
                SetActiveCell(newAddr);
                if (e.ClickCount == 2)
                {
                    if (!TryShowPivotTableDetails(showMessage: false))
                        EnterEditMode();
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
            if (_inlineEditor != null)
            {
                _inlineEditor.Text = e.Text;
                _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
                _formulaRangeEntryMode = FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText(e.Text);
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
        if (Keyboard.FocusedElement is not TextBox and not ComboBox)
        {
            var keyTipKey = GetEffectiveKey(e);
            if (IsStandaloneAltKey(keyTipKey) && _ribbonKeyTipMode.IsActive)
            {
                _standaloneAltKeyTipTracker.BeginStandaloneAltCandidate();
                e.Handled = true;
                return;
            }

            if (_ribbonKeyTipMode.IsActive && Keyboard.Modifiers == ModifierKeys.None)
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

        if (KeyboardShortcutMatcher.TryGetFontToggleShortcut(e.Key, Keyboard.Modifiers, out var fontToggleShortcut))
        {
            var button = fontToggleShortcut switch
            {
                FontToggleShortcut.Bold => BoldButton,
                FontToggleShortcut.Italic => ItalicButton,
                FontToggleShortcut.Strikethrough => StrikeButton,
                _ => UnderlineButton
            };
            ApplyFontToggleShortcut(fontToggleShortcut, button);
            e.Handled = true;
            return;
        }

        if (KeyboardShortcutMatcher.IsPasteSpecialShortcut(e.Key, e.SystemKey, Keyboard.Modifiers))
        {
            PasteSpecialBtn_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (KeyboardShortcutMatcher.TryGetSelectionShortcut(e.Key, Keyboard.Modifiers, out var selectionShortcut))
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
            return;
        }
        if (KeyboardShortcutMatcher.TryGetGridShortcut(e.Key, Keyboard.Modifiers, out var gridShortcut))
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
            return;
        }

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
        int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
        int colPageSize = Math.Max(1, (SheetGrid.Viewport?.ColMetrics.Count ?? 12) - 1);

        CellAddress? target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            e.Key,
            e.SystemKey,
            Keyboard.Modifiers,
            current,
            colPageSize);

        target ??= e.Key switch
        {
            Key.Up    => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, -1)
                                  : new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            Key.Down  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, +1)
                                  : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, FreeX.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Left  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, +1)
                                  : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, FreeX.Core.Model.CellAddress.MaxCol)),

            Key.Home     => new CellAddress(_currentSheetId, ctrlHeld ? 1u : current.Row, 1u),
            Key.End      => ctrlHeld ? ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, _currentSheetId) : null,
            Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Row + (uint)pageSize), current.Col),

            Key.Enter => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col)
                : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, FreeX.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Tab   => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, FreeX.Core.Model.CellAddress.MaxCol)),
            _         => null
        };

        if (target == null) return;

        if (_endMode)
            SetEndMode(false);

        // Enter and Tab (including Shift variants) move the active cell; they don't extend selection
        bool moveOnly = e.Key is Key.Enter or Key.Tab;
        if (_selectionMode == ExcelSelectionMode.Add && !moveOnly)
            AddOrMoveAdditionalSelection(target.Value, extendSelection);
        else if (extendSelection && !moveOnly && _selectionAnchor.HasValue)
            ExtendSelection(_selectionAnchor.Value, target.Value);
        else
            SetActiveCell(target.Value);

        EnsureCellVisible(target.Value);
        e.Handled = true;
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
        FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(nextCorner), nextCorner);
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
        if (GetFormulaRangeEntryEditor() is null)
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
            CellAddressBox.Text = FormatCellReference(merge.Value.Start);
            var mergedCell = sheet!.GetCell(merge.Value.Start);
            FormulaBar.Text = FormatFormulaBarText(mergedCell, merge.Value.Start);
            FocusSheetGridIfNeeded();
            RefreshToolbarAfterSelectionChange();
            RefreshStatusBar();
            RefreshValidationDropdown();
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
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        SetCellAddressBoxSelectionText(FormatCellReference(addr));

        var cell = sheet?.GetCell(addr);
        FormulaBar.Text = FormatFormulaBarText(cell, addr);
        FocusSheetGridIfNeeded();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
        RefreshValidationDropdown();
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
        FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(snapshot.Anchor), snapshot.Anchor);

        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
        RefreshValidationDropdown();
        UpdateCommentPreview(snapshot.Anchor);
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
            FormulaBar.Text = FormatFormulaBarText(activeCellModel, cell);
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
            FormulaBar.Text = FormatFormulaBarText(sheet.GetCell(cell), cell);
            SheetGrid.Focus();
            RefreshToolbarAfterSelectionChange();
            RefreshStatusBar();
        }
    }

    private void SelectWholeRowsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetSelectionRange(SelectionRangeService.GetWholeRows(range), range.Start);
    }

    private void SelectWholeColumnsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetSelectionRange(SelectionRangeService.GetWholeColumns(range), range.Start);
    }

    private void SetSelectionRange(GridRange range, CellAddress activeCell)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        var activeCellModel = sheet?.GetCell(activeCell);
        FormulaBar.Text = FormatFormulaBarText(activeCellModel, activeCell);
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
        SheetGrid.SelectedRange = new GridRange(
            new CellAddress(_currentSheetId,
                Math.Min(anchor.Row, to.Row), Math.Min(anchor.Col, to.Col)),
            new CellAddress(_currentSheetId,
                Math.Max(anchor.Row, to.Row), Math.Max(anchor.Col, to.Col)));
        SetCellAddressBoxSelectionText(FormatRangeReference(anchor, to));
        RefreshStatusBarAfterDragSelectionChange();
    }

    private void AddOrMoveAdditionalSelection(CellAddress target, bool extendSelection)
    {
        if (IsAdditionalSelectionExtensionUnchanged(target, extendSelection))
            return;

        ClearSelectionTransientOverlays();

        if (!extendSelection)
            _selectionAnchor = target;

        var anchor = _selectionAnchor ?? target;
        if (anchor.Sheet != target.Sheet)
        {
            anchor = target;
            _selectionAnchor = target;
        }

        _selectionCursor = target;
        var activeRange = new GridRange(anchor, target);
        var ranges = CreateAdditionalSelectionRanges(
            SheetGrid.SelectedRanges,
            SheetGrid.SelectedRange,
            activeRange);

        SetSelectedRangesIfChanged(ranges);
        SheetGrid.SelectedRange = activeRange;
        SetCellAddressBoxSelectionText(FormatRangeReference(activeRange.Start, activeRange.End));

        var sheet = _workbook.GetSheet(_currentSheetId);
        FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(target), target);
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

        if (target == GridHeaderContextMenuTarget.Column)
        {
            var firstCol = Math.Min(anchorIndex, targetIndex);
            var lastCol = Math.Max(anchorIndex, targetIndex);
            _selectionAnchor = new CellAddress(_currentSheetId, 1, anchorIndex);
            _selectionCursor = new CellAddress(_currentSheetId, 1_048_576, targetIndex);
            SheetGrid.SelectedRange = new GridRange(
                new CellAddress(_currentSheetId, 1, firstCol),
                new CellAddress(_currentSheetId, 1_048_576, lastCol));
            var c1 = FormatColumnReference(firstCol);
            var c2 = FormatColumnReference(lastCol);
            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
        }
        else
        {
            var firstRow = Math.Min(anchorIndex, targetIndex);
            var lastRow = Math.Max(anchorIndex, targetIndex);
            _selectionAnchor = new CellAddress(_currentSheetId, anchorIndex, 1);
            _selectionCursor = new CellAddress(_currentSheetId, targetIndex, 16_384);
            SheetGrid.SelectedRange = new GridRange(
                new CellAddress(_currentSheetId, firstRow, 1),
                new CellAddress(_currentSheetId, lastRow, 16_384));
            CellAddressBox.Text = firstRow == lastRow ? $"{firstRow}:{firstRow}" : $"{firstRow}:{lastRow}";
        }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
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
                _selectionCursor == new CellAddress(_currentSheetId, 1_048_576, targetIndex) &&
                range.Start == new CellAddress(_currentSheetId, 1, firstCol) &&
                range.End == new CellAddress(_currentSheetId, 1_048_576, lastCol);
        }

        var firstRow = Math.Min(anchorIndex, targetIndex);
        var lastRow = Math.Max(anchorIndex, targetIndex);
        return _selectionAnchor == new CellAddress(_currentSheetId, anchorIndex, 1) &&
            _selectionCursor == new CellAddress(_currentSheetId, targetIndex, 16_384) &&
            range.Start == new CellAddress(_currentSheetId, firstRow, 1) &&
            range.End == new CellAddress(_currentSheetId, lastRow, 16_384);
    }

    private void SetSelectedRangesIfChanged(IReadOnlyList<GridRange>? ranges)
    {
        if (!ReferenceEquals(SheetGrid.SelectedRanges, ranges))
            SheetGrid.SelectedRanges = ranges;
    }

    private void SetCellAddressBoxSelectionText(string text)
    {
        if (CellAddressBox.Text == text)
            return;

        if (CellAddressBox.IsKeyboardFocusWithin || !CellAddressBox.IsUndoEnabled)
        {
            CellAddressBox.Text = text;
            return;
        }

        CellAddressBox.IsUndoEnabled = false;
        try
        {
            CellAddressBox.Text = text;
        }
        finally
        {
            CellAddressBox.IsUndoEnabled = true;
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
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
        {
            ClearCommentPreview();
            return;
        }

        if (sheet.Comments.Count == 0 &&
            sheet.ThreadedComments.Count == 0)
        {
            ClearCommentPreview();
            return;
        }

        var preview = CommentNavigationPlanner.FormatCellCommentPreview(
            sheet.Comments,
            sheet.ThreadedComments,
            new CellAddress(_currentSheetId, address.Row, address.Col));
        SetCommentPreview(preview);
    }

    private void ClearCommentPreview() => SetCommentPreview(null);

    private void SetCommentPreview(string? preview)
    {
        if (!Equals(SheetGrid.ToolTip, preview))
            SheetGrid.ToolTip = preview;
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

    private static IReadOnlyList<GridRange> CreateAdditionalSelectionRanges(
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? currentActive,
        GridRange activeRange)
    {
        var hasExistingRanges = selectedRanges is { Count: > 0 };
        var ranges = selectedRanges as MutableSelectionRanges ??
            (hasExistingRanges
                ? new MutableSelectionRanges(selectedRanges!)
                : new MutableSelectionRanges(activeRange));

        if (ranges.Count > 0 &&
            currentActive is { } active &&
            ranges[ranges.Count - 1] == active)
        {
            ranges.ReplaceLast(activeRange);
        }
        else if (hasExistingRanges)
        {
            ranges.Add(activeRange);
        }
        else
        {
            ranges.ReplaceLast(activeRange);
        }

        return ranges;
    }

    private sealed class MutableSelectionRanges : IReadOnlyList<GridRange>
    {
        private GridRange[] _ranges;

        public MutableSelectionRanges(GridRange range)
        {
            _ranges = [range];
            Count = 1;
        }

        public MutableSelectionRanges(IReadOnlyList<GridRange> ranges)
        {
            Count = ranges.Count;
            _ranges = new GridRange[Math.Max(Count, 1)];
            for (var i = 0; i < Count; i++)
                _ranges[i] = ranges[i];
        }

        public int Count { get; private set; }

        public GridRange this[int index] => _ranges[index];

        public void ReplaceLast(GridRange range)
        {
            if (Count == 0)
            {
                Add(range);
                return;
            }

            _ranges[Count - 1] = range;
        }

        public void Add(GridRange range)
        {
            if (Count == _ranges.Length)
                Array.Resize(ref _ranges, Math.Max(Count * 2, 1));

            _ranges[Count++] = range;
        }

        public IEnumerator<GridRange> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return _ranges[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
