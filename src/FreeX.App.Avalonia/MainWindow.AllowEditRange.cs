using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private static AvaloniaCompactDialogChromeStyle AllowEditRangeDialogChromeStyle =>
        new(FormulaBarFontFamily)
        {
            ControlHeight = 20,
            TextBoxHeight = 18,
            ButtonHeight = 20,
            ButtonPadding = new Thickness(4, 1),
            RemoveFocusAdorner = true,
        };

    // ── Review ▸ Protect entry point ───────────────────────────────────────────
    private void AllowEditRanges() => _ = ShowAllowEditRangeDialogAsync();

    private async Task ShowAllowEditRangeDialogAsync(string? initialRangeText = null)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var sheetId = _session.ActiveSheet.Id;

        var dialog = new Window
        {
            Title = UiText.Get("AllowEditRange_Title"),
            Width = 430,
            Height = 420,
            MinWidth = 390,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "AllowEditRangeDialog");
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, AllowEditRangeDialogChromeStyle);

        var rangesList = new ListBox { MinHeight = 80 };
        AvaloniaCompactDialogChrome.ApplyListBox(rangesList, AllowEditRangeDialogChromeStyle);
        AutomationProperties.SetAutomationId(rangesList, "AllowEditRangeExistingRangesList");

        var rangeBox = new TextBox
        {
            Text = initialRangeText ?? FormatRangeReference(_session.SelectedRange),
            MinWidth = 220,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(rangeBox, AllowEditRangeDialogChromeStyle);
        AutomationProperties.SetAutomationId(rangeBox, "AllowEditRangeBox");

        // F4 on the bare WPF-like range field enters the shared worksheet pointing session.
        // The registered picker remains collapsed so it contributes no visual or tab-layout space.
        var rangePicker = CreateDialogRangePickerButton(
            "AllowEditRangePickerButton",
            UiText.Get("AllowEditRange_RangeAutomationName"));
        rangePicker.IsVisible = false;
        rangePicker.IsTabStop = false;

        // Range-specific password (Excel's per-range "Range Password", distinct from the sheet password):
        // optional, so an empty box means the range stays freely editable once reached. WPF parity
        // (AllowEditRangeDialog.cs, _rangePasswordBox).
        var rangePasswordBox = new TextBox { PasswordChar = '•', MinWidth = 220 };
        AvaloniaCompactDialogChrome.ApplyTextBox(rangePasswordBox, AllowEditRangeDialogChromeStyle);
        AutomationProperties.SetName(rangePasswordBox, UiText.Get("Protection_PasswordAutomationName"));
        AutomationProperties.SetAutomationId(rangePasswordBox, "AllowEditRangePasswordBox");
        AutomationProperties.SetHelpText(rangePasswordBox, UiText.Get("Protection_PasswordHelpText"));

        var newButton = new Button { Content = UiText.Get("AllowEditRange_NewButton"), MinWidth = 82 };
        AvaloniaCompactDialogChrome.ApplyButton(newButton, AllowEditRangeDialogChromeStyle, newButton.MinWidth);
        AutomationProperties.SetAutomationId(newButton, "AllowEditRangeNewButton");
        var modifyButton = new Button { Content = UiText.Get("AllowEditRange_ModifyButton"), MinWidth = 82, IsEnabled = false };
        AvaloniaCompactDialogChrome.ApplyButton(modifyButton, AllowEditRangeDialogChromeStyle, modifyButton.MinWidth);
        AutomationProperties.SetAutomationId(modifyButton, "AllowEditRangeModifyButton");
        var deleteButton = new Button { Content = UiText.Get("AllowEditRange_DeleteButton"), MinWidth = 82, IsEnabled = false };
        AvaloniaCompactDialogChrome.ApplyButton(deleteButton, AllowEditRangeDialogChromeStyle, deleteButton.MinWidth);
        AutomationProperties.SetAutomationId(deleteButton, "AllowEditRangeDeleteButton");
        // WPF has a Permissions button (always disabled in this implementation)
        var permissionsButton = new Button { Content = UiText.Get("AllowEditRange_PermissionsButton"), MinWidth = 100, IsEnabled = false };
        AvaloniaCompactDialogChrome.ApplyButton(permissionsButton, AllowEditRangeDialogChromeStyle, permissionsButton.MinWidth);
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
            var plan = AllowEditRangePlanner.CreateCommandPlan(
                sheetId,
                result,
                typedPassword is null ? null : ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword),
                passwordChanged: true,
                existingPasswords: _session.ActiveSheet.AllowEditRangePasswords);
            if (plan is not null)
                TryExecute(plan.Command, UiText.Format("AllowEditRange_Added", result.Range!.Value));
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
            var typedPassword = string.IsNullOrEmpty(rangePasswordBox.Text) ? null : rangePasswordBox.Text;
            var plan = AllowEditRangePlanner.CreateCommandPlan(
                sheetId,
                result,
                typedPassword is null ? null : ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword),
                passwordChanged: typedPassword is not null,
                existingPasswords: _session.ActiveSheet.AllowEditRangePasswords);
            if (plan is not null &&
                TryExecute(plan.Command, UiText.Format("AllowEditRange_Modified", result.Range!.Value)))
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
            var plan = AllowEditRangePlanner.CreateCommandPlan(
                sheetId,
                result,
                password: null,
                passwordChanged: false,
                existingPasswords: _session.ActiveSheet.AllowEditRangePasswords);
            if (plan is not null)
                TryExecute(plan.Command, UiText.Format("AllowEditRange_Removed", result.Range!.Value));
            rangeBeingModified = null;
            rangePasswordBox.Text = string.Empty;
        };

        // WPF has [OK][Cancel] at bottom; OK is an alias for Close (ranges are applied in real-time)
        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        AvaloniaCompactDialogChrome.ApplyButton(okButton, AllowEditRangeDialogChromeStyle, okButton.MinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "AllowEditRangeOkButton");
        okButton.Click += (_, _) => dialog.Close();
        var closeButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        AvaloniaCompactDialogChrome.ApplyButton(closeButton, AllowEditRangeDialogChromeStyle, closeButton.MinWidth);
        AutomationProperties.SetAutomationId(closeButton, "AllowEditRangeCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        RefreshRanges();

        // WPF button order: [New...][Modify...][Delete][Permissions...] in a left-aligned row.
        var rangeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { newButton, modifyButton, deleteButton, permissionsButton },
        };

        // WPF: GroupBox containing the list and its left-aligned action row.
        var existingRangesGroupContent = new StackPanel
        {
            Margin = new Thickness(4, 10, 2, 6),
            Children = { rangesList, rangeButtons },
        };

        var existingRangesGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("AllowEditRange_ExistingRangesLabel")),
            Content = existingRangesGroupContent,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
        };
        AvaloniaCompactDialogChrome.ApplyGroupBox(existingRangesGroup, AllowEditRangeDialogChromeStyle);

        // WPF: the Range section is a bare label and editor with no inline picker.
        var rangeGroup = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("AllowEditRange_RangeLabel")),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                rangeBox,
            },
            Margin = new Thickness(0, 0, 0, 5),
        };

        // Range-specific password label + box (WPF parity: AllowEditRangeDialog.cs's Protection_Password
        // label/box sit directly under the range example text, before the OK/Cancel row).
        var rangePasswordPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 8, 0, 0),
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
        var bottomRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, closeButton], style: AvaloniaCompactDialogChrome.WindowsStyle);

        var dialogBody = new StackPanel
        {
            Width = 390,
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
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(4, 12, 12, 12),
            Width = 390,
            Children =
            {
                dialogBody,
                bottomRow,
                rangePicker,
            },
        };
        AttachDialogRangePicker(dialog, rangePicker, rangeBox, "range.allow-edit-range.range");
        rangeBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.F4 || args.KeyModifiers != KeyModifiers.None)
                return;

            rangePicker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent) { Source = rangePicker });
            args.Handled = true;
        };
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
