using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Ribbon.Avalonia;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle RowColumnDialogChromeStyle => new(FormulaBarFontFamily);

    // Hide / Unhide rows and columns (Excel parity: Ctrl+9 / Ctrl+Shift+9 / Ctrl+0 / Ctrl+Shift+0,
    // Home ▸ Cells ▸ Format ▸ Hide & Unhide, and the row/column header right-click menus). The
    // selection→span math and the undoable mutation are fully portable: span extraction lives in
    // FreeX.Core.Commands.SelectionRangeService and the edit runs through the shared
    // Set{Rows,Columns}HiddenCommand, executed via WorkbookSession.ExecuteReviewCommand (undo/redo).

    /// <summary>
    /// Matches the Windows host's hide/unhide grid shortcuts and runs the corresponding command:
    /// Ctrl+9 hides rows, Ctrl+Shift+9 unhides rows, Ctrl+0 hides columns, Ctrl+Shift+0 unhides
    /// columns. Uses the Control modifier specifically (not Meta) so Cmd+0 "Zoom 100%" is untouched.
    /// </summary>
    private bool TryHandleRowColumnVisibilityShortcut(KeyEventArgs e)
    {
        if (_formulaBox.IsFocused)
            return false;

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var otherModifiers = (e.KeyModifiers & ~(KeyModifiers.Control | KeyModifiers.Shift)) != 0;
        if (!control || otherModifiers)
            return false;

        if (e.Key is Key.D9 or Key.NumPad9)
        {
            if (shift)
                UnhideSelectedRows();
            else
                HideSelectedRows();
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.D0 or Key.NumPad0)
        {
            if (shift)
                UnhideSelectedColumns();
            else
                HideSelectedColumns();
            e.Handled = true;
            return true;
        }

        return false;
    }

    private void HideSelectedRows() => SetSelectedRowsHidden(hidden: true);

    private void UnhideSelectedRows() => SetSelectedRowsHidden(hidden: false);

    private void HideSelectedColumns() => SetSelectedColumnsHidden(hidden: true);

    private void UnhideSelectedColumns() => SetSelectedColumnsHidden(hidden: false);

    private void SetSelectedRowsHidden(bool hidden)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var (startRow, endRow) = SelectionRangeService.GetRowSpan(_session.SelectedRange);
        var result = _session.SetSelectedRowsHidden(hidden);
        if (result.Success)
            RefreshShell(hidden
                ? UiText.Format("RowColumn_RowsHidden", endRow - startRow + 1)
                : UiText.Get("RowColumn_RowsUnhidden"));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Get(hidden ? "RowColumn_HideRowsFailed" : "RowColumn_UnhideRowsFailed"));
    }

    private void SetSelectedColumnsHidden(bool hidden)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var (startCol, endCol) = SelectionRangeService.GetColumnSpan(_session.SelectedRange);
        var result = _session.SetSelectedColumnsHidden(hidden);
        if (result.Success)
            RefreshShell(hidden
                ? UiText.Format("RowColumn_ColumnsHidden", endCol - startCol + 1)
                : UiText.Get("RowColumn_ColumnsUnhidden"));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Get(hidden ? "RowColumn_HideColumnsFailed" : "RowColumn_UnhideColumnsFailed"));
    }

    /// <summary>
    /// Selects an entire row (every column) so subsequent row commands (hide/clear/etc.) target it.
    /// Extends the existing selection when <paramref name="extend"/> is set (Shift-click on a header).
    /// </summary>
    private void SelectEntireRow(uint row, bool extend = false)
    {
        var anchorRow = extend ? _session.ActiveCell.Row : row;
        SelectEntireRowRange(anchorRow, row);
    }

    private void SelectEntireRowFromHeaderDrag(uint targetRow, uint anchorRow) =>
        SelectEntireRowRange(anchorRow, targetRow);

    private void SelectEntireRowRange(uint anchorRow, uint targetRow)
    {
        var sheet = _session.ActiveSheet.Id;
        var range = GridSelectionNavigationPlanner.CreateWholeRowsRange(sheet, anchorRow, targetRow);

        // Match the WPF SelectRow route: a row-header click is a formula reference
        // while point mode is active, not a request to commit the edit first.
        if (TryApplyFormulaRangeSelection(
                range,
                new CellAddress(sheet, anchorRow, 1),
                new CellAddress(sheet, targetRow, CellAddress.MaxCol)))
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        range = MergedSelectionRangePlanner.ExpandToFullyContainMerges(_session.ActiveSheet, range);
        _session.SelectRange(range);
        RefreshTableContextualTab();
        ApplyFormatPainterAfterTargetSelection();
    }

    /// <summary>
    /// Row-header counterpart of <see cref="AddAdditionalColumnSelection"/>
    /// (R84-app-mouse-selection-5-1).
    /// </summary>
    private void AddAdditionalRowSelection(uint row)
    {
        var sheet = _session.ActiveSheet.Id;
        var newRange = GridSelectionNavigationPlanner.CreateWholeRowsRange(sheet, row, row);
        if (IsFormulaRangeEntryActiveForPointMode() &&
            TryAppendDisjointFormulaPointRange(newRange))
        {
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        newRange = MergedSelectionRangePlanner.ExpandToFullyContainMerges(_session.ActiveSheet, newRange);
        var ranges = GridSelectionNavigationPlanner.AppendDisjointSelectionArea(
            _session.SelectedRanges,
            _session.SelectedRange,
            newRange);
        _session.SelectRanges(newRange, ranges, newRange.Start);
        RefreshTableContextualTab();
        ApplyFormatPainterAfterTargetSelection();
    }

    /// <summary>
    /// Selects an entire column (every row) so subsequent column commands target it. Extends the
    /// existing selection when <paramref name="extend"/> is set (Shift-click on a header).
    /// </summary>
    private void SelectEntireColumn(uint col, bool extend = false)
    {
        var anchorCol = extend ? _session.ActiveCell.Col : col;
        SelectEntireColumnRange(anchorCol, col);
    }

    private void SelectEntireColumnFromHeaderDrag(uint targetCol, uint anchorCol) =>
        SelectEntireColumnRange(anchorCol, targetCol);

    private void SelectEntireColumnRange(uint anchorCol, uint targetCol)
    {
        var sheet = _session.ActiveSheet.Id;
        var range = GridSelectionNavigationPlanner.CreateWholeColumnsRange(sheet, anchorCol, targetCol);

        // Keep column-header point selection on the shared formula-entry path, as WPF does.
        if (TryApplyFormulaRangeSelection(
                range,
                new CellAddress(sheet, 1, anchorCol),
                new CellAddress(sheet, CellAddress.MaxRow, targetCol)))
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        range = MergedSelectionRangePlanner.ExpandToFullyContainMerges(_session.ActiveSheet, range);
        _session.SelectRange(range);
        RefreshTableContextualTab();
        ApplyFormatPainterAfterTargetSelection();
    }

    /// <summary>
    /// Ctrl+clicking a column header adds the whole column as a disjoint SECOND (or later)
    /// selection area instead of collapsing the selection down to just this column, mirroring the
    /// WPF host's AddAdditionalColumnSelection (MainWindow.Selection.cs, R49-render-multiarea-
    /// selection-3-2) for the Avalonia shell (R84-app-mouse-selection-5-1).
    /// </summary>
    private void AddAdditionalColumnSelection(uint col)
    {
        var sheet = _session.ActiveSheet.Id;
        var newRange = GridSelectionNavigationPlanner.CreateWholeColumnsRange(sheet, col, col);
        if (IsFormulaRangeEntryActiveForPointMode() &&
            TryAppendDisjointFormulaPointRange(newRange))
        {
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();
        newRange = MergedSelectionRangePlanner.ExpandToFullyContainMerges(_session.ActiveSheet, newRange);
        var ranges = GridSelectionNavigationPlanner.AppendDisjointSelectionArea(
            _session.SelectedRanges,
            _session.SelectedRange,
            newRange);
        _session.SelectRanges(newRange, ranges, newRange.Start);
        RefreshTableContextualTab();
        ApplyFormatPainterAfterTargetSelection();
    }

    /// <summary>
    /// Opens the row-header context menu (Hide/Unhide Rows, Row Height, Insert/Delete, etc.), built
    /// from the shared neutral <see cref="WorksheetContextMenuPlanner"/> RowSelection plan — the same
    /// plan the Windows host renders.
    /// </summary>
    private void OpenRowHeaderContextMenu(Control anchor)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.RowSelection);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        AvaloniaContextMenuRenderer.BuildContextMenu(ribbonMenu, DispatchWorksheetContextMenuCommand).Open(anchor);
    }

    /// <summary>
    /// Opens the column-header context menu (Hide/Unhide Columns, Column Width, Insert/Delete, etc.),
    /// built from the shared neutral ColumnSelection plan.
    /// </summary>
    private void OpenColumnHeaderContextMenu(Control anchor)
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.ColumnSelection);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        AvaloniaContextMenuRenderer.BuildContextMenu(ribbonMenu, DispatchWorksheetContextMenuCommand).Open(anchor);
    }

    // Row Height / Column Width / AutoFit (Excel parity: Home ▸ Cells ▸ Format and the row/column
    // header right-click menus). Sizing math + the undoable mutation are portable: the session drives
    // the shared SetRowHeight/SetColumnWidth commands and AutoFitSizingService via WorkbookSession.

    /// <summary>
    /// Prompts for a row height in points and applies it to the selected rows via the shared
    /// <c>SetRowHeightCommand</c> (matching the Windows host's Row Height dialog).
    /// </summary>
    private async Task ShowRowHeightDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var current = _session.GetSelectedRowHeight();
        var value = await ShowDimensionInputDialogAsync(
            UiText.Get("RowColumn_RowHeightDialogTitle"),
            UiText.Get("RowColumn_RowHeightDialogPrompt"),
            current,
            min: 0,
            max: 409.5,
            automationId: "RowHeightValueBox");
        if (value is not { } height)
            return;

        var result = _session.SetSelectedRowsHeight(height);
        if (result.Success)
            RefreshShell(UiText.Format("RowColumn_RowHeightApplied", FormatDimension(height)));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Get("RowColumn_RowHeightFailed"));
    }

    /// <summary>
    /// Prompts for a column width in characters and applies it to the selected columns via the shared
    /// <c>SetColumnWidthCommand</c> (matching the Windows host's Column Width dialog).
    /// </summary>
    private async Task ShowColumnWidthDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var current = _session.GetSelectedColumnWidth();
        var value = await ShowDimensionInputDialogAsync(
            UiText.Get("RowColumn_ColumnWidthDialogTitle"),
            UiText.Get("RowColumn_ColumnWidthDialogPrompt"),
            current,
            min: 0,
            max: 255,
            automationId: "ColumnWidthValueBox");
        if (value is not { } width)
            return;

        var result = _session.SetSelectedColumnsWidth(width);
        if (result.Success)
            RefreshShell(UiText.Format("RowColumn_ColumnWidthApplied", FormatDimension(width)));
        else
            RefreshShell(result.ErrorMessage ?? UiText.Get("RowColumn_ColumnWidthFailed"));
    }

    /// <summary>
    /// AutoFits the selected rows' heights to their content using the shared content-based estimate
    /// (AutoFitSizingService — character/line counts, not true glyph metrics).
    /// </summary>
    private void AutoFitSelectedRowHeight()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.AutoFitSelectedRowHeight();
        RefreshShell(result.Success
            ? UiText.Get("RowColumn_RowHeightAutoFitted")
            : result.ErrorMessage ?? UiText.Get("RowColumn_AutoFitRowFailed"));
    }

    /// <summary>
    /// AutoFits the selected columns' widths to their content using the shared content-based estimate
    /// (AutoFitSizingService — character/line counts, not true glyph metrics).
    /// </summary>
    private void AutoFitSelectedColumnWidth()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = _session.AutoFitSelectedColumnWidth();
        RefreshShell(result.Success
            ? UiText.Get("RowColumn_ColumnWidthAutoFitted")
            : result.ErrorMessage ?? UiText.Get("RowColumn_AutoFitColumnFailed"));
    }

    private static string FormatDimension(double value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>
    /// Single-numeric-input modal used by the Row Height / Column Width dialogs. Returns the parsed,
    /// clamped value, or null on cancel / invalid input. The Core command re-validates the range, so
    /// this clamp is only for an immediate, friendly result.
    /// </summary>
    private async Task<double?> ShowDimensionInputDialogAsync(
        string title,
        string prompt,
        double current,
        double min,
        double max,
        string automationId)
    {
        double? result = null;
        var dialog = new Window
        {
            Title = title,
            Width = 320,
            Height = 170,
            MinWidth = 280,
            MinHeight = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var valueBox = new TextBox
        {
            Text = current.ToString("0.##", CultureInfo.CurrentCulture),
            MinWidth = 240,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(valueBox, RowColumnDialogChromeStyle);
        AutomationProperties.SetName(valueBox, prompt);
        AutomationProperties.SetAutomationId(valueBox, automationId);

        var validationText = new TextBlock();
        AvaloniaCompactDialogChrome.ApplyValidationStatus(validationText, RowColumnDialogChromeStyle);

        var okButton = new Button { Content = "OK", IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(okButton, RowColumnDialogChromeStyle, 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancelButton, RowColumnDialogChromeStyle, 84);
        AutomationProperties.SetAutomationId(okButton, "DimensionDialogOkButton");
        AutomationProperties.SetAutomationId(cancelButton, "DimensionDialogCancelButton");

        void Accept()
        {
            var text = (valueBox.Text ?? "").Trim();
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                validationText.Text = prompt;
                validationText.IsVisible = true;
                valueBox.Focus();
                valueBox.SelectAll();
                return;
            }

            result = Math.Clamp(parsed, min, max);
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([cancelButton, okButton]);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = prompt },
                valueBox,
                validationText,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            valueBox.Focus();
            valueBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }
}
