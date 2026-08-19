using System.Linq;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Protect Sheet and Protect Workbook dialogs for the Avalonia/macOS shell (Review menu). When the target is
/// unprotected the dialog collects a password (with confirmation) and — for a sheet — the allowed-action
/// checklist seeded from <see cref="SheetProtectionOptions"/>; OK validates the confirm-match through
/// <see cref="ProtectionPassword"/> and runs the Core protect command. When the target is already protected
/// the action unprotects instead, prompting for the password when one is stored. The portable dialog model,
/// validation, projection, command composition, and outcomes come from
/// <see cref="ProtectionWorkflowSession"/>; commands run through the shared workbook session path.
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle ProtectionDialogChromeStyle => new(FormulaBarFontFamily);

    // ── Review ▸ Protect menu entry points ─────────────────────────────────────
    private void ProtectSheet() => RunGuarded(ShowProtectSheetDialogAsync);

    private void ProtectWorkbook() => RunGuarded(ShowProtectWorkbookDialogAsync);

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

        var sheet = _session.ActiveSheet;
        var state = ProtectionSession.ProjectSheet(sheet);

        var dialog = new Window
        {
            Title = state.IsProtected ? UiText.Get("ShellLoc_UnprotectSheetTitle") : UiText.Get("ShellLoc_ProtectSheetTitle"),
            Width = state.IsProtected ? ProtectionDialogPlanner.UnprotectSheetWidth : ProtectionDialogPlanner.ProtectSheetWidth,
            Height = state.IsProtected ? ProtectionDialogPlanner.UnprotectSheetHeight : ProtectionDialogPlanner.ProtectSheetHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ProtectSheetDialog");

        var passwordBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        ApplyProtectTextBoxChrome(passwordBox);
        AutomationProperties.SetAutomationId(passwordBox, "ProtectSheetPasswordBox");
        var confirmBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        ApplyProtectTextBoxChrome(confirmBox);
        AutomationProperties.SetAutomationId(confirmBox, "ProtectSheetConfirmBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "ProtectSheetWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyProtectButtonChrome(okButton, 84, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "ProtectSheetOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyProtectButtonChrome(cancelButton, 84);
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
            var unprotectPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = state.HasPassword
                            ? UiText.Get("ShellLoc_SheetProtectedEnterPassword")
                            : UiText.Get("ShellLoc_SheetProtectedClickOk"),
                        Foreground = HeaderForeground,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                    },
                },
            };

            if (state.HasPassword)
            {
                unprotectPanel.Children.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordLabel"), passwordBox));
                unprotectPanel.Children.Add(new TextBlock
                {
                    Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                    Foreground = Brush(110, 110, 110),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    FontFamily = FormulaBarFontFamily,
                });
            }

            contentChildren.Add(ProtectionGroupBox(UiText.Get("ShellLoc_PasswordGroupHeader"), unprotectPanel));

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;
                var options = state.Options with
                {
                    Password = state.HasPassword ? passwordBox.Text : null,
                };
                var outcome = ProtectionSession.ExecuteSheet(sheet, options);
                if (!outcome.Success)
                {
                    ShowWarning(outcome.ErrorMessage ?? UiText.Get(outcome.ErrorResourceKey!));
                    return;
                }

                RefreshShell(UiText.Get(outcome.SuccessStatusResourceKey));
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
                    Content = UiText.Get(option.LabelKey),
                    IsChecked = option.DefaultEnabled,
                };
                ApplyProtectCheckBoxChrome(box);
                AutomationProperties.SetAutomationId(box, $"ProtectSheetPermission{index}Box");
                permissionBoxes.Add((option.Permission, box));
                checklist.Children.Add(box);
                index++;
            }

            contentChildren.Add(new TextBlock
            {
                Text = UiText.Get("ShellLoc_ProtectWorksheetContentsHeader"),
                Foreground = HeaderForeground,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            });

            var passwordPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    ProtectionLabeledField(UiText.Get("ShellLoc_PasswordOptionalLabel"), passwordBox),
                    ProtectionLabeledField(UiText.Get("ShellLoc_ConfirmPasswordLabel"), confirmBox),
                    new TextBlock
                    {
                        Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                        Foreground = Brush(110, 110, 110),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        FontFamily = FormulaBarFontFamily,
                    },
                },
            };
            contentChildren.Add(ProtectionGroupBox(UiText.Get("ShellLoc_PasswordGroupHeader"), passwordPanel));
            contentChildren.Add(ProtectionGroupBox(
                UiText.Get("ShellLoc_AllowAllUsersToLabel"),
                new ScrollViewer { Content = checklist, MaxHeight = 280 }));

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

                var outcome = ProtectionSession.ExecuteSheet(sheet, options);
                if (!outcome.Success)
                {
                    ShowWarning(outcome.ErrorMessage ?? UiText.Get(outcome.ErrorResourceKey!));
                    return;
                }

                RefreshShell(UiText.Get(outcome.SuccessStatusResourceKey));
                dialog.Close();
            };
        }

        contentChildren.Add(warningText);

        dialog.Content = ProtectionDialogLayout(contentChildren, cancelButton, okButton);
        AttachProtectionDialogInitialFocus(dialog, passwordBox);
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

        var state = ProtectionSession.ProjectWorkbook();

        var dialog = new Window
        {
            Title = state.IsStructureProtected ? UiText.Get("ShellLoc_UnprotectWorkbookTitle") : UiText.Get("ShellLoc_ProtectWorkbookTitle"),
            Width = ProtectionDialogPlanner.ProtectWorkbookCaptureWidth,
            Height = state.IsStructureProtected ? ProtectionDialogPlanner.ProtectWorkbookCaptureHeight : 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ProtectWorkbookDialog");

        var passwordBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        ApplyProtectTextBoxChrome(passwordBox);
        AutomationProperties.SetAutomationId(passwordBox, "ProtectWorkbookPasswordBox");
        var confirmBox = new TextBox { PasswordChar = '•', MinWidth = 200 };
        ApplyProtectTextBoxChrome(confirmBox);
        AutomationProperties.SetAutomationId(confirmBox, "ProtectWorkbookConfirmBox");

        var structureBox = new CheckBox { Content = UiText.Get("ShellLoc_StructureCheckbox"), IsChecked = true };
        ApplyProtectCheckBoxChrome(structureBox);
        AutomationProperties.SetAutomationId(structureBox, "ProtectWorkbookStructureBox");
        var windowsBox = new CheckBox { Content = UiText.Get("ShellLoc_WindowsCheckbox") };
        ApplyProtectCheckBoxChrome(windowsBox);
        AutomationProperties.SetAutomationId(windowsBox, "ProtectWorkbookWindowsBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "ProtectWorkbookWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyProtectButtonChrome(okButton, 84, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "ProtectWorkbookOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyProtectButtonChrome(cancelButton, 84);
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
            var unprotectPanel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = state.HasPassword
                            ? UiText.Get("ShellLoc_WorkbookProtectedEnterPassword")
                            : UiText.Get("ShellLoc_WorkbookProtectedClickOk"),
                        Foreground = HeaderForeground,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                    },
                },
            };

            if (state.HasPassword)
            {
                unprotectPanel.Children.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordLabel"), passwordBox));
                unprotectPanel.Children.Add(new TextBlock
                {
                    Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                    Foreground = Brush(110, 110, 110),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 11,
                    FontFamily = FormulaBarFontFamily,
                });
            }

            contentChildren.Add(ProtectionGroupBox(UiText.Get("ShellLoc_PasswordGroupHeader"), unprotectPanel));

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;
                var options = state.Options with
                {
                    Password = state.HasPassword ? passwordBox.Text : null,
                };
                var outcome = ProtectionSession.ExecuteWorkbook(options);
                if (!outcome.Success)
                {
                    ShowWarning(outcome.ErrorMessage ?? UiText.Get(outcome.ErrorResourceKey!));
                    return;
                }

                RefreshShell(UiText.Get(outcome.SuccessStatusResourceKey));
                dialog.Close();
            };
        }
        else
        {
            contentChildren.Add(new TextBlock
            {
                Text = UiText.Get("ShellLoc_ProtectWorkbookForLabel"),
                Foreground = HeaderForeground,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            });
            contentChildren.Add(structureBox);
            contentChildren.Add(windowsBox);
            contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordOptionalLabel"), passwordBox));
            contentChildren.Add(ProtectionLabeledField(UiText.Get("ShellLoc_ConfirmPasswordLabel"), confirmBox));

            okButton.Click += (_, _) =>
            {
                warningText.IsVisible = false;

                var options = new ProtectWorkbookOptions
                {
                    ProtectStructure = structureBox.IsChecked == true,
                    ProtectWindows = windowsBox.IsChecked == true,
                    Password = passwordBox.Text,
                    PasswordConfirmation = confirmBox.Text,
                };

                var outcome = ProtectionSession.ExecuteWorkbook(options);
                if (!outcome.Success)
                {
                    ShowWarning(outcome.ErrorMessage ?? UiText.Get(outcome.ErrorResourceKey!));
                    return;
                }

                RefreshShell(UiText.Get(outcome.SuccessStatusResourceKey));
                dialog.Close();
            };
        }

        contentChildren.Add(warningText);

        dialog.Content = ProtectionDialogLayout(contentChildren, cancelButton, okButton);
        AttachProtectionDialogInitialFocus(dialog, passwordBox);
        await dialog.ShowDialog(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AttachProtectionDialogInitialFocus(Window dialog, TextBox passwordBox)
    {
        // Match WPF PasswordProtectionDialog.Loaded: route keyboard input to the password field
        // before the shared owned-dialog lifecycle probes Tab and Escape.
        dialog.Opened += (_, _) =>
        {
            passwordBox.Focus();
            passwordBox.SelectAll();
        };
    }

    /// <summary>
    /// A bordered group with a header label — the Avalonia equivalent of the WPF <c>GroupBox</c> used by the
    /// Protect dialogs to frame the Password area and the "Allow all users of this worksheet to:" checklist.
    /// Matches the Windows reference, which draws a thin grey border around each section with a caption.
    /// </summary>
    private static Border ProtectionGroupBox(string header, Control content)
    {
        content.Margin = new Thickness(8, 6, 8, 8);
        return new Border
        {
            BorderBrush = FormulaBarControlBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    new TextBlock
                    {
                        Text = StripDisplayMnemonic(header),
                        Foreground = HeaderForeground,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        Margin = new Thickness(8, 4, 8, 0),
                    },
                    content,
                },
            },
        };
    }

    private static StackPanel ProtectionLabeledField(string label, Control field)
    {
        field.Margin = new Thickness(0, 2, 0, 0);
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = StripDisplayMnemonic(label), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily },
                field,
            },
        };
    }

    private static DockPanel ProtectionDialogLayout(Controls bodyChildren, Button cancelButton, Button okButton)
    {
        // WPF order: [OK] [Cancel] — primary button on the left
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 10, 0, 0));
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

    // ── Visual chrome helpers (Protection dialogs) ───────────────────────────

    /// <summary>
    /// Applies standard Protection-dialog button chrome (Height=24, FontSize=12, white background,
    /// grey/blue border). <paramref name="minWidth"/> sets MinWidth; <paramref name="isDefault"/> uses
    /// blue border for the default/OK button.
    /// </summary>
    private static void ApplyProtectButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, ProtectionDialogChromeStyle, minWidth, isDefault);

    /// <summary>
    /// Applies standard Protection-dialog text-box chrome (Height=24, Padding=(4,1), FontSize=12, grey border).
    /// </summary>
    private static void ApplyProtectTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ProtectionDialogChromeStyle);

    /// <summary>
    /// Applies standard Protection-dialog check-box chrome (MinHeight=20, MaxHeight=20, FontSize=12).
    /// </summary>
    private static void ApplyProtectCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, ProtectionDialogChromeStyle);
    }
}
