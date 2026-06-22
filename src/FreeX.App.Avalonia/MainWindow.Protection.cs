using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Protect Sheet and Protect Workbook dialogs for the Avalonia/macOS shell (Review menu). When the target is
/// unprotected the dialog collects a password (with confirmation) and — for a sheet — the allowed-action
/// checklist seeded from <see cref="SheetProtectionOptions"/>; OK validates the confirm-match through
/// <see cref="ProtectionPassword"/> and runs the Core protect command. When the target is already protected
/// the action unprotects instead, prompting for the password when one is stored. The portable dialog model,
/// validation, and projection come from <see cref="FreeX.App.Presentation.Protection"/>; the non-UI mapping
/// onto Core protect/unprotect commands lives in <see cref="ProtectionShellGlue"/>; commands run through the
/// shared session command path.
/// </summary>
public sealed partial class MainWindow
{
    // ── Review ▸ Protect menu entry points ─────────────────────────────────────
    private void ProtectSheet() => _ = ShowProtectSheetDialogAsync();

    private void ProtectWorkbook() => _ = ShowProtectWorkbookDialogAsync();

    /// <summary>
    /// The Protect Sheet dialog. For an unprotected sheet it offers a password (with confirmation) and the
    /// allowed-action checklist from <see cref="SheetProtectionOptions.All"/> (seeded with the default toggles);
    /// OK validates the confirm-match and runs the Core <see cref="FreeX.Core.Commands.ProtectSheetCommand"/>.
    /// For an already-protected sheet it acts as an unprotect prompt: it collects the password only (when one is
    /// stored) and runs the Core <see cref="FreeX.Core.Commands.UnprotectSheetCommand"/>.
    /// </summary>
    private async Task ShowProtectSheetDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var state = ProtectionShellGlue.ProjectSheet(_session.ActiveSheet);
        var sheetId = _session.ActiveSheet.Id;

        var dialog = new Window
        {
            Title = state.IsProtected ? UiText.Get("ShellLoc_UnprotectSheetTitle") : UiText.Get("ShellLoc_ProtectSheetTitle"),
            Width = state.IsProtected ? 380 : 430,
            Height = state.IsProtected ? 200 : 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ProtectSheetDialog");

        var passwordBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        AutomationProperties.SetAutomationId(passwordBox, "ProtectSheetPasswordBox");
        var confirmBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        AutomationProperties.SetAutomationId(confirmBox, "ProtectSheetConfirmBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "ProtectSheetWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "ProtectSheetOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "ProtectSheetCancelButton");
        cancelButton.Click += (_, _) => dialog.Close();

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        var contentChildren = new Controls();

        if (state.IsProtected)
        {
            contentChildren.Add(new TextBlock
            {
                Text = state.HasPassword
                    ? UiText.Get("ShellLoc_SheetProtectedEnterPassword")
                    : UiText.Get("ShellLoc_SheetProtectedClickOk"),
                Foreground = HeaderForeground,
                TextWrapping = TextWrapping.Wrap,
            });

            if (state.HasPassword)
                contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordLabel"), passwordBox));

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;
                var command = ProtectionShellGlue.BuildUnprotectSheetCommand(
                    sheetId,
                    state.HasPassword ? passwordBox.Text : null);
                var result = _session.ExecuteReviewCommand(command);
                if (!result.Success)
                {
                    ShowWarning(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotUnprotectSheet"));
                    return;
                }

                RefreshShell(UiText.Get("ShellLoc_UnprotectedSheet"));
                dialog.Close();
            };
        }
        else
        {
            var permissionBoxes = new List<(SheetProtectionPermission Permission, CheckBox Box)>();
            var checklist = new StackPanel { Spacing = 4 };
            var index = 0;
            foreach (var option in SheetProtectionOptions.All)
            {
                var box = new CheckBox
                {
                    Content = ProtectionShellGlue.DescribePermission(option.Permission),
                    IsChecked = option.DefaultEnabled,
                };
                AutomationProperties.SetAutomationId(box, $"ProtectSheetPermission{index}Box");
                permissionBoxes.Add((option.Permission, box));
                checklist.Children.Add(box);
                index++;
            }

            contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordOptionalLabel"), passwordBox));
            contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_ConfirmPasswordLabel"), confirmBox));
            contentChildren.Add(new TextBlock
            {
                Text = UiText.Get("ShellLoc_AllowAllUsersToLabel"),
                Foreground = HeaderForeground,
                Margin = new Thickness(0, 6, 0, 0),
            });
            contentChildren.Add(new ScrollViewer { Content = checklist, MaxHeight = 280 });

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;

                var enabled = permissionBoxes
                    .Where(p => p.Box.IsChecked == true)
                    .Select(p => p.Permission)
                    .ToList();

                var options = ProtectSheetOptions.FromCorePermissions(
                    enabled,
                    passwordBox.Text,
                    confirmBox.Text);

                var validation = options.ValidatePassword();
                if (!validation.IsValid)
                {
                    ShowWarning(UiText.Get("ShellLoc_PasswordsDoNotMatch"));
                    return;
                }

                var command = ProtectionShellGlue.BuildProtectSheetCommand(sheetId, options);
                var result = _session.ExecuteReviewCommand(command);
                if (!result.Success)
                {
                    ShowWarning(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotProtectSheet"));
                    return;
                }

                RefreshShell(UiText.Get("ShellLoc_ProtectedSheet"));
                dialog.Close();
            };
        }

        contentChildren.Add(warningText);

        dialog.Content = ProtectionDialogLayout(contentChildren, cancelButton, okButton);
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// The Protect Workbook dialog. For an unprotected workbook it offers the Structure and Windows toggles plus
    /// a password (with confirmation); OK validates the confirm-match and runs the Core
    /// <see cref="FreeX.Core.Commands.ProtectWorkbookCommand"/> (window protection is carried for fidelity but is
    /// not persisted by Core). For an already-protected workbook it acts as an unprotect prompt: it collects the
    /// password only (when one is stored) and runs the Core <see cref="FreeX.Core.Commands.UnprotectWorkbookCommand"/>.
    /// </summary>
    private async Task ShowProtectWorkbookDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var state = ProtectionShellGlue.ProjectWorkbook(_session.Workbook);

        var dialog = new Window
        {
            Title = state.IsStructureProtected ? UiText.Get("ShellLoc_UnprotectWorkbookTitle") : UiText.Get("ShellLoc_ProtectWorkbookTitle"),
            Width = 380,
            Height = state.IsStructureProtected ? 200 : 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ProtectWorkbookDialog");

        var passwordBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        AutomationProperties.SetAutomationId(passwordBox, "ProtectWorkbookPasswordBox");
        var confirmBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        AutomationProperties.SetAutomationId(confirmBox, "ProtectWorkbookConfirmBox");

        var structureBox = new CheckBox { Content = UiText.Get("ShellLoc_StructureCheckbox"), IsChecked = true };
        AutomationProperties.SetAutomationId(structureBox, "ProtectWorkbookStructureBox");
        var windowsBox = new CheckBox { Content = UiText.Get("ShellLoc_WindowsCheckbox") };
        AutomationProperties.SetAutomationId(windowsBox, "ProtectWorkbookWindowsBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "ProtectWorkbookWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "ProtectWorkbookOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "ProtectWorkbookCancelButton");
        cancelButton.Click += (_, _) => dialog.Close();

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        var contentChildren = new Controls();

        if (state.IsStructureProtected)
        {
            contentChildren.Add(new TextBlock
            {
                Text = state.HasPassword
                    ? UiText.Get("ShellLoc_WorkbookProtectedEnterPassword")
                    : UiText.Get("ShellLoc_WorkbookProtectedClickOk"),
                Foreground = HeaderForeground,
                TextWrapping = TextWrapping.Wrap,
            });

            if (state.HasPassword)
                contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordLabel"), passwordBox));

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;
                var command = ProtectionShellGlue.BuildUnprotectWorkbookCommand(
                    state.HasPassword ? passwordBox.Text : null);
                var result = _session.ExecuteReviewCommand(command);
                if (!result.Success)
                {
                    ShowWarning(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotUnprotectWorkbook"));
                    return;
                }

                RefreshShell(UiText.Get("ShellLoc_UnprotectedWorkbook"));
                dialog.Close();
            };
        }
        else
        {
            contentChildren.Add(new TextBlock
            {
                Text = UiText.Get("ShellLoc_ProtectWorkbookForLabel"),
                Foreground = HeaderForeground,
            });
            contentChildren.Add(structureBox);
            contentChildren.Add(windowsBox);
            contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordOptionalLabel"), passwordBox));
            contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_ConfirmPasswordLabel"), confirmBox));

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;

                if (structureBox.IsChecked != true && windowsBox.IsChecked != true)
                {
                    ShowWarning(UiText.Get("ShellLoc_SelectStructureOrWindows"));
                    return;
                }

                var options = new ProtectWorkbookOptions
                {
                    ProtectStructure = structureBox.IsChecked == true,
                    ProtectWindows = windowsBox.IsChecked == true,
                    Password = passwordBox.Text,
                    PasswordConfirmation = confirmBox.Text,
                };

                var validation = options.ValidatePassword();
                if (!validation.IsValid)
                {
                    ShowWarning(UiText.Get("ShellLoc_PasswordsDoNotMatch"));
                    return;
                }

                var command = ProtectionShellGlue.BuildProtectWorkbookCommand(options);
                var result = _session.ExecuteReviewCommand(command);
                if (!result.Success)
                {
                    ShowWarning(result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotProtectWorkbook"));
                    return;
                }

                RefreshShell(UiText.Get("ShellLoc_ProtectedWorkbook"));
                dialog.Close();
            };
        }

        contentChildren.Add(warningText);

        dialog.Content = ProtectionDialogLayout(contentChildren, cancelButton, okButton);
        await dialog.ShowDialog(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StackPanel ProtectionLabeledField(string label, Control field)
    {
        field.Margin = new Thickness(0, 2, 0, 0);
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = label, Foreground = HeaderForeground },
                field,
            },
        };
    }

    private static DockPanel ProtectionDialogLayout(Controls bodyChildren, Button cancelButton, Button okButton)
    {
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var body = new StackPanel { Spacing = 8 };
        foreach (var child in bodyChildren)
            body.Children.Add(child);

        return new DockPanel
        {
            Margin = new Thickness(16),
            Children = { buttonRow, body },
        };
    }
}
