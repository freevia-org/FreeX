using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Cells group (pickers) ────────────────────────────────────────────────

    private void InsertPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertCellsMenuItem_Click(sender, e);
    }
    private void DeletePickerBtn_Click(object sender, RoutedEventArgs e)
    {
        DeleteCellsMenuItem_Click(sender, e);
    }
    private void FormatPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void InsertCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
            return;

        CommandOutcome outcome;
        var success = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftDown => TryExecuteRepeatableCurrentRangeCommand(
                "Insert Cells",
                range,
                currentRange => new InsertCellsCommand(_currentSheetId, currentRange, InsertCellsShiftDirection.Down),
                out outcome),
            KeyboardInsertDeleteDialogChoice.EntireRow => TryExecuteRepeatableCurrentRangeCommand(
                "Insert Row",
                range,
                currentRange => new InsertRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.RowCount),
                out outcome),
            KeyboardInsertDeleteDialogChoice.EntireColumn => TryExecuteRepeatableCurrentRangeCommand(
                "Insert Column",
                range,
                currentRange => new InsertColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.ColCount),
                out outcome),
            _ => TryExecuteRepeatableCurrentRangeCommand(
                "Insert Cells",
                range,
                currentRange => new InsertCellsCommand(_currentSheetId, currentRange, InsertCellsShiftDirection.Right),
                out outcome)
        };
        if (!success) return;

        if (choice is KeyboardInsertDeleteDialogChoice.EntireRow or KeyboardInsertDeleteDialogChoice.EntireColumn)
            ClearFormulaTraceArrowsAfterStructuralEdit();
        ClearClipboardMarqueeAfterStructuralEdit();

        // R76-render-freeze-scroll-4-1: an EntireRow/EntireColumn insert renumbers every row/col
        // at or below it, so keep the same content on screen if the edit is at/above the view.
        if (choice == KeyboardInsertDeleteDialogChoice.EntireRow)
            ShiftScrollOriginForRowEdit(range.Start.Row, (int)range.RowCount);
        else if (choice == KeyboardInsertDeleteDialogChoice.EntireColumn)
            ShiftScrollOriginForColEdit(range.Start.Col, (int)range.ColCount);

        UpdateViewport();
    }

    /// <summary>
    /// R75-render-selection-marquee-4-1: Insert/Delete Rows/Columns/Cells shift the grid
    /// structurally, so an active Copy/Cut marching-ants marquee must be cancelled the same way an
    /// ordinary cell edit already cancels it (R54, TryExecuteEditCells in
    /// MainWindow.CommandExecution.cs). Without this, a subsequent Paste silently uses the STALE
    /// pre-shift clip.SourceRange, moving/copying the wrong cells.
    /// </summary>
    private void ClearClipboardMarqueeAfterStructuralEdit()
    {
        if (_internalClipboard is not null || SheetGrid.ClipboardRange is not null)
        {
            _internalClipboard = null;
            ClearClipboardVisualState();
        }
    }

    private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }
    private void DeleteCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (!TryShowCellShiftDialog(CellShiftDialogMode.Delete, out var choice))
            return;

        CommandOutcome outcome;
        var success = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftUp => TryExecuteRepeatableCurrentRangeCommand(
                "Delete Cells",
                range,
                currentRange => new DeleteCellsCommand(_currentSheetId, currentRange, DeleteCellsShiftDirection.Up),
                out outcome),
            KeyboardInsertDeleteDialogChoice.EntireRow => TryExecuteRepeatableCurrentRangeCommand(
                "Delete Row",
                range,
                currentRange => new DeleteRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.RowCount),
                out outcome),
            KeyboardInsertDeleteDialogChoice.EntireColumn => TryExecuteRepeatableCurrentRangeCommand(
                "Delete Column",
                range,
                currentRange => new DeleteColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.ColCount),
                out outcome),
            _ => TryExecuteRepeatableCurrentRangeCommand(
                "Delete Cells",
                range,
                currentRange => new DeleteCellsCommand(_currentSheetId, currentRange, DeleteCellsShiftDirection.Left),
                out outcome)
        };
        if (!success) return;

        if (choice is KeyboardInsertDeleteDialogChoice.EntireRow or KeyboardInsertDeleteDialogChoice.EntireColumn)
            ClearFormulaTraceArrowsAfterStructuralEdit();
        ClearClipboardMarqueeAfterStructuralEdit();

        // R76-render-freeze-scroll-4-1: an EntireRow/EntireColumn delete renumbers every row/col
        // at or below it, so keep the same content on screen if the edit is at/above the view.
        if (choice == KeyboardInsertDeleteDialogChoice.EntireRow)
            ShiftScrollOriginForRowEdit(range.Start.Row, -(int)range.RowCount);
        else if (choice == KeyboardInsertDeleteDialogChoice.EntireColumn)
            ShiftScrollOriginForColEdit(range.Start.Col, -(int)range.ColCount);

        UpdateViewport();
    }

    private void DeleteSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || _workbook.Sheets.Count <= 1)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_DeleteOnlyVisibleSheet"),
                UiText.Get("MainWindowMessage_DeleteSheetTitle"));
            return;
        }

        if (!_messageService.AskYesNo(
                UiText.Format("MainWindowMessage_DeleteSheetPrompt", sheet.Name),
                UiText.Get("MainWindowMessage_DeleteSheetTitle"))) return;
        if (!TryExecuteCommand(new RemoveSheetCommand(_currentSheetId), "Delete Sheet"))
            return;

        _worksheetSelections.Remove(_currentSheetId);
        _currentSheetId = _workbook.Sheets[0].Id;
        RecalculateWorkbook();
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void FormatRowHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new RowHeightDialog(RowColumnSizingPlanner.GetRowHeightDialogValue(sheet, range)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Row Height",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return RowColumnSizingPlanner.CreateRowHeightCommand(sheetId, currentRange, dialog.Result.Height);
                }))
            return;
        UpdateViewport();
    }

    private void FormatAutoRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Auto Row Height", sheetId => CreateAutoFitRowHeightCommand(sheetId, range)))
            return;
        UpdateViewport();
    }
    private void FormatColWidthMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new ColumnWidthDialog(RowColumnSizingPlanner.GetColumnWidthDialogValue(sheet, range)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Column Width",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return RowColumnSizingPlanner.CreateColumnWidthCommand(sheetId, currentRange, dialog.Result.Width);
                }))
            return;
        UpdateViewport();
    }

    private void FormatAutoColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Auto Column Width", sheetId => CreateAutoFitColumnWidthCommand(sheetId, range)))
            return;
        UpdateViewport();
    }

    private IWorkbookCommand CreateAutoFitRowHeightCommand(SheetId sheetId, GridRange range)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return new FailedWorkbookCommand(UiText.Get("MainWindowMessage_SheetNotFound"));

        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitCellText(sheet, row, col),
            sheet.DefaultRowHeight);

        return RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(sheetId, plans)
            ?? new CompositeWorkbookCommand("Auto Row Height", []);
    }

    private IWorkbookCommand CreateAutoFitColumnWidthCommand(SheetId sheetId, GridRange range)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return new FailedWorkbookCommand(UiText.Get("MainWindowMessage_SheetNotFound"));

        var plans = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitCellText(sheet, row, col),
            sheet.DefaultColumnWidth);

        return RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans)
            ?? new CompositeWorkbookCommand("Auto Column Width", []);
    }

    private AutoFitCellText? GetAutoFitCellText(Sheet sheet, uint row, uint col)
    {
        if (sheet.GetCell(row, col) is not { } cell)
            return null;

        var style = _workbook.GetStyle(cell.StyleId);
        return new AutoFitCellText(GetAutoFitDisplayText(sheet, cell), style.WrapText, TextRotation: style.TextRotation, FontSize: style.FontSize);
    }

    private string GetAutoFitDisplayText(Sheet sheet, Cell cell)
    {
        var style = _workbook.GetStyle(cell.StyleId);

        // AutoFit measures whatever text is CURRENTLY DISPLAYED, so with Show Formulas on it must
        // size to the formula text -- and Show Formulas is per-window (R89-show-formulas-per-window-1).
        // Read this window's own effective view state rather than the raw shared Sheet field: a
        // sibling "New Window" on the same sheet may have flipped the shared field without this
        // window ever adopting it, which would size this window's columns to the sibling's mode.
        // Mirrors GetEffectiveViewState use in MainWindow.FormulaCommands.cs and the shared tier's
        // WorkbookSession.GetAutoFitDisplayText, which already reads the per-view accessor.
        return GetEffectiveViewState(sheet).ShowFormulas && cell.FormulaText is not null
            ? "=" + cell.FormulaText
            : NumberFormatter.Format(cell.Value, style.NumberFormat, _workbook.Uses1904DateSystem);
    }

    /// <summary>
    /// Turning on Wrap Text auto-grows each affected row to fit the now-wrapped content, matching
    /// Excel's "row grows unless you've manually resized it" behavior (mirrors
    /// WorkbookSession.CreateWrapTextGrowthCommands in the Avalonia shell). Reuses the same
    /// content-based estimate (RowColumnSizingPlanner/AutoFitSizingService) as the explicit "AutoFit
    /// Row Height" command above, but since this runs before the WrapText style diff has been
    /// applied, the display-text lookup is overridden to report WrapText=true for cells in
    /// <paramref name="range"/>. Only ever grows a row: any row whose estimate doesn't exceed its
    /// current height (including a row a user previously resized taller by hand) is left untouched,
    /// matching Excel never shrinking a row just from toggling wrap on.
    /// </summary>
    private IReadOnlyList<IWorkbookCommand> CreateWrapTextGrowthCommands(SheetId sheetId, GridRange range)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return [];

        var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
        var plans = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            sheetRange,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitCellTextForPendingWrap(sheet, row, col, sheetRange),
            sheet.DefaultRowHeight);

        return plans
            .Where(plan => plan.Size > (sheet.RowHeights.TryGetValue(plan.Index, out var currentHeight) ? currentHeight : sheet.DefaultRowHeight))
            .Select(plan => (IWorkbookCommand)new SetRowHeightCommand(sheetId, plan.Index, plan.Index, plan.Size))
            .ToList();
    }

    private AutoFitCellText? GetAutoFitCellTextForPendingWrap(Sheet sheet, uint row, uint col, GridRange pendingWrapRange)
    {
        if (GetAutoFitCellText(sheet, row, col) is not { } cellText)
            return null;

        return pendingWrapRange.Contains(new CellAddress(sheet.Id, row, col))
            ? cellText with { WrapText = true }
            : cellText;
    }

    /// <summary>
    /// Applies a style diff that may enable Wrap Text, folding the Excel-matching row-height
    /// auto-grow (<see cref="CreateWrapTextGrowthCommands"/>) into the same undoable/repeatable
    /// operation as the style change itself -- unlike the generic ApplyStyleDiff (WorkbookUiState.cs),
    /// which has no notion of row growth. Used by the Wrap Text ribbon toggle and by the Format
    /// Cells dialog's simple (no border/merge ops) apply path.
    /// </summary>
    private void ApplyStyleDiffWithWrapGrowth(StyleDiff diff)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableApplyStyleWithWrapGrowth(diff, "Apply Style"))
            return;

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private bool TryExecuteRepeatableApplyStyleWithWrapGrowth(StyleDiff diff, string title)
    {
        IWorkbookCommand CreateCommand()
        {
            var fallbackRange = new GridRange(
                new CellAddress(_currentSheetId, 1, 1),
                new CellAddress(_currentSheetId, 1, 1));
            var ranges = GetCurrentSelectionRanges(fallbackRange);
            var groupedSheetIds = CurrentGroupedEditSheetIds();
            var commands = new List<IWorkbookCommand>
            {
                SelectionStyleCommandPlanner.CreateApplyStyleCommand(groupedSheetIds, ranges, diff, title)
            };

            if (diff.WrapText == true)
            {
                foreach (var sheetId in groupedSheetIds)
                foreach (var range in ranges)
                    commands.AddRange(CreateWrapTextGrowthCommands(sheetId, range));
            }

            return commands.Count == 1 ? commands[0] : new CompositeWorkbookCommand(title, commands);
        }

        return TryExecuteRepeatableCommand(CreateCommand, title, out _);
    }

    private void FormatDefaultWidthMenuItem_Click(object sender, RoutedEventArgs e) { FormatColWidthMenuItem_Click(sender, e); }
    private void FormatHideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteRowsHidden(hidden: true);
    }

    private void FormatUnhideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteRowsHidden(hidden: false);
    }

    private void FormatHideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteColumnsHidden(hidden: true);
    }

    private void FormatUnhideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteColumnsHidden(hidden: false);
    }
    private void FormatProtectSheetMenuItem_Click(object sender, RoutedEventArgs e) { ProtectSheetBtn_Click(sender, e); }
    private void FormatRenameSheetMenuItem_Click(object sender, RoutedEventArgs e) => RenameCurrentSheet();
    private void FormatTabColorMenuItem_Click(object sender, RoutedEventArgs e) => ColorCurrentSheetTab();
    private void FormatHideSheetMenuItem_Click(object sender, RoutedEventArgs e) => HideCurrentSheet();
    private void FormatUnhideSheetMenuItem_Click(object sender, RoutedEventArgs e) => UnhideSheet();
    private void FormatLockCellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var style = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(Locked: !style.Locked));
    }

    private void FormatCellsMenuItem_Click(object sender, RoutedEventArgs e) => OpenFormatCellsDialog();

    private void InsertRowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        InsertRows(range.Start.Row);
    }

    private void DeleteRowBtn_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void InsertColBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        InsertColumns(range.Start.Col);
    }

    private void DeleteColBtn_Click(object sender, RoutedEventArgs e) => DeleteSelectedColumns();

    private void InsertRows(uint beforeRow)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Row", sheetId => new InsertRowsCommand(sheetId, beforeRow)))
            return;

        ClearFormulaTraceArrowsAfterStructuralEdit();
        ClearClipboardMarqueeAfterStructuralEdit();
        ShiftScrollOriginForRowEdit(beforeRow, 1);
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void InsertColumns(uint beforeCol)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Column", sheetId => new InsertColumnsCommand(sheetId, beforeCol)))
            return;

        ClearFormulaTraceArrowsAfterStructuralEdit();
        ClearClipboardMarqueeAfterStructuralEdit();
        ShiftScrollOriginForColEdit(beforeCol, 1);
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var startRow = range.Start.Row;
        var rowCount = range.End.Row - range.Start.Row + 1;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Row - currentRange.Start.Row + 1;
                    return new DeleteRowsCommand(sheetId, currentRange.Start.Row, count);
                }))
            return;

        ClearFormulaTraceArrowsAfterStructuralEdit();
        ClearClipboardMarqueeAfterStructuralEdit();
        ShiftScrollOriginForRowEdit(startRow, -(int)rowCount);
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var startCol = range.Start.Col;
        var colCount = range.End.Col - range.Start.Col + 1;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Col - currentRange.Start.Col + 1;
                    return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, count);
                }))
            return;

        ClearFormulaTraceArrowsAfterStructuralEdit();
        ClearClipboardMarqueeAfterStructuralEdit();
        ShiftScrollOriginForColEdit(startCol, -(int)colCount);
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ApplyNumberFormatShortcut(NumberFormatShortcut shortcut) =>
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatShortcutService.GetFormat(shortcut)));

    private void CopyFromAbove(CopyFromAboveMode mode)
    {
        if (SheetGrid.SelectedRange?.Start is not { } target)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null ||
            CopyFromAbovePlanner.CreateEdit(sheet, target, mode) is not { } edit)
            return;

        if (!TryExecuteEditCells([edit], mode == CopyFromAboveMode.Value ? "Copy Value from Above" : "Copy Formula from Above"))
            return;

        FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(target), target);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ApplyFontToggleShortcut(FontToggleShortcut shortcut)
    {
        // Read/write the neutral RibbonStateStore (keyed by CommandName) — the same source of truth
        // the ribbon-click handlers use (BoldButton_Click reads IsRibbonCommandChecked("Bold")), so
        // the keyboard toggle stays consistent with the rendered ribbon and the current selection.
        var commandName = shortcut switch
        {
            FontToggleShortcut.Bold => "Bold",
            FontToggleShortcut.Italic => "Italic",
            FontToggleShortcut.Strikethrough => "Strikethrough",
            _ => "Underline"
        };
        var enabled = !IsRibbonCommandChecked(commandName);
        if (shortcut == FontToggleShortcut.Underline)
        {
            SetToolbarToggleStates(underline: enabled, strike: enabled ? false : null);
            ApplyStyleDiff(CellStyleDiffPlanner.UnderlineDiff(enabled));
            return;
        }

        if (shortcut == FontToggleShortcut.Strikethrough)
        {
            SetToolbarToggleStates(strike: enabled, underline: enabled ? false : null);
            ApplyStyleDiff(CellStyleDiffPlanner.StrikethroughDiff(enabled));
            return;
        }

        _ribbonState.SetChecked(commandName, enabled);
        ApplyStyleDiff(FontToggleShortcutService.CreateDiff(shortcut, enabled));
    }

    private void ApplyOutlineBorderShortcut()
    {
        ApplyRangeBorderPreset(BorderShortcutService.GetOutlineBorderDiff, "Outline Border");
    }

    private void ExecuteKeyboardInsert()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var plan = KeyboardInsertDeletePlanner.PlanInsert(range);
        if (plan == KeyboardInsertDeletePlan.Rows)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Insert Row",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new InsertRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                    }))
                return;

            ClearFormulaTraceArrowsAfterStructuralEdit();
        }
        else if (plan == KeyboardInsertDeletePlan.Columns)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Insert Column",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new InsertColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                    }))
                return;

            ClearFormulaTraceArrowsAfterStructuralEdit();
        }
        else if (!ExecuteKeyboardInsertCellsWithPrompt(range))
        {
            return;
        }

        ClearClipboardMarqueeAfterStructuralEdit();
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ExecuteKeyboardDelete()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var plan = KeyboardInsertDeletePlanner.PlanDelete(range);
        if (plan == KeyboardInsertDeletePlan.Rows)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Delete Row",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new DeleteRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                    }))
                return;

            ClearFormulaTraceArrowsAfterStructuralEdit();
        }
        else if (plan == KeyboardInsertDeletePlan.Columns)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Delete Column",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                    }))
                return;

            ClearFormulaTraceArrowsAfterStructuralEdit();
        }
        else if (!ExecuteKeyboardDeleteCellsWithPrompt(range))
        {
            return;
        }

        ClearClipboardMarqueeAfterStructuralEdit();
        RecalculateWorkbook();
        UpdateViewport();
    }

    private bool ExecuteKeyboardInsertCellsWithPrompt(GridRange range)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
            return false;

        var success = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftDown => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Cells",
                sheetId => new InsertCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    InsertCellsShiftDirection.Down)),
            KeyboardInsertDeleteDialogChoice.EntireRow => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new InsertRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                }),
            KeyboardInsertDeleteDialogChoice.EntireColumn => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new InsertColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                }),
            _ => TryExecuteRepeatableGroupedSheetCommand(
                "Insert Cells",
                sheetId => new InsertCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    InsertCellsShiftDirection.Right))
        };

        if (success && choice is KeyboardInsertDeleteDialogChoice.EntireRow or KeyboardInsertDeleteDialogChoice.EntireColumn)
            ClearFormulaTraceArrowsAfterStructuralEdit();

        return success;
    }

    private bool ExecuteKeyboardDeleteCellsWithPrompt(GridRange range)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Delete, out var choice))
            return false;

        var success = choice switch
        {
            KeyboardInsertDeleteDialogChoice.ShiftUp => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Cells",
                sheetId => new DeleteCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    DeleteCellsShiftDirection.Up)),
            KeyboardInsertDeleteDialogChoice.EntireRow => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new DeleteRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                }),
            KeyboardInsertDeleteDialogChoice.EntireColumn => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                }),
            _ => TryExecuteRepeatableGroupedSheetCommand(
                "Delete Cells",
                sheetId => new DeleteCellsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                    DeleteCellsShiftDirection.Left))
        };

        if (success && choice is KeyboardInsertDeleteDialogChoice.EntireRow or KeyboardInsertDeleteDialogChoice.EntireColumn)
            ClearFormulaTraceArrowsAfterStructuralEdit();

        return success;
    }

    private bool TryShowCellShiftDialog(CellShiftDialogMode mode, out KeyboardInsertDeleteDialogChoice choice)
    {
        var dialog = new CellShiftDialog(mode) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            choice = default;
            return false;
        }

        choice = CellShiftDialog.ToKeyboardChoice(mode, dialog.SelectedChoice);
        return true;
    }

    private void ExecuteRowsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                hidden ? "Hide Row" : "Unhide Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return RowColumnSizingPlanner.CreateRowsHiddenCommand(sheetId, currentRange, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void ExecuteColumnsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                hidden ? "Hide Column" : "Unhide Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return RowColumnSizingPlanner.CreateColumnsHiddenCommand(sheetId, currentRange, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void OpenFormatCellsDialog(FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var selectedCell = sheet.GetCell(range.Start);
        var currentStyle = _workbook.GetStyle(selectedCell?.StyleId ?? StyleId.Default);
        var mergeCells = CellMergePlanner.IsSelectionMerged(sheet, range);
        var numberPreviewText = selectedCell is null
            ? null
            : GetAutoFitDisplayText(sheet, selectedCell);
        var dlg = new FormatCellsDialog(currentStyle, initialTab, mergeCells, numberPreviewText) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultDiff is null) return;
        var mergeContentResolution = MergeCellContentResolution.KeepFirstCell;
        if (dlg.ResultMergeCells == true && !TryResolveMergeContentResolution(range, out mergeContentResolution))
            return;

        ApplyFormatCellsDialogResult(
            range,
            dlg.ResultDiff,
            dlg.ResultBorderSelection,
            dlg.ResultMergeCells,
            mergeContentResolution);
    }

    private void ApplyFormatCellsDialogResult(
        GridRange range,
        StyleDiff diff,
        FormatCellsBorderSelection borderSelection,
        bool? mergeCells,
        MergeCellContentResolution mergeContentResolution = MergeCellContentResolution.KeepFirstCell)
    {
        if (!borderSelection.HasRangeOperations && mergeCells is null)
        {
            ApplyStyleDiffWithWrapGrowth(diff);
            return;
        }

        var nonBorderDiff = diff with
        {
            BorderTop = null,
            BorderRight = null,
            BorderBottom = null,
            BorderLeft = null
        };

        IWorkbookCommand CreateRangeCommand(SheetId sheetId, GridRange sheetRange)
        {
            var sheet = _workbook.GetSheet(sheetId);
            var commands = new List<IWorkbookCommand>
            {
                new ApplyStyleCommand(
                    sheetId,
                    sheetRange,
                    borderSelection.HasRangeOperations ? nonBorderDiff : diff)
            };

            if (diff.WrapText == true)
            {
                commands.AddRange(CreateWrapTextGrowthCommands(sheetId, sheetRange));
            }

            if (borderSelection.Clear)
            {
                commands.Add(new ApplyStyleCommand(sheetId, sheetRange, BorderShortcutService.GetClearBorderDiff()));
            }

            if (borderSelection.Outline is { } outline)
            {
                commands.AddRange(CreateBorderCommands(
                    sheetId,
                    sheetRange,
                    sheet,
                    (currentRange, address) => BorderShortcutService.GetOutlineBorderDiff(
                        currentRange,
                        address,
                        outline.Style,
                        outline.Color)));
            }

            if (borderSelection.Inside is { } inside)
            {
                commands.AddRange(CreateBorderCommands(
                    sheetId,
                    sheetRange,
                    sheet,
                    (currentRange, address) => BorderShortcutService.GetInsideBorderDiff(
                        currentRange,
                        address,
                        inside.Style,
                        inside.Color)));
            }

            if (mergeCells is { } shouldMerge && sheet is not null)
            {
                commands.AddRange(CellMergePlanner.CreateFormatCellsMergeCommands(
                    sheet,
                    sheetId,
                    sheetRange,
                    shouldMerge,
                    mergeContentResolution));
            }

            return commands.Count == 1
                ? commands[0]
                : new CompositeWorkbookCommand("Format Cells", commands);
        }

        if (!TryExecuteRepeatableCurrentSelectionRangesCommand("Format Cells", range, CreateRangeCommand))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private static IReadOnlyList<IWorkbookCommand> CreateBorderCommands(
        SheetId sheetId,
        GridRange range,
        Sheet? sheet,
        Func<GridRange, CellAddress, StyleDiff> createDiff)
    {
        // Clamp the dense cell iteration to the used-range zone.  For a whole-column or
        // whole-row border selection this prevents creating millions of single-cell commands.
        // The createDiff function still receives the original (full) range so that edge/interior
        // decisions (outline vs. inside borders) remain correct.
        var iterRange = sheet is not null
            ? ApplyStyleCommand.StyleOnlyCreateZone(sheet, range) ?? range
            : range;

        return iterRange
            .AllCells()
            .Select(address => (Address: address, Diff: createDiff(range, address)))
            .Where(plan => BorderShortcutService.HasBorderChanges(plan.Diff))
            .Select(plan => (IWorkbookCommand)new ApplyStyleCommand(
                sheetId,
                new GridRange(plan.Address, plan.Address),
                plan.Diff))
            .ToList();
    }

    private void OnAutofillRequested(GridRange sourceRange, GridRange fillRange) =>
        ExecuteAutofill(sourceRange, fillRange, _autofillCtrlHeld);

    /// <summary>
    /// Executes an autofill for the given source/fill ranges with an explicit Ctrl-flip state.
    /// Shared by the dragged fill-handle path (<see cref="OnAutofillRequested"/>, which uses the
    /// live Ctrl state captured via <c>AutofillModifiersResolved</c>) and the double-click fill
    /// path (<see cref="OnAutofillHandleDoubleClicked"/>, which never has a paired
    /// <c>AutofillModifiersResolved</c> event and so must not read the possibly-stale
    /// <see cref="_autofillCtrlHeld"/> field).
    /// </summary>
    private void ExecuteAutofill(GridRange sourceRange, GridRange fillRange, bool ctrlHeld)
    {
        var cmd = new AutofillCommand(_currentSheetId, sourceRange, fillRange, ctrlHeld);
        if (!TryExecuteCommand(cmd, "Autofill", out var outcome))
            return;

        SelectCompletedAutofillRange(sourceRange, fillRange);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void SelectCompletedAutofillRange(GridRange sourceRange, GridRange fillRange)
    {
        var selectionRange = FreeX.App.Presentation.GridInteraction.GridAutofillPlanner.CalculateCompletedSelectionRange(sourceRange, fillRange);
        _selectionAnchor = selectionRange.Start;
        _selectionCursor = selectionRange.End;
        if (_workbook.GetSheet(_currentSheetId) is { } sheet)
        {
            sheet.ActiveRow = selectionRange.Start.Row;
            sheet.ActiveCol = selectionRange.Start.Col;
        }

        SetSelectedRangesIfChanged(null);
        SheetGrid.SelectedRange = selectionRange;
        SetCellAddressBoxSelectionText(FormatNameBoxSelectionText(selectionRange));
        RefreshToolbarAfterSelectionChange();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        UpdateCommentPreview(selectionRange.Start);
    }

    private void OnSelectionMoveRequested(GridRange sourceRange, GridRange targetRange)
    {
        if (sourceRange.Start.Sheet != _currentSheetId ||
            targetRange.Start.Sheet != _currentSheetId)
        {
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        if (SelectionMoveOverwritePlanner.HasOverwriteTargets(sheet, sourceRange, targetRange) &&
            !_messageService.AskYesNo(UiText.Get("MainWindowMessage_TextToColumnsReplaceDataPrompt")))
        {
            return;
        }

        var isCtrlCopy = _selectionMoveCtrlHeld;
        IWorkbookCommand command = isCtrlCopy
            ? new CopyRangeCommand(_currentSheetId, sourceRange, targetRange.Start)
            : new MoveRangeCommand(_currentSheetId, sourceRange, targetRange.Start);
        if (!TryExecuteCommand(command, isCtrlCopy ? "Copy Cells" : "Move Cells", out var outcome))
            return;

        ClearClipboardVisualState();
        SetSelectedRangesIfChanged(null);
        _selectionAnchor = targetRange.Start;
        _selectionCursor = targetRange.End;
        SheetGrid.SelectedRange = targetRange;

        sheet.ActiveRow = targetRange.Start.Row;
        sheet.ActiveCol = targetRange.Start.Col;

        SetCellAddressBoxSelectionText(targetRange.Start == targetRange.End
            ? FormatCellReference(targetRange.Start)
            : FormatRangeReference(targetRange.Start, targetRange.End));
        SetFormulaBarSelectionText(FormatFormulaBarText(sheet.GetCell(targetRange.Start), targetRange.Start));

        UpdateViewport();
        RefreshToolbarAfterSelectionChange();
        RefreshStatusBar();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        UpdateCommentPreview(targetRange.Start);
        FocusSheetGridIfNeeded();
    }
}
