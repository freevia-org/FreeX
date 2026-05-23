using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void SelectRow(uint row)
    {
        HideValidationDropdown();
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, row, 1);
        _selectionCursor = new CellAddress(_currentSheetId, row, maxCol);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = $"{row}:{row}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectColumn(uint col)
    {
        HideValidationDropdown();
        const uint maxRow = 1_048_576;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, col);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, col);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        var colName = FormatColumnReference(col);
        CellAddressBox.Text = $"{colName}:{colName}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectAll()
    {
        HideValidationDropdown();
        const uint maxRow = 1_048_576;
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, 1);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, maxCol);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = FormatCellReference(_selectionAnchor.Value);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SheetGrid);
        const double colHeaderH = Freexcel.App.UI.GridView.ColHeaderHeight;
        double rowHeaderW = SheetGrid.ActualRowHeaderWidth;

        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        // ── Header area ───────────────────────────────────────────────────────
        if (pos.X < rowHeaderW || pos.Y < colHeaderH)
        {
            // Top-left corner: select all
            if (pos.X < rowHeaderW && pos.Y < colHeaderH)
            {
                SelectAll();
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
                            _selectionCursor = new CellAddress(_currentSheetId, 1_048_576, cm.Col);
                            SheetGrid.SelectedRange = new GridRange(
                                new CellAddress(_currentSheetId, 1, Math.Min(anchorCol, cm.Col)),
                                new CellAddress(_currentSheetId, 1_048_576, Math.Max(anchorCol, cm.Col)));
                            var c1 = FormatColumnReference(Math.Min(anchorCol, cm.Col));
                            var c2 = FormatColumnReference(Math.Max(anchorCol, cm.Col));
                            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
                        }
                        else
                        {
                            SelectColumn(cm.Col);
                        }
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
                        _selectionCursor = new CellAddress(_currentSheetId, rm.Row, 16_384);
                        SheetGrid.SelectedRange = new GridRange(
                            new CellAddress(_currentSheetId, Math.Min(anchorRow, rm.Row), 1),
                            new CellAddress(_currentSheetId, Math.Max(anchorRow, rm.Row), 16_384));
                        var r1 = Math.Min(anchorRow, rm.Row);
                        var r2 = Math.Max(anchorRow, rm.Row);
                        CellAddressBox.Text = r1 == r2 ? $"{r1}:{r1}" : $"{r1}:{r2}";
                    }
                    else
                    {
                        SelectRow(rm.Row);
                    }
                    return;
                }
            }
            return;
        }

        // ── Cell area ─────────────────────────────────────────────────────────

        if (_formulaTraceArrows.Count > 0 &&
            Freexcel.App.UI.GridView.HitTestFormulaTraceMarker(viewport, _formulaTraceArrows, _currentSheetId, pos) is { } traceTarget)
        {
            NavigateToCell(traceTarget);
            RefreshSheetTabs();
            RefreshToolbar();
            RefreshStatusBar();
            e.Handled = true;
            return;
        }

        _activeSplitPaneRegion = Freexcel.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);
        var hitAddress = Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
        if (hitAddress is { } newAddr)
        {
            if (TryApplyFormulaRangeSelection(newAddr, extendSelection: (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
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
                    e.Handled = true;
                    return;
                }

                SetActiveCell(newAddr);
                _formatPainterTargetSelectionActive = true;
                _dragSelectActive = true;
                SheetGrid.CaptureMouse();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
            {
                ExtendSelection(_selectionAnchor.Value, newAddr);
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
                    _dragSelectActive = true;
                    SheetGrid.CaptureMouse();
                }
            }
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

        ExecuteCommandShortcut(commandShortcut, sender, e);
        e.Handled = true;
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
                                  : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Left  => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => useDataBoundary ? ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, +1)
                                  : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),

            Key.Home     => new CellAddress(_currentSheetId, ctrlHeld ? 1u : current.Row, 1u),
            Key.End      => ctrlHeld ? ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, _currentSheetId) : null,
            Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Row + (uint)pageSize), current.Col),

            Key.Enter => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col)
                : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Tab   => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),
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

    private bool TryHandleShellFocusCyclePreview(System.Windows.Input.KeyEventArgs e)
    {
        if (!KeyboardShortcutMatcher.TryGetCommandShortcut(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                out var commandShortcut) ||
            commandShortcut != KeyboardCommandShortcut.CycleShellFocus)
        {
            return false;
        }

        ExecuteCommandShortcut(commandShortcut, this, e);
        e.Handled = true;
        return true;
    }

    private bool TryHandleFocusedRibbonKeyboardNavigation(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
            !IsInsideRibbonSurface(focusedElement) ||
            Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
        {
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool IsInsideRibbonSurface(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParentForKeyboardFocus(current))
        {
            if (ReferenceEquals(current, RibbonTabs))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetTreeParentForKeyboardFocus(DependencyObject element)
    {
        if (element is Visual)
        {
            var visualParent = VisualTreeHelper.GetParent(element);
            if (visualParent is not null)
                return visualParent;
        }

        return LogicalTreeHelper.GetParent(element);
    }

    private void CycleShellFocus(bool reverse)
    {
        var current = GetCurrentShellFocusTarget();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            current = ShellFocusCyclePlanner.GetNext(current, reverse);
            if (FocusShellRegion(current))
                return;
        }
    }

    private ShellFocusTarget GetCurrentShellFocusTarget()
    {
        if (Keyboard.FocusedElement is DependencyObject focusedElement)
        {
            if (IsInsideRibbonSurface(focusedElement))
                return ShellFocusTarget.Ribbon;

            if (ReferenceEquals(focusedElement, FormulaBar) ||
                ReferenceEquals(focusedElement, CellAddressBox) ||
                ReferenceEquals(focusedElement, FormulaBarExpandBtn) ||
                IsDescendantOf(focusedElement, FormulaBarBorder))
            {
                return ShellFocusTarget.FormulaBar;
            }

            if (ReferenceEquals(focusedElement, SheetNavLeftBtn) ||
                ReferenceEquals(focusedElement, SheetNavRightBtn) ||
                ReferenceEquals(focusedElement, AddSheetButton) ||
                ReferenceEquals(focusedElement, HorizontalScroll) ||
                IsDescendantOf(focusedElement, SheetTabsScroller))
            {
                return ShellFocusTarget.SheetTabs;
            }

            if (IsDescendantOf(focusedElement, StatusBarGrid))
                return ShellFocusTarget.StatusBar;
        }

        return ShellFocusTarget.Worksheet;
    }

    private bool FocusShellRegion(ShellFocusTarget target)
    {
        switch (target)
        {
            case ShellFocusTarget.Ribbon:
                if (RibbonTabs?.SelectedItem is TabItem selectedTab && selectedTab.Focus())
                    return true;
                return RibbonTabs?.Focus() == true;

            case ShellFocusTarget.FormulaBar:
                if (FormulaBarBorder?.Visibility != Visibility.Visible)
                    return false;
                return FormulaBar.Focus();

            case ShellFocusTarget.SheetTabs:
                return AddSheetButton.Focus();

            case ShellFocusTarget.StatusBar:
                return ZoomSlider.Focus();

            default:
                FocusSheetGridIfNeeded();
                return true;
        }
    }

    private static bool IsDescendantOf(DependencyObject element, DependencyObject? ancestor)
    {
        if (ancestor is null)
            return false;

        for (DependencyObject? current = element; current is not null; current = GetTreeParentForKeyboardFocus(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private void ExecuteCommandShortcut(KeyboardCommandShortcut shortcut, object sender, RoutedEventArgs e)
    {
        _keyboardCommandDispatcher.TryExecute(shortcut, sender, e);
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
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ScrollActiveCellIntoView()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell)
            return;

        EnsureCellVisible(activeCell);
        FocusSheetGridIfNeeded();
    }

    private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var keyTipKey = GetEffectiveKey(e);
        if (!_standaloneAltKeyTipTracker.ShouldToggleOnKeyUp(keyTipKey))
            return;

        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
        else
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);

        e.Handled = true;
    }

    private void SetActiveCell(CellAddress addr)
    {
        if (GetFormulaRangeEntryEditor() is null)
            ClearFormulaRangeEntryState();

        // If the cell belongs to a merged region, select the whole region
        var sheet = _workbook.GetSheet(_currentSheetId);
        var merge = sheet?.GetMergeRegion(addr);
        if (merge.HasValue)
        {
            _selectionAnchor = merge.Value.Start;
            _selectionCursor = merge.Value.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = merge.Value;
            CellAddressBox.Text = FormatCellReference(merge.Value.Start);
            var mergedCell = sheet!.GetCell(merge.Value.Start);
            FormulaBar.Text = FormatFormulaBarText(mergedCell, merge.Value.Start);
            FocusSheetGridIfNeeded();
            RefreshToolbar();
            RefreshStatusBar();
            RefreshValidationDropdown();
            return;
        }

        _selectionAnchor = addr;
        _selectionCursor = addr;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        CellAddressBox.Text = FormatCellReference(addr);

        var cell = sheet?.GetCell(addr);
        FormulaBar.Text = FormatFormulaBarText(cell, addr);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
        RefreshValidationDropdown();
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
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            var activeCellModel = sheet.GetCell(cell);
            FormulaBar.Text = FormatFormulaBarText(activeCellModel, cell);
            SheetGrid.Focus();
            RefreshToolbar();
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
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            FormulaBar.Text = FormatFormulaBarText(sheet.GetCell(cell), cell);
            SheetGrid.Focus();
            RefreshToolbar();
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
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        var activeCellModel = sheet?.GetCell(activeCell);
        FormulaBar.Text = FormatFormulaBarText(activeCellModel, activeCell);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExtendSelection(CellAddress anchor, CellAddress to)
    {
        _selectionCursor = to;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(
            new CellAddress(_currentSheetId,
                Math.Min(anchor.Row, to.Row), Math.Min(anchor.Col, to.Col)),
            new CellAddress(_currentSheetId,
                Math.Max(anchor.Row, to.Row), Math.Max(anchor.Col, to.Col)));
        CellAddressBox.Text = FormatRangeReference(anchor, to);
        RefreshStatusBar();
    }

    private void AddOrMoveAdditionalSelection(CellAddress target, bool extendSelection)
    {
        var ranges = SheetGrid.SelectedRanges is { Count: > 0 }
            ? SheetGrid.SelectedRanges.ToList()
            : SheetGrid.SelectedRange is { } currentRange ? [currentRange] : [];

        if (!extendSelection)
            _selectionAnchor = target;

        var anchor = _selectionAnchor ?? target;
        _selectionCursor = target;
        var activeRange = new GridRange(anchor, target);

        if (ranges.Count > 0 && SheetGrid.SelectedRange is { } currentActive && ranges[^1] == currentActive)
            ranges[^1] = activeRange;
        else
            ranges.Add(activeRange);

        SheetGrid.SelectedRanges = ranges;
        SheetGrid.SelectedRange = activeRange;
        CellAddressBox.Text = FormatRangeReference(activeRange.Start, activeRange.End);

        var sheet = _workbook.GetSheet(_currentSheetId);
        FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(target), target);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private CellAddress? HitTestCell(System.Windows.Point pos)
    {
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return null;
        return Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
    }

    private void SheetGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragSelectActive || e.LeftButton != MouseButtonState.Pressed) return;
        if (_selectionAnchor is not { } anchor) return;
        var hitAddr = HitTestCell(e.GetPosition(SheetGrid));
        if (hitAddr.HasValue && GetFormulaRangeEntryEditor() is not null)
            TryApplyFormulaRangeSelection(hitAddr.Value, extendSelection: true);
        else if (hitAddr.HasValue)
            ExtendSelection(anchor, hitAddr.Value);
    }

    private void SheetGrid_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_formatPainterTargetSelectionActive)
        {
            _formatPainterTargetSelectionActive = false;
            _dragSelectActive = false;
            SheetGrid.ReleaseMouseCapture();

            if (SheetGrid.SelectedRange is { } selectedRange)
                TryApplyFormatPainter(selectedRange);

            e.Handled = true;
            return;
        }

        if (!_dragSelectActive) return;
        _dragSelectActive = false;
        SheetGrid.ReleaseMouseCapture();
        GetFormulaRangeEntryEditor()?.Focus();
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();
        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
    }

}
