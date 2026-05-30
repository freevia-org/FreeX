using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Cells group (pickers) ────────────────────────────────────────────────

    private void InsertPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void DeletePickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
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

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
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

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void DeleteSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || _workbook.Sheets.Count <= 1) { _messageService.ShowWarning("Cannot delete the only sheet.", "Delete Sheet"); return; }
        if (!_messageService.AskYesNo($"Delete '{sheet.Name}'?", "Delete Sheet")) return;
        var outcome = _commandBus.Execute(_workbook.Id, new RemoveSheetCommand(_currentSheetId));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Delete Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[0].Id;
        RecalculateWorkbook();
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void FormatRowHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var dialog = new RowHeightDialog(RowColumnDimensionPlanner.GetRowHeightDialogValue(sheet, range)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Row Height",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return RowColumnDimensionPlanner.CreateRowHeightCommand(sheetId, currentRange, dialog.Result.Height);
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
        var dialog = new ColumnWidthDialog(RowColumnDimensionPlanner.GetColumnWidthDialogValue(sheet, range)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Column Width",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    return RowColumnDimensionPlanner.CreateColumnWidthCommand(sheetId, currentRange, dialog.Result.Width);
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

        var plans = AutoFitPlanner.PlanRowHeights(
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitDisplayText(sheet, row, col),
            sheet.DefaultRowHeight);

        return RowColumnDimensionPlanner.CreateAutoFitRowHeightCommand(sheetId, plans);
    }

    private IWorkbookCommand CreateAutoFitColumnWidthCommand(SheetId sheetId, GridRange range)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return new FailedWorkbookCommand(UiText.Get("MainWindowMessage_SheetNotFound"));

        var plans = AutoFitPlanner.PlanColumnWidths(
            range,
            sheet.GetUsedRange(),
            (row, col) => GetAutoFitDisplayText(sheet, row, col),
            sheet.DefaultColumnWidth);

        return RowColumnDimensionPlanner.CreateAutoFitColumnWidthCommand(sheetId, plans);
    }

    private string? GetAutoFitDisplayText(Sheet sheet, uint row, uint col)
    {
        return sheet.GetCell(row, col) is { } cell
            ? GetAutoFitDisplayText(sheet, cell)
            : null;
    }

    private string GetAutoFitDisplayText(Sheet sheet, Cell cell)
    {
        var style = _workbook.GetStyle(cell.StyleId);
        return sheet.ShowFormulas && cell.FormulaText is not null
            ? "=" + cell.FormulaText
            : NumberFormatter.Format(cell.Value, style.NumberFormat);
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

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void InsertColumns(uint beforeCol)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Column", sheetId => new InsertColumnsCommand(sheetId, beforeCol)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Row - currentRange.Start.Row + 1;
                    return new DeleteRowsCommand(sheetId, currentRange.Start.Row, count);
                }))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Col - currentRange.Start.Col + 1;
                    return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, count);
                }))
            return;

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

        RecalculateIfAutomatic([target]);
        FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(target), target);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ApplyFontToggleShortcut(FontToggleShortcut shortcut, ToggleButton button)
    {
        var enabled = !(button.IsChecked == true);
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

        button.IsChecked = enabled;
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
        }
        else if (!ExecuteKeyboardInsertCellsWithPrompt(range))
        {
            return;
        }

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
        }
        else if (!ExecuteKeyboardDeleteCellsWithPrompt(range))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private bool ExecuteKeyboardInsertCellsWithPrompt(GridRange range)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
            return false;

        return choice switch
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
    }

    private bool ExecuteKeyboardDeleteCellsWithPrompt(GridRange range)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Delete, out var choice))
            return false;

        return choice switch
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
                    return RowColumnDimensionPlanner.CreateRowsHiddenCommand(sheetId, currentRange, hidden);
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
                    return RowColumnDimensionPlanner.CreateColumnsHiddenCommand(sheetId, currentRange, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void OpenFormatCellsDialog(FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var currentStyle = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        var mergeCells = FormatCellsMergePlanner.IsSelectionMerged(sheet, range);
        var dlg = new FormatCellsDialog(currentStyle, initialTab, mergeCells) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultDiff is null) return;
        ApplyFormatCellsDialogResult(range, dlg.ResultDiff, dlg.ResultBorderSelection, dlg.ResultMergeCells);
    }

    private void ApplyFormatCellsDialogResult(
        GridRange range,
        StyleDiff diff,
        FormatCellsBorderSelection borderSelection,
        bool? mergeCells)
    {
        if (!borderSelection.HasRangeOperations && mergeCells is null)
        {
            ApplyStyleDiff(diff);
            return;
        }

        var nonBorderDiff = diff with
        {
            BorderTop = null,
            BorderRight = null,
            BorderBottom = null,
            BorderLeft = null
        };

        IWorkbookCommand CreateSheetCommand(SheetId sheetId)
        {
            var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
            var sheet = _workbook.GetSheet(sheetId);
            var commands = new List<IWorkbookCommand>
            {
                new ApplyStyleCommand(
                    sheetId,
                    sheetRange,
                    borderSelection.HasRangeOperations ? nonBorderDiff : diff)
            };

            if (borderSelection.Clear)
            {
                commands.Add(new ApplyStyleCommand(sheetId, sheetRange, BorderShortcutService.GetClearBorderDiff()));
            }

            if (borderSelection.Outline is { } outline)
            {
                commands.AddRange(CreateBorderCommands(
                    sheetId,
                    sheetRange,
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
                    (currentRange, address) => BorderShortcutService.GetInsideBorderDiff(
                        currentRange,
                        address,
                        inside.Style,
                        inside.Color)));
            }

            if (mergeCells is { } shouldMerge && sheet is not null)
                commands.AddRange(FormatCellsMergePlanner.CreateMergeCommands(sheet, sheetId, sheetRange, shouldMerge));

            return commands.Count == 1
                ? commands[0]
                : new CompositeWorkbookCommand("Format Cells", commands);
        }

        if (!TryExecuteRepeatableGroupedSheetCommand("Format Cells", CreateSheetCommand))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private static IReadOnlyList<IWorkbookCommand> CreateBorderCommands(
        SheetId sheetId,
        GridRange range,
        Func<GridRange, CellAddress, StyleDiff> createDiff)
    {
        return range
            .AllCells()
            .Select(address => (Address: address, Diff: createDiff(range, address)))
            .Where(plan => BorderShortcutService.HasBorderChanges(plan.Diff))
            .Select(plan => (IWorkbookCommand)new ApplyStyleCommand(
                sheetId,
                new GridRange(plan.Address, plan.Address),
                plan.Diff))
            .ToList();
    }

    private void OnAutofillRequested(GridRange sourceRange, GridRange fillRange)
    {
        var cmd = new AutofillCommand(_currentSheetId, sourceRange, fillRange);
        if (!TryExecuteCommand(cmd, "Autofill"))
            return;

        RecalculateIfAutomatic(fillRange.AllCells().ToList());
        UpdateViewport();
        RefreshStatusBar();
    }
}
