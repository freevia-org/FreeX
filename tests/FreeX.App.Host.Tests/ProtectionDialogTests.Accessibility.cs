using System.Reflection;
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
        source.Should().Contain("new Label { Content = UiText.Get(\"AllowEditRange_RangeLabel\")");
        UiText.Get("AllowEditRange_RangeLabel").Should().Be("_Range:");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("Header = UiText.Get(\"AllowEditRange_RangeGroupHeader\")");
        source.Should().Contain("Content = \"...\"");
        source.Should().Contain("ToolTip = UiText.Get(\"AllowEditRange_PickerToolTip\")");
        source.Should().Contain("AutomationProperties.SetName(rangePicker, UiText.Get(\"AllowEditRange_PickerAutomationName\"))");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().Contain("rangePicker.Click += RangePicker_Click");
        source.Should().Contain("private void RangePicker_Click");
        source.Should().Contain("RangeSelectionRequest = CreateRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("FocusRangeInput();");
        var pickerHandlerSource = source[
            source.IndexOf("private void RangePicker_Click", StringComparison.Ordinal)..
            source.IndexOf("public static AllowEditRangeSelectionRequest", StringComparison.Ordinal)];
        pickerHandlerSource.Should().Contain("FocusRangeInput();");
        source.Should().Contain("UiText.Get(\"AllowEditRange_ExampleText\")");
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
                var passwordBox = GetPrivateField<PasswordBox>(protectDialog, "_passwordBox");
                AutomationProperties.GetName(passwordBox).Should().Be("Protection password");
                AutomationProperties.GetAutomationId(passwordBox).Should().Be("ProtectionPasswordBox");
                AutomationProperties.GetHelpText(passwordBox).Should().Be("Enter the optional password for protecting the sheet or workbook.");

                var confirmationBox = GetPrivateField<PasswordBox>(confirmDialog, "_confirmationBox");
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
        source.Should().Contain("UiText.Get(\"Protection_PermissionSelectLockedCells\")");
        source.Should().Contain("UiText.Get(\"Protection_PermissionEditScenarios\")");
        source.Should().Contain("UiText.Get(\"Protection_ChooseWhichProtectedSheetActionsRemainAvailable\")");
        source.Should().NotContain("current enforcement is limited");
    }
}
