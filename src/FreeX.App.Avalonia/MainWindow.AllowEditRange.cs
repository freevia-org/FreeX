using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

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
            Width = 430,
            Height = 400,
            MinWidth = 390,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "AllowEditRangeDialog");

        var rangesList = new ListBox { MinHeight = 80 };
        ApplyDataOpsListBoxChrome(rangesList);
        AutomationProperties.SetAutomationId(rangesList, "AllowEditRangeExistingRangesList");

        var rangeBox = new TextBox
        {
            Text = FormatRangeReference(_session.SelectedRange),
            MinWidth = 220,
        };
        ApplyDataOpsTextBoxChrome(rangeBox);
        AutomationProperties.SetAutomationId(rangeBox, "AllowEditRangeBox");
        var rangePicker = CreateDialogRangePickerButton(
            "AllowEditRangePickerButton",
            UiText.Get("AllowEditRange_PickerAutomationName"));
        AutomationProperties.SetHelpText(rangePicker, UiText.Get("AllowEditRange_PickerHelpText"));
        ToolTip.SetTip(rangePicker, UiText.Get("AllowEditRange_PickerToolTip"));

        // Range-specific password (Excel's per-range "Range Password", distinct from the sheet password):
        // optional, so an empty box means the range stays freely editable once reached. WPF parity
        // (AllowEditRangeDialog.cs, _rangePasswordBox).
        var rangePasswordBox = new TextBox { PasswordChar = '•', MinWidth = 220 };
        ApplyDataOpsTextBoxChrome(rangePasswordBox);
        AutomationProperties.SetName(rangePasswordBox, UiText.Get("Protection_PasswordAutomationName"));
        AutomationProperties.SetAutomationId(rangePasswordBox, "AllowEditRangePasswordBox");
        AutomationProperties.SetHelpText(rangePasswordBox, UiText.Get("Protection_PasswordHelpText"));

        var newButton = new Button { Content = UiText.Get("AllowEditRange_NewButton"), MinWidth = 82 };
        ApplyDataOpsButtonChrome(newButton);
        AutomationProperties.SetAutomationId(newButton, "AllowEditRangeNewButton");
        var modifyButton = new Button { Content = UiText.Get("AllowEditRange_ModifyButton"), MinWidth = 82, IsEnabled = false };
        ApplyDataOpsButtonChrome(modifyButton);
        AutomationProperties.SetAutomationId(modifyButton, "AllowEditRangeModifyButton");
        var deleteButton = new Button { Content = UiText.Get("AllowEditRange_DeleteButton"), MinWidth = 82, IsEnabled = false };
        ApplyDataOpsButtonChrome(deleteButton);
        AutomationProperties.SetAutomationId(deleteButton, "AllowEditRangeDeleteButton");
        // WPF has a Permissions button (always disabled in this implementation)
        var permissionsButton = new Button { Content = UiText.Get("AllowEditRange_PermissionsButton"), MinWidth = 100, IsEnabled = false };
        ApplyDataOpsButtonChrome(permissionsButton);
        AutomationProperties.SetAutomationId(permissionsButton, "AllowEditRangePermissionsButton");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
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
            var typedPassword = string.IsNullOrEmpty(rangePasswordBox.Text) ? null : rangePasswordBox.Text;
            var command = new CompositeWorkbookCommand(
                "Allow Edit Range",
                [
                    new AllowEditRangeCommand(sheetId, result.Range!.Value),
                    new SetAllowEditRangePasswordCommand(
                        sheetId,
                        result.Range!.Value,
                        typedPassword is null ? null : ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword)),
                ]);
            TryExecute(command, UiText.Format("AllowEditRange_Added", result.Range!.Value));
            rangeBeingModified = null;
            rangePasswordBox.Text = string.Empty;
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
                // Mirrors Excel/WPF: an existing range password is never redisplayed (only its hash is
                // known), so the box is always cleared here. A blank box left on commit means "leave the
                // stored password (if any) alone" -- see the modify branch below.
                rangePasswordBox.Text = string.Empty;
                return;
            }

            if (!AllowEditRangePlanner.TryParseRange(rangeBox.Text, sheetId, out var updatedRange))
            {
                ShowWarning(UiText.Get("AllowEditRange_InvalidRange"));
                return;
            }

            var result = AllowEditRangePlanner.CreateModifyResult(originalRange, updatedRange);
            var modifyCommands = new List<IWorkbookCommand>
            {
                new RemoveAllowEditRangeCommand(sheetId, result.PreviousRange!.Value),
                new AllowEditRangeCommand(sheetId, result.Range!.Value),
            };

            var typedPassword = string.IsNullOrEmpty(rangePasswordBox.Text) ? null : rangePasswordBox.Text;
            if (typedPassword is not null)
            {
                modifyCommands.Add(new SetAllowEditRangePasswordCommand(
                    sheetId,
                    result.Range!.Value,
                    ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword)));
            }
            else if (!result.Range!.Value.Equals(result.PreviousRange!.Value) &&
                     _session.ActiveSheet.AllowEditRangePasswords.TryGetValue(result.PreviousRange!.Value, out var carriedPassword))
            {
                // The range's key changed (e.g. its bounds were edited) but the password was left
                // untouched -- carry the existing password over to the new key so it is not lost.
                modifyCommands.Add(new SetAllowEditRangePasswordCommand(sheetId, result.Range!.Value, carriedPassword));
            }

            var command = new CompositeWorkbookCommand("Modify Allow Edit Range", modifyCommands);
            if (TryExecute(command, UiText.Format("AllowEditRange_Modified", result.Range!.Value)))
            {
                rangeBeingModified = null;
                rangePasswordBox.Text = string.Empty;
            }
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
            var command = new CompositeWorkbookCommand(
                "Remove Allow Edit Range",
                [
                    new RemoveAllowEditRangeCommand(sheetId, result.Range!.Value),
                    new SetAllowEditRangePasswordCommand(sheetId, result.Range!.Value, null),
                ]);
            TryExecute(command, UiText.Format("AllowEditRange_Removed", result.Range!.Value));
            rangeBeingModified = null;
            rangePasswordBox.Text = string.Empty;
        };

        // WPF has [OK][Cancel] at bottom; OK is an alias for Close (ranges are applied in real-time)
        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(okButton, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "AllowEditRangeOkButton");
        okButton.Click += (_, _) => dialog.Close();
        var closeButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(closeButton);
        AutomationProperties.SetAutomationId(closeButton, "AllowEditRangeCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        RefreshRanges();

        // WPF button order: [New...][Modify...][Delete][Permissions...] in a row, right-aligned
        var rangeButtons = AvaloniaCompactDialogChrome.CreateActionRow(
            [newButton, modifyButton, deleteButton, permissionsButton],
            new Thickness(0, 8, 0, 0));

        // WPF: GroupBox (no explicit header visible) containing label + list + action buttons.
        // The label is shown as the GroupBox Header so it matches the WPF visual framing.
        var existingRangesGroupContent = new DockPanel { Margin = new Thickness(4), LastChildFill = true };
        DockPanel.SetDock(rangeButtons, Dock.Bottom);
        existingRangesGroupContent.Children.Add(rangeButtons);
        existingRangesGroupContent.Children.Add(rangesList);

        var existingRangesGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("AllowEditRange_ExistingRangesLabel")),
            Content = existingRangesGroupContent,
            Margin = new Thickness(0, 4, 0, 8),
        };

        // WPF: second GroupBox with "Range" header containing the cell-reference textbox.
        var rangeGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("AllowEditRange_RangeLabel")),
            Content = new Border
            {
                Padding = new Thickness(4),
                Child = BuildDialogRangePickerRow(rangeBox, rangePicker),
            },
            Margin = new Thickness(0, 0, 0, 6),
        };

        // Range-specific password label + box (WPF parity: AllowEditRangeDialog.cs's Protection_Password
        // label/box sit directly under the range example text, before the OK/Cancel row).
        var rangePasswordPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("Protection_Password")),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                rangePasswordBox,
            },
        };

        // WPF bottom button order: [OK][Cancel]
        var bottomRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, closeButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(bottomRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                bottomRow,
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = UiText.Get("AllowEditRange_Intro"), Foreground = HeaderForeground, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = FormulaBarFontFamily, Margin = new Thickness(0, 0, 0, 4) },
                        existingRangesGroup,
                        rangeGroup,
                        new TextBlock { Text = UiText.Get("AllowEditRange_Example"), Foreground = SecondaryInk, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = FormulaBarFontFamily },
                        rangePasswordPanel,
                        warningText,
                    },
                },
            },
        };
        AttachDialogRangePicker(dialog, rangePicker, rangeBox, "range.allow-edit-range.range");
        // Match WPF AllowEditRangeDialog.Loaded: the range input owns initial focus and its
        // contents are selected so typing immediately replaces the current reference.
        dialog.Opened += (_, _) =>
        {
            rangeBox.Focus();
            rangeBox.SelectAll();
        };

        await dialog.ShowDialog(this);
    }
}
