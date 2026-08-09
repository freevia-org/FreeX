using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle InsertDeleteCellsDialogChromeStyle => new(FormulaBarFontFamily);

    // Home ▸ Cells ▸ Insert Cells / Delete Cells (parity gap: the ribbon buttons were no-ops). A small
    // shift-direction dialog mirrors Excel's prompt; the structural edit runs through the generic
    // review-command executor (undo/redo). Kept in the Avalonia shell to avoid WorkbookSession churn.
    //
    // R79-commands-insert-delete-shift-5-2: a whole-row/whole-column selection must route to
    // InsertRowsCommand/InsertColumnsCommand (and their Delete- counterparts) exactly as the WPF
    // host's KeyboardInsertDeletePlanner does — never through the band-scoped InsertCellsCommand/
    // DeleteCellsCommand, which builds its shift band from the selection's row/col span and would
    // treat a whole-column selection's full row span (1..MaxRow) as a near-sheet-height band. That
    // both spuriously trips the table/AutoFilter overlap guard for unrelated tables elsewhere in the
    // column and skips the whole-column-only state (AutoFilter.Reference, FilterHiddenRows, form
    // controls, pivot tables, sparklines, watched cells) that only the Rows/Columns commands shift.

    private async Task ShowInsertCellsDialogAsync()
    {
        var range = _session.SelectedRange;
        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            var rowResult = _session.ExecuteReviewCommand(
                new InsertRowsCommand(_session.ActiveSheet.Id, range.Start.Row, range.RowCount));
            // R127B-avalonia-clipboard-marquee-structural-1: WorkbookSession.ExecuteReviewCommand
            // now retires the SESSION-level pending Copy/Cut on a successful structural edit (see
            // WorkbookSession.IsStructuralCellShiftCommand), but the Avalonia shell's own marching-
            // ants overlay state (_clipboardMarqueeRange/_clipboardMarqueeIsCut in MainWindow.cs) is
            // separate UI-only state that RefreshShell does not touch -- clear it explicitly here too,
            // matching the WPF host's ClearClipboardMarqueeAfterStructuralEdit (which clears both
            // _internalClipboard AND SheetGrid.ClipboardRange/ClipboardIsCut together) and this
            // shell's own InsertContextRow/InsertContextColumn (MainWindow.ContextMenuGridActions.cs).
            if (rowResult.Success)
                SetClipboardMarquee(null, isCut: false);
            RefreshShell(rowResult.Success ? "Inserted rows" : rowResult.ErrorMessage ?? "Could not insert rows.");
            return;
        }
        if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            var colResult = _session.ExecuteReviewCommand(
                new InsertColumnsCommand(_session.ActiveSheet.Id, range.Start.Col, range.ColCount));
            if (colResult.Success)
                SetClipboardMarquee(null, isCut: false);
            RefreshShell(colResult.Success ? "Inserted columns" : colResult.ErrorMessage ?? "Could not insert columns.");
            return;
        }

        var choice = await ShowShiftDirectionAsync("Insert Cells", "Shift cells right", "Shift cells down");
        if (choice is null)
            return;
        var direction = choice == 0 ? InsertCellsShiftDirection.Right : InsertCellsShiftDirection.Down;
        var result = _session.ExecuteReviewCommand(new InsertCellsCommand(_session.ActiveSheet.Id, range, direction));
        if (result.Success)
            SetClipboardMarquee(null, isCut: false);
        RefreshShell(result.Success
            ? $"Inserted cells ({(direction == InsertCellsShiftDirection.Right ? "shift right" : "shift down")})"
            : result.ErrorMessage ?? "Could not insert cells.");
    }

    private async Task ShowDeleteCellsDialogAsync()
    {
        var range = _session.SelectedRange;
        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            var rowResult = _session.ExecuteReviewCommand(
                new DeleteRowsCommand(_session.ActiveSheet.Id, range.Start.Row, range.RowCount));
            if (rowResult.Success)
                SetClipboardMarquee(null, isCut: false);
            RefreshShell(rowResult.Success ? "Deleted rows" : rowResult.ErrorMessage ?? "Could not delete rows.");
            return;
        }
        if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            var colResult = _session.ExecuteReviewCommand(
                new DeleteColumnsCommand(_session.ActiveSheet.Id, range.Start.Col, range.ColCount));
            if (colResult.Success)
                SetClipboardMarquee(null, isCut: false);
            RefreshShell(colResult.Success ? "Deleted columns" : colResult.ErrorMessage ?? "Could not delete columns.");
            return;
        }

        var choice = await ShowShiftDirectionAsync("Delete Cells", "Shift cells left", "Shift cells up");
        if (choice is null)
            return;
        var direction = choice == 0 ? DeleteCellsShiftDirection.Left : DeleteCellsShiftDirection.Up;
        var result = _session.ExecuteReviewCommand(new DeleteCellsCommand(_session.ActiveSheet.Id, range, direction));
        if (result.Success)
            SetClipboardMarquee(null, isCut: false);
        RefreshShell(result.Success
            ? $"Deleted cells ({(direction == DeleteCellsShiftDirection.Left ? "shift left" : "shift up")})"
            : result.ErrorMessage ?? "Could not delete cells.");
    }

    /// <summary>Two-option shift-direction prompt. Returns 0 (first), 1 (second), or null if cancelled.</summary>
    private async Task<int?> ShowShiftDirectionAsync(string title, string optionA, string optionB)
    {
        var first = new RadioButton { Content = optionA, GroupName = "shift", IsChecked = true, Margin = new Thickness(0, 2) };
        var second = new RadioButton { Content = optionB, GroupName = "shift", Margin = new Thickness(0, 2) };
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyRadioButton(first, InsertDeleteCellsDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(second, InsertDeleteCellsDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyButton(ok, InsertDeleteCellsDialogChromeStyle, 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, InsertDeleteCellsDialogChromeStyle, 84);

        var dialog = new Window
        {
            Title = title,
            Width = 320,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    first,
                    second,
                    AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
                },
            },
        };

        ok.Click += (_, _) => dialog.Close(second.IsChecked == true ? (int?)1 : 0);
        cancel.Click += (_, _) => dialog.Close((int?)null);
        return await dialog.ShowDialog<int?>(this);
    }
}
