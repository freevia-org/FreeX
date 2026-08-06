using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class ProtectionDialogTests
{
    [Fact]
    public void ProtectionDialogs_ExposeKeyboardAccessKeys()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_RangeLabel\"),");
        UiText.Get("AllowEditRange_RangeLabel").Should().Be("_Range:");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().NotContain("rangePicker");
        source.Should().Contain("private void RangePicker_Click");
        source.Should().Contain("RangeSelectionRequest = CreateRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("FocusRangeInput();");
        var pickerHandlerSource = source[
            source.IndexOf("private void RangePicker_Click", StringComparison.Ordinal)..
            source.IndexOf("public static AllowEditRangeSelectionRequest", StringComparison.Ordinal)];
        pickerHandlerSource.Should().Contain("FocusRangeInput();");
        source.Should().Contain("UiText.Get(\"AllowEditRange_Example\")");
    }

    [Fact]
    public void ProtectionDialogsOpenedFromKeyboard_FocusInitialEntryFields()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_passwordBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_passwordBox);");
        source.Should().Contain("_confirmationBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_confirmationBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void ProtectionDialogsInvalidInputs_RefocusInvalidEntryFields()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("FocusConfirmationInput();");
        source.Should().Contain("private void FocusConfirmationInput()");
        source.Should().Contain("_confirmationBox.Focus();");
        source.Should().Contain("_confirmationBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_confirmationBox);");
        source.Should().Contain("FocusRangeInput();");
        source.Should().Contain("private void FocusRangeInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void ProtectionPasswordFields_ExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var protectDialog = new PasswordProtectionDialog("Protect Sheet", "_Password (optional):");
            var confirmDialog = new ConfirmPasswordDialog("secret");
            try
            {
                var passwordBox = DialogSourceTestSupport.GetPrivateField<PasswordBox>(protectDialog, "_passwordBox");
                AutomationProperties.GetName(passwordBox).Should().Be("Protection password");
                AutomationProperties.GetAutomationId(passwordBox).Should().Be("ProtectionPasswordBox");
                AutomationProperties.GetHelpText(passwordBox).Should().Be("Enter the optional password for protecting the sheet or workbook.");

                var confirmationBox = DialogSourceTestSupport.GetPrivateField<PasswordBox>(confirmDialog, "_confirmationBox");
                AutomationProperties.GetName(confirmationBox).Should().Be("Confirm protection password");
                AutomationProperties.GetAutomationId(confirmationBox).Should().Be("ConfirmProtectionPasswordBox");
                AutomationProperties.GetHelpText(confirmationBox).Should().Be("Reenter the password to confirm protection.");
            }
            finally
            {
                protectDialog.Close();
                confirmDialog.Close();
            }
        });
    }

    [Fact]
    public void ProtectSheetDialog_ExposesPermissionChecklistAndFollowUpConfirmation()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("Header = UiText.Get(\"Protection_AllowAllUsersOfThisWorksheetTo\")");
        source.Should().Contain("Header = UiText.Get(\"Protection_Password\")");
        source.Should().Contain("UiText.Get(\"Protection_ProtectWorksheetContents\")");
        source.Should().Contain("UiText.Get(\"Protection_CautionLostOrForgottenPasswordsCannotBeRecovered\")");
        source.Should().Contain("ConfirmPasswordDialog");
        source.Should().Contain("UiText.Get(\"Protection_ConfirmPassword\")");
        source.Should().NotContain("_Confirm password:");
        source.Should().Contain("foreach (var option in SheetProtectionOptions.All)");
        source.Should().Contain("Content = UiText.Get(option.LabelKey)");
        var protectionOptionsSource = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Presentation",
            "Protection",
            "SheetProtectionOptions.cs");
        protectionOptionsSource.Should().Contain("\"Protection_PermissionSelectLockedCells\"");
        protectionOptionsSource.Should().Contain("\"Protection_PermissionEditScenarios\"");
        source.Should().Contain("UiText.Get(\"Protection_ChooseWhichProtectedSheetActionsRemainAvailable\")");
        source.Should().NotContain("current enforcement is limited");
    }

    [Fact]
    public void ProtectionPasswordDialogs_UseContentHeightSizingInsteadOfFixedWindowHeights()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("width: isProtectSheet ? ProtectionDialogPlanner.ProtectSheetWidth : ProtectionDialogPlanner.ProtectWorkbookCaptureWidth");
        source.Should().Contain("minHeight: isProtectSheet ? ProtectionDialogPlanner.ProtectSheetHeight : ProtectionDialogPlanner.ProtectWorkbookCaptureHeight");
        source.Should().Contain("DialogSizing.ApplyContentHeight(this, width: 360, minHeight: 180);");
        source.Should().NotContain("Height = isProtectSheet ? 540 : 250;");
        source.Should().NotContain("Height = 170;");
    }

    [Fact]
    public void UnprotectSheetDialogLayout_KeepsPasswordAndActionsVisibleAtDefaultSize()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PasswordProtectionDialog(
                UiText.Get("Protection_UnprotectSheetTitle"),
                UiText.Get("Protection_Password2"));
            dialog.Show();
            try
            {
                dialog.UpdateLayout();

                dialog.SizeToContent.Should().Be(SizeToContent.Height);
                dialog.MinWidth.Should().Be(380);
                dialog.MinHeight.Should().Be(ProtectionDialogPlanner.ProtectWorkbookCaptureHeight);
                var root = dialog.Content.Should().BeAssignableTo<FrameworkElement>().Subject;
                var passwordBox = DialogSourceTestSupport.GetPrivateField<PasswordBox>(dialog, "_passwordBox");
                var buttons = WpfTestTree.FindVisualDescendants<Button>(dialog)
                    .Where(button => button.IsDefault || button.IsCancel)
                    .ToArray();
                buttons.Should().HaveCount(2);

                var passwordBounds = BoundsRelativeTo(root, passwordBox);
                var actionTop = buttons.Min(button => BoundsRelativeTo(root, button).Top);
                actionTop.Should().BeGreaterThan(passwordBounds.Bottom);

                foreach (var button in buttons)
                {
                    button.ActualWidth.Should().BeGreaterThanOrEqualTo(button.MinWidth);
                    button.ActualHeight.Should().BeGreaterThan(0);
                    AssertInside(root, button);
                }

                AssertInside(root, passwordBox);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
