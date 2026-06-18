using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Allow Users to Edit Ranges dialog for the Avalonia/macOS shell (Review ▸ Protect). It lists the active
/// sheet's stored allowed-edit ranges and lets the user add, modify, or delete a titled range by its cell
/// reference. The range parsing, list projection, button enablement, and result records come from the
/// portable <see cref="AllowEditRangePlanner"/>; the add/modify/remove actions map onto the Core
/// allow-edit-range commands run through the shared session command path. User-facing strings route through
/// <see cref="UiText"/>.
/// </summary>
public sealed partial class MainWindow
{
    // ── Review ▸ Protect entry point ───────────────────────────────────────────
    private void AllowEditRanges() => _ = ShowAllowEditRangeDialogAsync();

    private async Task ShowAllowEditRangeDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var sheetId = _session.ActiveSheet.Id;

        var dialog = new Window
        {
            Title = UiText.Get("AllowEditRange_Title"),
            Width = 420,
            Height = 380,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "AllowEditRangeDialog");

        var rangesList = new ListBox { MinHeight = 120 };
        AutomationProperties.SetAutomationId(rangesList, "AllowEditRangeExistingRangesList");

        var rangeBox = new TextBox
        {
            Text = FormatRangeReference(_session.SelectedRange),
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(rangeBox, "AllowEditRangeBox");

        var newButton = new Button { Content = UiText.Get("AllowEditRange_NewButton"), MinWidth = 84 };
        AutomationProperties.SetAutomationId(newButton, "AllowEditRangeNewButton");
        var modifyButton = new Button { Content = UiText.Get("AllowEditRange_ModifyButton"), MinWidth = 84, IsEnabled = false };
        AutomationProperties.SetAutomationId(modifyButton, "AllowEditRangeModifyButton");
        var deleteButton = new Button { Content = UiText.Get("AllowEditRange_DeleteButton"), MinWidth = 84, IsEnabled = false };
        AutomationProperties.SetAutomationId(deleteButton, "AllowEditRangeDeleteButton");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "AllowEditRangeWarningText");

        // The range currently loaded into the edit box for modification (null when adding a new range).
        GridRange? rangeBeingModified = null;

        void RefreshRanges()
        {
            rangesList.ItemsSource = AllowEditRangePlanner.BuildExistingRangeItems(_session.ActiveSheet.AllowEditRanges);
            UpdateButtons();
        }

        void UpdateButtons()
        {
            var state = AllowEditRangePlanner.BuildButtonState(rangesList.ItemCount, rangesList.SelectedItem is not null);
            modifyButton.IsEnabled = state.CanModifySelectedRange;
            deleteButton.IsEnabled = state.CanDeleteSelectedRange;
        }

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        rangesList.SelectionChanged += (_, _) =>
        {
            if (rangeBeingModified is not null &&
                (rangesList.SelectedItem is not string selected ||
                 !AllowEditRangePlanner.TryParseRange(selected, sheetId, out var selectedRange) ||
                 selectedRange != rangeBeingModified))
            {
                rangeBeingModified = null;
            }

            UpdateButtons();
        };

        bool TryExecute(IWorkbookCommand command, string status)
        {
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                ShowWarning(result.ErrorMessage ?? UiText.Get("AllowEditRange_InvalidRange"));
                return false;
            }

            RefreshShell(status);
            RefreshRanges();
            return true;
        }

        newButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            if (!AllowEditRangePlanner.TryParseRange(rangeBox.Text, sheetId, out var range))
            {
                ShowWarning(UiText.Get("AllowEditRange_InvalidRange"));
                return;
            }

            var result = AllowEditRangePlanner.CreateAddResult(range);
            TryExecute(
                new AllowEditRangeCommand(sheetId, result.Range!.Value),
                UiText.Format("AllowEditRange_Added", result.Range!.Value));
            rangeBeingModified = null;
        };

        modifyButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            if (rangesList.SelectedItem is not string selected ||
                !AllowEditRangePlanner.TryParseRange(selected, sheetId, out var originalRange))
            {
                return;
            }

            // First click loads the selected range into the edit box; a second click commits the edit.
            if (rangeBeingModified != originalRange)
            {
                rangeBeingModified = originalRange;
                rangeBox.Text = selected;
                return;
            }

            if (!AllowEditRangePlanner.TryParseRange(rangeBox.Text, sheetId, out var updatedRange))
            {
                ShowWarning(UiText.Get("AllowEditRange_InvalidRange"));
                return;
            }

            var result = AllowEditRangePlanner.CreateModifyResult(originalRange, updatedRange);
            var command = new CompositeWorkbookCommand(
                "Modify Allow Edit Range",
                [
                    new RemoveAllowEditRangeCommand(sheetId, result.PreviousRange!.Value),
                    new AllowEditRangeCommand(sheetId, result.Range!.Value),
                ]);
            if (TryExecute(command, UiText.Format("AllowEditRange_Modified", result.Range!.Value)))
                rangeBeingModified = null;
        };

        deleteButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            if (rangesList.SelectedItem is not string selected ||
                !AllowEditRangePlanner.TryParseRange(selected, sheetId, out var range))
            {
                return;
            }

            var result = AllowEditRangePlanner.CreateRemoveResult(range);
            TryExecute(
                new RemoveAllowEditRangeCommand(sheetId, result.Range!.Value),
                UiText.Format("AllowEditRange_Removed", result.Range!.Value));
            rangeBeingModified = null;
        };

        var closeButton = new Button { Content = UiText.Get("Common_Close"), IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(closeButton, "AllowEditRangeCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        RefreshRanges();

        var rangeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { newButton, modifyButton, deleteButton },
        };

        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { closeButton },
        };
        DockPanel.SetDock(bottomRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                bottomRow,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = UiText.Get("AllowEditRange_Intro"), Foreground = HeaderForeground, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = UiText.Get("AllowEditRange_ExistingRangesLabel"), Foreground = HeaderForeground },
                        rangesList,
                        rangeButtons,
                        new TextBlock { Text = UiText.Get("AllowEditRange_RangeLabel"), Foreground = HeaderForeground },
                        rangeBox,
                        new TextBlock { Text = UiText.Get("AllowEditRange_Example"), Foreground = SecondaryInk, TextWrapping = TextWrapping.Wrap },
                        warningText,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }
}
