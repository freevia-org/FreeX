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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ProtectionDialogs.cs"));

        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().Contain("new Label { Content = \"_Range:\"");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("Header = \"Range\"");
        source.Should().Contain("Content = \"...\"");
        source.Should().Contain("ToolTip = \"Select editable range\"");
        source.Should().Contain("AutomationProperties.SetName(rangePicker, \"Select editable range\")");
        source.Should().Contain("rangePicker.Click += RangePicker_Click");
        source.Should().Contain("private void RangePicker_Click");
        source.Should().Contain("_rangeBox.SelectAll()");
        source.Should().Contain("Use an A1-style range");
    }

    [Fact]
    public void ProtectSheetDialog_ExposesPermissionChecklistAndFollowUpConfirmation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ProtectionDialogs.cs"));

        source.Should().Contain("Allow all users of this worksheet to:");
        source.Should().Contain("Header = \"Password\"");
        source.Should().Contain("Protect worksheet and contents of locked cells");
        source.Should().Contain("Caution: lost or forgotten passwords cannot be recovered.");
        source.Should().Contain("ConfirmPasswordDialog");
        source.Should().Contain("Confirm Password");
        source.Should().NotContain("_Confirm password:");
        source.Should().Contain("Select locked cells");
        source.Should().Contain("Edit scenarios");
    }
}
