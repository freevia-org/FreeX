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

public sealed class ProtectionDialogTests
{
    [Fact]
    public void SheetProtectionDialogResult_ForProtectedSheetRequestsUnprotect()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;

        var result = ProtectionDialogPlanner.CreateSheetResult(sheet, password: "ignored");

        result.Mode.Should().Be(ProtectionDialogMode.Unprotect);
        result.Password.Should().BeNull();
    }

    [Fact]
    public void SheetProtectionDialogResult_ForUnprotectedSheetKeepsPassword()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = ProtectionDialogPlanner.CreateSheetResult(sheet, password: "secret");

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().Be("secret");
        result.SelectedSheetPermissions.Should().Equal(["Select locked cells", "Select unlocked cells"]);
    }

    [Fact]
    public void SheetProtectionDialogResult_ForUnprotectedSheetKeepsSelectedPermissions()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet,
            password: "secret",
            selectedSheetPermissions: ["Select unlocked cells", "Sort"]);

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().Be("secret");
        result.SelectedSheetPermissions.Should().Equal(["Select unlocked cells", "Sort"]);
    }

    [Fact]
    public void SheetProtectionDialogResult_RequiresMatchingPasswordConfirmation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = ProtectionDialogPlanner.CreateSheetResult(sheet, password: "secret", confirmation: "Secret");

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().BeNull();
    }

    [Fact]
    public void DefaultSheetPermissions_MatchExcelProtectSheetChecklist()
    {
        ProtectionDialogPlanner.GetDefaultSheetPermissions()
            .Should()
            .Equal([
                "Select locked cells",
                "Select unlocked cells",
                "Format cells",
                "Format columns",
                "Format rows",
                "Insert columns",
                "Insert rows",
                "Insert hyperlinks",
                "Delete columns",
                "Delete rows",
                "Sort",
                "Use AutoFilter",
                "Use PivotTable reports",
                "Edit objects",
                "Edit scenarios"]);
    }

    [Fact]
    public void WorkbookProtectionDialogResult_ForProtectedWorkbookRequestsUnprotect()
    {
        var workbook = new Workbook("test") { IsStructureProtected = true };

        var result = ProtectionDialogPlanner.CreateWorkbookResult(workbook, password: "ignored");

        result.Mode.Should().Be(ProtectionDialogMode.Unprotect);
        result.Password.Should().BeNull();
    }

    [Fact]
    public void TryParseAllowEditRange_AcceptsRangeOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        ProtectionDialogPlanner.TryParseAllowEditRange("A1:B2", sheetId, out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheetId, 1, 1));
        range.End.Should().Be(new CellAddress(sheetId, 2, 2));
    }

    [Fact]
    public void TryParseAllowEditRange_RejectsInvalidRangeThroughSharedParser()
    {
        ProtectionDialogPlanner.TryParseAllowEditRange("A1:B2:C3", SheetId.New(), out _).Should().BeFalse();
    }

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
    public void AllowEditRangeDialog_ExposesExcelLikeRangeManagerActions()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("public enum AllowEditRangeDialogAction");
        source.Should().Contain("public sealed record AllowEditRangeDialogResult");
        source.Should().Contain("private readonly ListBox _existingRangesBox");
        source.Should().Contain("new Label { Content = UiText.Get(\"AllowEditRange_ExistingRangesLabel\"), Target = _existingRangesBox");
        source.Should().NotContain("Header = \"Ranges unlocked by password\"");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_DeleteButton\")");
        source.Should().Contain("Content = UiText.Get(\"AllowEditRange_ClearAllButton\")");
        source.Should().Contain("private void DeleteSelectedRange_Click");
        source.Should().Contain("private void ClearAllRanges_Click");
        source.Should().Contain("CreateRemoveResult");
        source.Should().Contain("CreateClearResult");
    }

    [Fact]
    public void AllowEditRangeDialog_ExistingRangesListExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_existingRangesBox, UiText.Get(\"AllowEditRange_ExistingRangesAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_existingRangesBox, \"AllowEditRangeExistingRangesList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_existingRangesBox, UiText.Get(\"AllowEditRange_ExistingRangesHelpText\"));");
        UiText.Get("AllowEditRange_ExistingRangesAutomationName").Should().Be("Ranges unlocked by password");
    }

    [Fact]
    public void AllowEditRangeDialog_RangeEditorExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(\"AllowEditRange_RangeAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rangeBox, \"AllowEditRangeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(\"AllowEditRange_RangeHelpText\"));");
        UiText.Get("AllowEditRange_RangeAutomationName").Should().Be("Editable range");
    }

    [Fact]
    public void AllowEditRangeDialog_ActionButtonsExposeAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_deleteRangeButton, UiText.Get(\"AllowEditRange_DeleteAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_deleteRangeButton, \"AllowEditRangeDeleteButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_deleteRangeButton, UiText.Get(\"AllowEditRange_DeleteHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(_clearRangesButton, UiText.Get(\"AllowEditRange_ClearAllAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_clearRangesButton, \"AllowEditRangeClearAllButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_clearRangesButton, UiText.Get(\"AllowEditRange_ClearAllHelpText\"));");
        source.Should().Contain("AutomationProperties.SetName(rangePicker, UiText.Get(\"AllowEditRange_PickerAutomationName\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(rangePicker, \"AllowEditRangePickerButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(");
        source.Should().Contain("UiText.Get(\"AllowEditRange_PickerHelpText\"));");
    }

    [Fact]
    public void AllowEditRangesWorkflow_ExecutesAddRemoveAndClearCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("new AllowEditRangeDialog(");
        source.Should().Contain("AllowEditRangeDialogAction.Add");
        source.Should().Contain("new AllowEditRangeCommand(_currentSheetId, range)");
        source.Should().Contain("AllowEditRangeDialogAction.Remove");
        source.Should().Contain("new RemoveAllowEditRangeCommand(_currentSheetId, range)");
        source.Should().Contain("AllowEditRangeDialogAction.Clear");
        source.Should().Contain("new ClearAllowEditRangesCommand(_currentSheetId)");
    }

    [Fact]
    public void AllowEditRangesWorkflow_WiresRangePickerToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("request => ApplyAllowEditRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplyAllowEditRangeSelection(");
        source.Should().Contain("AllowEditRangeSelectionRequest request");
        source.Should().Contain("dialog.ApplyRangeSelection(FormatRangeReference(selectedRange.Start, selectedRange.End));");
        source.Should().Contain("dialog.Hide();");
        source.Should().Contain("dialog.Show();");
        source.Should().Contain("dialog.Activate();");
    }

    [Fact]
    public void AllowEditRangeDialog_CreateResults_CaptureRequestedAction()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));

        AllowEditRangeDialog.CreateAddResult(range)
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Add, range));
        AllowEditRangeDialog.CreateRemoveResult(range)
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Remove, range));
        AllowEditRangeDialog.CreateClearResult()
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Clear, null));
    }

    [Fact]
    public void AllowEditRangeDialogPlanner_BuildsRangeListAndButtonState()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));

        AllowEditRangeDialogPlanner.BuildExistingRangeItems([range]).Should().Equal(range.ToString());
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 0, hasSelectedRange: false)
            .Should()
            .Be(new AllowEditRangeButtonState(false, false));
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 1, hasSelectedRange: false)
            .Should()
            .Be(new AllowEditRangeButtonState(false, true));
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 1, hasSelectedRange: true)
            .Should()
            .Be(new AllowEditRangeButtonState(true, true));
    }

    [Fact]
    public void AllowEditRangeDialogExistingRangesList_DoubleClickRemovesSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));
            var dialog = new AllowEditRangeDialog(sheetId, "C3:D4", [range]);
            var existingRangesBox = GetPrivateField<ListBox>(dialog, "_existingRangesBox");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                existingRangesBox.SelectedIndex = 0;
                existingRangesBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                });

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(AllowEditRangeDialog.CreateRemoveResult(range));
        });
    }

    [Fact]
    public void AllowEditRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        AllowEditRangeDialog.CreateRangeSelectionRequest(" $A$1:$C$10 ")
            .Should()
            .Be(new AllowEditRangeSelectionRequest("$A$1:$C$10", CollapseDialog: true));
    }

    [Fact]
    public void AllowEditRangePicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<AllowEditRangeSelectionRequest>();
            var dialog = new AllowEditRangeDialog(SheetId.New(), " $A$1:$C$10 ", requests.Add);
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "RangePicker_Click");

                requests.Should().Equal(new AllowEditRangeSelectionRequest("$A$1:$C$10", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
                GetPrivateField<TextBox>(dialog, "_rangeBox").SelectionLength.Should().Be("$A$1:$C$10".Length + 2);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AllowEditRangeDialogApplyRangeSelection_UpdatesRangeBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AllowEditRangeDialog(SheetId.New(), "$A$1:$C$10");
            try
            {
                dialog.ApplyRangeSelection("$B$2:$D$8");

                var rangeBox = GetPrivateField<TextBox>(dialog, "_rangeBox");
                rangeBox.Text.Should().Be("$B$2:$D$8");
                rangeBox.SelectionLength.Should().Be("$B$2:$D$8".Length);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ProtectSheetDialog_ExposesPermissionChecklistAndFollowUpConfirmation()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("Allow all users of this worksheet to:");
        source.Should().Contain("Header = \"Password\"");
        source.Should().Contain("Protect worksheet and contents of locked cells");
        source.Should().Contain("Caution: lost or forgotten passwords cannot be recovered.");
        source.Should().Contain("ConfirmPasswordDialog");
        source.Should().Contain("Confirm Password");
        source.Should().NotContain("_Confirm password:");
        source.Should().Contain("Select locked cells");
        source.Should().Contain("Edit scenarios");
        source.Should().Contain("Choose which protected-sheet actions remain available.");
        source.Should().NotContain("current enforcement is limited");
    }

    private static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static void InvokePrivate(AllowEditRangeDialog dialog, string methodName)
    {
        var method = typeof(AllowEditRangeDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
    }

    private static string ReadProtectionDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ProtectionDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AllowEditRangeDialogPlanner.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ProtectionDialogPlanner.cs")));
}
