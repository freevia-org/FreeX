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
}
