using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

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
        source.Should().Contain("new Label { Content = \"_Range:\"");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("Header = \"Range\"");
        source.Should().Contain("Content = \"...\"");
        source.Should().Contain("ToolTip = \"Collapse dialog and select editable range\"");
        source.Should().Contain("AutomationProperties.SetName(rangePicker, \"Select editable range\")");
        source.Should().Contain("AutomationProperties.SetHelpText");
        source.Should().Contain("rangePicker.Click += RangePicker_Click");
        source.Should().Contain("private void RangePicker_Click");
        source.Should().Contain("RangeSelectionRequest = CreateRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("_rangeBox.SelectAll()");
        var pickerHandlerSource = source[
            source.IndexOf("private void RangePicker_Click", StringComparison.Ordinal)..
            source.IndexOf("public static AllowEditRangeSelectionRequest", StringComparison.Ordinal)];
        pickerHandlerSource.Should().Contain("Keyboard.Focus(_rangeBox)");
        source.Should().Contain("Use an A1-style range");
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
        source.Should().Contain("_rangeBox.Focus();");
        source.Should().Contain("_rangeBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_rangeBox);");
    }

    [Fact]
    public void ProtectionDialogsInvalidInputs_RefocusInvalidEntryFields()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("FocusConfirmationInput();");
        source.Should().Contain("private void FocusConfirmationInput()");
        source.Should().Contain("_confirmationBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_confirmationBox);");
        source.Should().Contain("FocusRangeInput();");
        source.Should().Contain("private void FocusRangeInput()");
        source.Should().Contain("_rangeBox.Focus();");
        source.Should().Contain("_rangeBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_rangeBox);");
    }

    [Fact]
    public void AllowEditRangeDialog_ExposesExcelLikeRangeManagerActions()
    {
        var source = ReadProtectionDialogSources();

        source.Should().Contain("public enum AllowEditRangeDialogAction");
        source.Should().Contain("public sealed record AllowEditRangeDialogResult");
        source.Should().Contain("private readonly ListBox _existingRangesBox");
        source.Should().Contain("Content = \"_Delete\"");
        source.Should().Contain("Content = \"Clear _All\"");
        source.Should().Contain("private void DeleteSelectedRange_Click");
        source.Should().Contain("private void ClearAllRanges_Click");
        source.Should().Contain("CreateRemoveResult");
        source.Should().Contain("CreateClearResult");
    }

    [Fact]
    public void AllowEditRangesWorkflow_ExecutesAddRemoveAndClearCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("new AllowEditRangeDialog(_currentSheetId, defaultRange, sheet.AllowEditRanges)");
        source.Should().Contain("AllowEditRangeDialogAction.Add");
        source.Should().Contain("new AllowEditRangeCommand(_currentSheetId, range)");
        source.Should().Contain("AllowEditRangeDialogAction.Remove");
        source.Should().Contain("new RemoveAllowEditRangeCommand(_currentSheetId, range)");
        source.Should().Contain("AllowEditRangeDialogAction.Clear");
        source.Should().Contain("new ClearAllowEditRangesCommand(_currentSheetId)");
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
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ProtectionDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AllowEditRangeDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ProtectionDialogPlanner.cs")));
}
