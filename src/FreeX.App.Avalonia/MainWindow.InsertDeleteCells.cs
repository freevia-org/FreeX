using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FreeX.Core.Commands;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Home ▸ Cells ▸ Insert Cells / Delete Cells (parity gap: the ribbon buttons were no-ops). A small
    // shift-direction dialog mirrors Excel's prompt; the structural edit runs through the generic
    // review-command executor (undo/redo). Kept in the Avalonia shell to avoid WorkbookSession churn.

    private async Task ShowInsertCellsDialogAsync()
    {
        var choice = await ShowShiftDirectionAsync("Insert Cells", "Shift cells right", "Shift cells down");
        if (choice is null)
            return;
        var direction = choice == 0 ? InsertCellsShiftDirection.Right : InsertCellsShiftDirection.Down;
        var range = _session.SelectedRange;
        var result = _session.ExecuteReviewCommand(new InsertCellsCommand(_session.ActiveSheet.Id, range, direction));
        RefreshShell(result.Success
            ? $"Inserted cells ({(direction == InsertCellsShiftDirection.Right ? "shift right" : "shift down")})"
            : result.ErrorMessage ?? "Could not insert cells.");
    }

    private async Task ShowDeleteCellsDialogAsync()
    {
        var choice = await ShowShiftDirectionAsync("Delete Cells", "Shift cells left", "Shift cells up");
        if (choice is null)
            return;
        var direction = choice == 0 ? DeleteCellsShiftDirection.Left : DeleteCellsShiftDirection.Up;
        var range = _session.SelectedRange;
        var result = _session.ExecuteReviewCommand(new DeleteCellsCommand(_session.ActiveSheet.Id, range, direction));
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
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        ApplyDialogButtonChrome(ok, 84, isDefault: true);
        ApplyDialogButtonChrome(cancel, 84);

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
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 12, 0, 0),
                        Children = { ok, cancel },
                    },
                },
            },
        };

        ok.Click += (_, _) => dialog.Close(second.IsChecked == true ? (int?)1 : 0);
        cancel.Click += (_, _) => dialog.Close((int?)null);
        return await dialog.ShowDialog<int?>(this);
    }
}
