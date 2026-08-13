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

        if (!TryExecuteWorksheetStructure(
                () => choice switch
                {
                    KeyboardInsertDeleteDialogChoice.ShiftDown =>
                        _session.InsertSelectedCells(InsertCellsShiftDirection.Down),
                    KeyboardInsertDeleteDialogChoice.EntireRow => _session.InsertSelectedRows(),
                    KeyboardInsertDeleteDialogChoice.EntireColumn => _session.InsertSelectedColumns(),
                    _ => _session.InsertSelectedCells(InsertCellsShiftDirection.Right)
                },
                out var result))
            return;

        CompleteWorksheetStructureEdit(result);
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
        if (_workbookClipboardSession.HasContent || SheetGrid.ClipboardRange is not null)
        {
            _workbookClipboardSession.Clear();
            ClearClipboardVisualState();
        }
    }

    private void CompleteWorksheetStructureEdit(
        WorkbookWorksheetStructureResult result,
        bool recalculateWorkbook = false)
    {
        if (!result.IsNoOp)
        {
            if (result.InvalidatesFormulaTraceArrows)
                ClearFormulaTraceArrowsAfterStructuralEdit();
            ClearClipboardMarqueeAfterStructuralEdit();

            if (result.ViewportRowDelta != 0)
                ShiftScrollOriginForRowEdit(result.TargetRange.Start.Row, result.ViewportRowDelta);
            if (result.ViewportColumnDelta != 0)
                ShiftScrollOriginForColEdit(result.TargetRange.Start.Col, result.ViewportColumnDelta);
        }

        if (recalculateWorkbook)
            RecalculateWorkbook();
        UpdateViewport();
    }

    private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }
    private void DeleteCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (!TryShowCellShiftDialog(CellShiftDialogMode.Delete, out var choice))
            return;

        if (!TryExecuteWorksheetStructure(
                () => choice switch
                {
                    KeyboardInsertDeleteDialogChoice.ShiftUp =>
                        _session.DeleteSelectedCells(DeleteCellsShiftDirection.Up),
                    KeyboardInsertDeleteDialogChoice.EntireRow => _session.DeleteSelectedRows(),
                    KeyboardInsertDeleteDialogChoice.EntireColumn => _session.DeleteSelectedColumns(),
                    _ => _session.DeleteSelectedCells(DeleteCellsShiftDirection.Left)
                },
                out var result))
            return;

        CompleteWorksheetStructureEdit(result);
    }

    private void DeleteSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var deletedSheetId = _currentSheetId;
        var sheet = _workbook.GetSheet(deletedSheetId);
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
        if (!TryExecuteCommand(new RemoveSheetCommand(deletedSheetId), "Delete Sheet"))
            return;

        // R126-viewstate-delete-purge-1: drop this window's own remembered view state/split
        // offsets for the deleted sheet id too -- otherwise WorksheetViewStateStore and
        // _splitPaneViewportOffsets each keep one stale entry per deleted sheet for the rest of
        // this window's lifetime (only a full New/Open Clear() ever drops them).
        // TryExecuteCommand synchronizes the session's new active sheet back into _currentSheetId,
        // so retain the deleted id captured before execution for renderer-cache cleanup.
        _worksheetSelections.Remove(deletedSheetId);
        _worksheetViewStates.Remove(deletedSheetId);
        _splitPaneViewportOffsets.Remove(deletedSheetId);
        _currentSheetId = _workbook.Sheets[0].Id;
        RecalculateWorkbook();
        RefreshSheetTabs();
        UpdateViewport();
    }

    /// <summary>
    /// R124-cellscmds-multiarea-rowheight-1: mirrors R123-cellscmds-multiarea-insert-1/-delete-1 for
    /// Row Height. With rows 2 and 5 Ctrl+click selected via AddAdditionalRowSelection,
    /// SheetGrid.SelectedRanges holds both disjoint whole-row areas while SheetGrid.SelectedRange is
    /// only the last-clicked (active) one -- reading only SelectedRange (as
    /// TryExecuteRepeatableGroupedSheetCommand did) silently dropped every area but the active one
    /// from the resize, unlike real Excel, which resizes every disjoint area of a multi-area
    /// selection. WorkbookSession expands the edit across every selected area and grouped sheet.
    /// </summary>
    private void FormatRowHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        SynchronizeWorkbookSessionSelection();
        var dialog = new RowHeightDialog(_session.GetSelectedRowHeight()) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetSelectedRowsHeight(dialog.Result.Height),
                "Row Height"))
            return;
        UpdateViewport();
    }

    /// <summary>See FormatRowHeightMenuItem_Click above (R124-cellscmds-multiarea-rowheight-1); AutoFit
    /// Row Height has never been repeatable (F4), so this keeps that but adds multi-area/grouped-sheet
    /// awareness through WorkbookSession.</summary>
    private void FormatAutoRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(_session.AutoFitSelectedRowHeight, "Auto Row Height"))
            return;
        UpdateViewport();
    }

    /// <summary>Column counterpart of FormatRowHeightMenuItem_Click above (R124-cellscmds-multiarea-rowheight-1).</summary>
    private void FormatColWidthMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        SynchronizeWorkbookSessionSelection();
        var dialog = new ColumnWidthDialog(_session.GetSelectedColumnWidth()) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetSelectedColumnsWidth(dialog.Result.Width),
                "Column Width"))
            return;
        UpdateViewport();
    }

    /// <summary>Column counterpart of FormatAutoRowMenuItem_Click above (R124-cellscmds-multiarea-rowheight-1).</summary>
    private void FormatAutoColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(_session.AutoFitSelectedColumnWidth, "Auto Column Width"))
            return;
        UpdateViewport();
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
    /// <summary>
    /// R128-cellscmds-formatcells-activecell-1 sibling pickup: the same top-left-corner-vs-
    /// active-cell bug fixed for <see cref="OpenFormatCellsDialog"/> above also affected this
    /// toggle -- it read the Locked state to flip from <c>range.Start</c> instead of the true
    /// active cell, so a backward-extended selection (e.g. click C5, Shift+click A1) toggled
    /// Locked based on A1's state while the user was looking at C5.
    /// </summary>
    private void FormatLockCellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var style = _workbook.GetStyle(sheet.GetCell(ResolveFormatCellsSeedCell(range))?.StyleId ?? StyleId.Default);
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

    /// <summary>
    /// R123-cellscmds-multiarea-insert-1: mirrors R123-cellscmds-multiarea-delete-1's fix for the
    /// Insert side. InsertRowBtn_Click, the worksheet right-click "Insert Row Above/Below" items
    /// (MainWindow.WorksheetContextMenu.cs), and the keyboard Ctrl+Plus path (ExecuteKeyboardInsert)
    /// all funnel through InsertRows/InsertColumns below, so fixing them here fixes every caller in
    /// one choke point (no per-call-site duplication to forget). With rows 2 and 5 Ctrl+click
    /// selected via AddAdditionalRowSelection, SheetGrid.SelectedRanges holds both disjoint whole-row
    /// areas while beforeRow (derived from the single ACTIVE area) only ever names one of them --
    /// Insert Row used to silently insert a single blank row at the active area alone, unlike real
    /// Excel, which inserts one new row at every disjoint area of a multi-area selection.
    /// </summary>
    private void InsertRows(uint beforeRow)
    {
        Func<WorkbookWorksheetStructureResult> execute =
            SheetGrid.SelectedRanges is { Count: > 1 } ranges &&
            ranges.All(SelectionRangeService.IsWholeRowSelection)
            ? _session.InsertSelectedRows
            : () => _session.InsertRows(beforeRow);
        if (!TryExecuteWorksheetStructure(execute, out var result))
            return;

        CompleteWorksheetStructureEdit(result, recalculateWorkbook: true);
    }

    /// <summary>Column counterpart of InsertRows above (R123-cellscmds-multiarea-insert-1).</summary>
    private void InsertColumns(uint beforeCol)
    {
        Func<WorkbookWorksheetStructureResult> execute =
            SheetGrid.SelectedRanges is { Count: > 1 } ranges &&
            ranges.All(SelectionRangeService.IsWholeColumnSelection)
            ? _session.InsertSelectedColumns
            : () => _session.InsertColumns(beforeCol);
        if (!TryExecuteWorksheetStructure(execute, out var result))
            return;

        CompleteWorksheetStructureEdit(result, recalculateWorkbook: true);
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is null ||
            !TryExecuteWorksheetStructure(_session.DeleteSelectedRows, out var result))
            return;

        CompleteWorksheetStructureEdit(result, recalculateWorkbook: true);
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is null ||
            !TryExecuteWorksheetStructure(_session.DeleteSelectedColumns, out var result))
            return;

        CompleteWorksheetStructureEdit(result, recalculateWorkbook: true);
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

        WorkbookWorksheetStructureResult result;
        var plan = KeyboardInsertDeletePlanner.PlanInsert(range);
        var success = plan switch
        {
            KeyboardInsertDeletePlan.Rows =>
                TryExecuteWorksheetStructure(_session.InsertSelectedRows, out result),
            KeyboardInsertDeletePlan.Columns =>
                TryExecuteWorksheetStructure(_session.InsertSelectedColumns, out result),
            _ => ExecuteKeyboardInsertCellsWithPrompt(out result)
        };
        if (!success)
            return;

        CompleteWorksheetStructureEdit(result, recalculateWorkbook: true);
    }

    private void ExecuteKeyboardDelete()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        WorkbookWorksheetStructureResult result;
        var plan = KeyboardInsertDeletePlanner.PlanDelete(range);
        var success = plan switch
        {
            KeyboardInsertDeletePlan.Rows =>
                TryExecuteWorksheetStructure(_session.DeleteSelectedRows, out result),
            KeyboardInsertDeletePlan.Columns =>
                TryExecuteWorksheetStructure(_session.DeleteSelectedColumns, out result),
            _ => ExecuteKeyboardDeleteCellsWithPrompt(out result)
        };
        if (!success)
            return;

        CompleteWorksheetStructureEdit(result, recalculateWorkbook: true);
    }

    private bool ExecuteKeyboardInsertCellsWithPrompt(out WorkbookWorksheetStructureResult result)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Insert, out var choice))
        {
            result = null!;
            return false;
        }

        return TryExecuteWorksheetStructure(
            () => choice switch
            {
                KeyboardInsertDeleteDialogChoice.ShiftDown =>
                    _session.InsertSelectedCells(InsertCellsShiftDirection.Down),
                KeyboardInsertDeleteDialogChoice.EntireRow => _session.InsertSelectedRows(),
                KeyboardInsertDeleteDialogChoice.EntireColumn => _session.InsertSelectedColumns(),
                _ => _session.InsertSelectedCells(InsertCellsShiftDirection.Right)
            },
            out result);
    }

    private bool ExecuteKeyboardDeleteCellsWithPrompt(out WorkbookWorksheetStructureResult result)
    {
        if (!TryShowCellShiftDialog(CellShiftDialogMode.Delete, out var choice))
        {
            result = null!;
            return false;
        }

        return TryExecuteWorksheetStructure(
            () => choice switch
            {
                KeyboardInsertDeleteDialogChoice.ShiftUp =>
                    _session.DeleteSelectedCells(DeleteCellsShiftDirection.Up),
                KeyboardInsertDeleteDialogChoice.EntireRow => _session.DeleteSelectedRows(),
                KeyboardInsertDeleteDialogChoice.EntireColumn => _session.DeleteSelectedColumns(),
                _ => _session.DeleteSelectedCells(DeleteCellsShiftDirection.Left)
            },
            out result);
    }

    private bool TryShowCellShiftDialog(CellShiftDialogMode mode, out KeyboardInsertDeleteDialogChoice choice)
    {
        var dialog = new CellShiftDialog(mode) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            choice = default;
            return false;
        }

        choice = CellShiftDialogPlanner.ToKeyboardChoice(mode, dialog.SelectedChoice);
        return true;
    }

    /// <summary>See FormatRowHeightMenuItem_Click (R124-cellscmds-multiarea-rowheight-1): Hide/Unhide
    /// Rows -- reached from the ribbon, the row-header right-click menu (MainWindow.WorksheetContextMenu.cs)
    /// and the Ctrl+9/Ctrl+Shift+9 keyboard shortcuts (MainWindow.Selection.cs) -- used to read only the
    /// active SheetGrid.SelectedRange, so Ctrl+click-selecting rows 2 and 5 then Hide Rows silently left
    /// row 2 visible. Now routes through the multi-area-aware plumbing.</summary>
    private void ExecuteRowsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetSelectedRowsHidden(hidden),
                hidden ? "Hide Row" : "Unhide Row"))
            return;

        UpdateViewport();
    }

    /// <summary>Column counterpart of ExecuteRowsHidden above (R124-cellscmds-multiarea-rowheight-1).</summary>
    private void ExecuteColumnsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteWorksheetLayout(
                () => _session.SetSelectedColumnsHidden(hidden),
                hidden ? "Hide Column" : "Unhide Column"))
            return;

        UpdateViewport();
    }

    /// <summary>
    /// The cell whose current formatting should seed/drive a per-selection format read -- used by
    /// the Format Cells dialog (Ctrl+1, Ctrl+Shift+F, the Font/Number/Alignment/Border
    /// dialog-launcher arrows, and 'More Number Formats…'/'More Borders…') and by the
    /// Format &gt; Lock Cell toggle. R128-cellscmds-formatcells-activecell-1: this must be the
    /// TRUE active/anchor cell (<see cref="FreeX.App.UI.GridView.ActiveCell"/>), not
    /// <paramref name="range"/>'s normalized top-left <c>Start</c> -- those differ whenever the
    /// selection was extended upward or leftward (e.g. click C5, then Shift+click A1, which keeps
    /// the active cell at C5 but normalizes Start to A1). Excel always reflects/toggles from the
    /// active cell of the selection, matching the same ActiveCell-over-Start correction already
    /// applied to the Home-tab ribbon toggles (R91-app-ribbon-state-5-1,
    /// MainWindow.WorkbookUiState.cs) and to Ctrl+Enter/hyperlink-open
    /// (R112-model-active-cell-vs-selection-1-1).
    /// </summary>
    private CellAddress ResolveFormatCellsSeedCell(GridRange range) => SheetGrid.ActiveCell ?? range.Start;

    private void OpenFormatCellsDialog(FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var selectedCell = sheet.GetCell(ResolveFormatCellsSeedCell(range));
        // mergeCells stays range-based (CellMergePlanner.IsSelectionMerged) -- that mirrors the
        // Avalonia shell's equivalent dialog opener, which likewise seeds style fields from the
        // active cell (_session.CreateFormatDiffFromActiveCell) but the merge checkbox from the
        // whole selection (_session.IsSelectedRangeMerged), since "merge cells" is a
        // whole-selection operation, not a per-cell style.
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
        FormatCellsDialogBorderSelection borderSelection,
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
