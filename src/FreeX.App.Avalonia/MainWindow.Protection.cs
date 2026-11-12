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

    // WPF PasswordProtectionDialog (ProtectionDialogs.cs) uses a default-template PasswordBox, which
    // renders 18px tall at the 12px dialog font; the buttons keep the shared control height.
    private const double ProtectWpfPasswordBoxHeight = 18;
    // WPF ProtectionDialogs.AddSheetPermissionChecklist: twelve whole permission rows show in the
    // permission viewport; each row is a 16px check box with a 4px bottom margin.
    private const int ProtectSheetWpfVisiblePermissionRows = 12;
    private const double ProtectSheetPermissionRowPitch = 20;

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
            // WPF PasswordProtectionDialog: DialogSizing.ApplyContentHeight(width, minHeight) -- a
            // NoResize window of fixed width whose height follows its content, never below the *outer*
            // minimum (540 for Protect Sheet, 250 for the password-only prompt). Avalonia sizes the
            // client area, so the measured Windows frame comes off the width and the minimum.
            Width = (state.IsProtected ? ProtectionDialogPlanner.ProtectWorkbookCaptureWidth : ProtectionDialogPlanner.ProtectSheetWidth)
                - WpfNoResizeFrameWidth,
            // Height seeds the size for hosts that do not apply SizeToContent (headless capture).
            Height = (state.IsProtected ? ProtectionDialogPlanner.ProtectWorkbookCaptureHeight : ProtectionDialogPlanner.ProtectSheetHeight)
                - WpfNoResizeClientFrameHeight,
            MinHeight = (state.IsProtected ? ProtectionDialogPlanner.ProtectWorkbookCaptureHeight : ProtectionDialogPlanner.ProtectSheetHeight)
                - WpfNoResizeClientFrameHeight,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
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
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetAutomationId(warningText, "ProtectSheetWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 72 };
        ApplyProtectButtonChrome(okButton, 72, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "ProtectSheetOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 72 };
        ApplyProtectButtonChrome(cancelButton, 72);
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
                unprotectPanel.Children.Add(ProtectionLabeledField(UiText.Get("ShellLoc_PasswordLabel"), passwordBox, topMargin: 8));
                unprotectPanel.Children.Add(new TextBlock
                {
                    Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                    Foreground = Brush(109, 109, 109),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 8, 0, 0),
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
            // WPF: each permission check box carries Margin = (0, 0, 0, 4).
            var checklist = new StackPanel();
            var index = 0;
            foreach (var option in SheetProtectionOptions.All)
            {
                var box = new CheckBox
                {
                    Content = UiText.Get(option.LabelKey),
                    IsChecked = option.DefaultEnabled,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                ApplyProtectCheckBoxChrome(box);
                // WPF lists these as natural-height (16px) check boxes with a 4px bottom margin, a
                // 20px pitch; the shared 20px dialog check row made the pitch 24.
                box.MinHeight = 16;
                box.MaxHeight = 16;
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
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            });

            var passwordPanel = new StackPanel
            {
                Children =
                {
                    ProtectionLabeledField(UiText.Get("ShellLoc_PasswordOptionalLabel"), passwordBox),
                    ProtectionLabeledField(UiText.Get("ShellLoc_ConfirmPasswordLabel"), confirmBox, topMargin: 8),
                    new TextBlock
                    {
                        Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                        Foreground = Brush(109, 109, 109),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        FontFamily = FormulaBarFontFamily,
                        Margin = new Thickness(0, 8, 0, 0),
                    },
                },
            };
            contentChildren.Add(ProtectionGroupBox(UiText.Get("ShellLoc_PasswordGroupHeader"), passwordPanel));
            // WPF: the checklist ScrollViewer sits directly in its group, so the scrollbar track runs
            // to within the group frame's own inset; the group has no bottom margin. The WPF viewport
            // shows exactly twelve whole permission rows; at this row pitch that is the viewport height.
            var checklistGroup = ProtectionGroupBox(
                UiText.Get("ShellLoc_AllowAllUsersToLabel"),
                new ScrollViewer
                {
                    Content = checklist,
                    Height = ProtectSheetWpfVisiblePermissionRows * ProtectSheetPermissionRowPitch,
                },
                contentMargin: new Thickness(6, 1, 0, 0));
            checklistGroup.Margin = new Thickness(0);
            contentChildren.Add(checklistGroup);

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

        // SizeToContent sizes the window from its content and does not honour the window MinHeight,
        // so the content itself carries the WPF minimum (DialogSizing.ApplyContentHeight).
        dialog.Content = ProtectionDialogLayout(contentChildren, cancelButton, okButton, dialog.MinHeight);
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
            // WPF PasswordProtectionDialog: a 380 *outer* width, height follows the content with a 250
            // *outer* minimum (DialogSizing.ApplyContentHeight). Avalonia sizes the client area, so the
            // Windows caption/frame comes off both.
            Width = ProtectionDialogPlanner.ProtectWorkbookCaptureWidth - WpfNoResizeFrameWidth,
            Height = ProtectionDialogPlanner.ProtectWorkbookCaptureHeight - WpfNoResizeClientFrameHeight,
            MinHeight = ProtectionDialogPlanner.ProtectWorkbookCaptureHeight - WpfNoResizeClientFrameHeight,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
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
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetAutomationId(warningText, "ProtectWorkbookWarningText");

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 72 };
        ApplyProtectButtonChrome(okButton, 72, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "ProtectWorkbookOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 72 };
        ApplyProtectButtonChrome(cancelButton, 72);
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
            var unprotectPanel = new StackPanel();

            if (state.HasPassword)
            {
                // WPF TryConfirmUnprotectPassword opens PasswordProtectionDialog with the password
                // prompt as both the SemiBold heading above the group and the label inside it,
                // followed by the password box and the grey caution line.
                contentChildren.Add(new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("Protection_Password2")),
                    Foreground = HeaderForeground,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                });
                unprotectPanel.Children.Add(ProtectionLabeledField(UiText.Get("Protection_Password2"), passwordBox));
                unprotectPanel.Children.Add(new TextBlock
                {
                    Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                    Foreground = Brush(109, 109, 109),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }
            else
            {
                unprotectPanel.Children.Add(new TextBlock
                {
                    Text = UiText.Get("ShellLoc_WorkbookProtectedClickOk"),
                    Foreground = HeaderForeground,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
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
            // WPF PasswordProtectionDialog: a SemiBold heading 8px above the "Password" group, which
            // holds the labelled password box and the grey caution line. The Structure / Windows
            // toggles and the confirmation field are this shell's additions and follow the same rhythm.
            contentChildren.Add(new TextBlock
            {
                Text = UiText.Get("ShellLoc_ProtectWorkbookForLabel"),
                Foreground = HeaderForeground,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            });
            structureBox.Margin = new Thickness(0, 0, 0, 4);
            windowsBox.Margin = new Thickness(0, 0, 0, 8);
            contentChildren.Add(structureBox);
            contentChildren.Add(windowsBox);
            contentChildren.Add(ProtectionGroupBox(
                UiText.Get("ShellLoc_PasswordGroupHeader"),
                new StackPanel
                {
                    Children =
                    {
                        ProtectionLabeledField(UiText.Get("ShellLoc_PasswordOptionalLabel"), passwordBox),
                        ProtectionLabeledField(UiText.Get("ShellLoc_ConfirmPasswordLabel"), confirmBox, topMargin: 8),
                        new TextBlock
                        {
                            Text = UiText.Get("ShellLoc_CautionPasswordsCannotBeRecovered"),
                            Foreground = Brush(109, 109, 109),
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 12,
                            FontFamily = FormulaBarFontFamily,
                            Margin = new Thickness(0, 8, 0, 0),
                        },
                    },
                }));

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
    /// The WPF <c>GroupBox</c> the Protect dialogs use to frame the Password area and the "Allow all
    /// users of this worksheet to:" checklist: a thin grey frame with the caption on its top edge. WPF
    /// <c>PasswordProtectionDialog</c> is a plain <c>Window</c>, so the caption is normal ink, and the
    /// group sits 10px above whatever follows it. The content keeps the WPF inner panel margin.
    /// </summary>
    private static GroupBox ProtectionGroupBox(string header, Control content, Thickness? contentMargin = null)
    {
        content.Margin = contentMargin ?? new Thickness(8, 6, 8, 8);
        var groupBox = new GroupBox
        {
            Header = StripDisplayMnemonic(header),
            Content = content,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(0),
        };
        ApplyPlainWindowGroupBoxChrome(groupBox, ProtectionDialogChromeStyle);
        return groupBox;
    }

    private static StackPanel ProtectionLabeledField(string label, Control field, double topMargin = 0)
    {
        // WPF: Label { Margin = (0, 0, 0, 4) } directly above its password box.
        return new StackPanel
        {
            Margin = new Thickness(0, topMargin, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(label),
                    Foreground = HeaderForeground,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                field,
            },
        };
    }

    private static StackPanel ProtectionDialogLayout(Controls bodyChildren, Button cancelButton, Button okButton)
    {
        // WPF: root StackPanel { Margin = 12 } with the [OK] [Cancel] row (72px buttons, 8px apart)
        // stacked 12px below the content, so the fields precede the buttons in the tab order too.
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 12, 0, 0));

        var root = new StackPanel { Margin = new Thickness(12) };
        foreach (var child in bodyChildren)
            root.Children.Add(child);
        root.Children.Add(buttonRow);
        return root;
    }

    /// <summary>
    /// <see cref="ProtectionDialogLayout(Controls, Button, Button)"/> for a window whose client height
    /// may not drop below <paramref name="clientMinHeight"/>: the height the minimum adds collects
    /// below the buttons, as in WPF.
    /// </summary>
    private static Grid ProtectionDialogLayout(
        Controls bodyChildren,
        Button cancelButton,
        Button okButton,
        double clientMinHeight)
    {
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 12, 0, 0));
        const double rootMargin = 12;
        var root = new Grid
        {
            Margin = new Thickness(rootMargin),
            MinHeight = Math.Max(0, clientMinHeight - (2 * rootMargin)),
        };

        var rows = new List<Control>(bodyChildren) { buttonRow };
        for (var i = 0; i < rows.Count; i++)
        {
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(rows[i], i);
            root.Children.Add(rows[i]);
        }

        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        return root;
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
    /// Applies standard Protection-dialog text-box chrome (Height=18 like the WPF PasswordBox, FontSize=12, grey border).
    /// </summary>
    private static void ApplyProtectTextBoxChrome(TextBox textBox)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ProtectionDialogChromeStyle);
        textBox.Height = ProtectWpfPasswordBoxHeight;
        textBox.MinHeight = ProtectWpfPasswordBoxHeight;
        textBox.MaxHeight = ProtectWpfPasswordBoxHeight;
        textBox.Padding = new Thickness(3, 0);
    }

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
