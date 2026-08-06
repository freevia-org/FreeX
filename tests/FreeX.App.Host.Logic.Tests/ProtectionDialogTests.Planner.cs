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

        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet.IsProtected,
            password: "ignored",
            SheetProtectionOptions.DefaultEnabledPermissions);

        result.Mode.Should().Be(ProtectionDialogMode.Unprotect);
        result.Password.Should().Be("ignored");
    }

    [Fact]
    public void SheetProtectionDialogResult_ForUnprotectedSheetKeepsPassword()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet.IsProtected,
            password: "secret",
            SheetProtectionOptions.DefaultEnabledPermissions);

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().Be("secret");
        result.SelectedSheetPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.SelectUnlockedCells);
    }

    [Fact]
    public void SheetProtectionDialogResult_ForUnprotectedSheetKeepsSelectedPermissions()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet.IsProtected,
            password: "secret",
            selectedSheetPermissions:
            [
                SheetProtectionPermission.SelectUnlockedCells,
                SheetProtectionPermission.Sort,
            ]);

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().Be("secret");
        result.SelectedSheetPermissions.Should().Equal(
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void SheetProtectionDialogResult_RequiresMatchingPasswordConfirmation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet.IsProtected,
            password: "secret",
            confirmation: "Secret",
            SheetProtectionOptions.DefaultEnabledPermissions);

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().BeNull();
    }

    [Fact]
    public void DefaultSheetPermissions_MatchExcelProtectSheetChecklist()
    {
        SheetProtectionOptions.All.Select(option => UiText.Get(option.LabelKey))
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

        var result = ProtectionDialogPlanner.CreateWorkbookResult(workbook.IsStructureProtected, password: "ignored");

        result.Mode.Should().Be(ProtectionDialogMode.Unprotect);
        result.Password.Should().Be("ignored");
    }

    [Fact]
    public void TryParseAllowEditRange_AcceptsRangeOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        AllowEditRangePlanner.TryParseRange("A1:B2", sheetId, out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheetId, 1, 1));
        range.End.Should().Be(new CellAddress(sheetId, 2, 2));
    }

    [Fact]
    public void TryParseAllowEditRange_RejectsInvalidRangeThroughSharedParser()
    {
        AllowEditRangePlanner.TryParseRange("A1:B2:C3", SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void Host_UsesPortableProtectionPlannersAndOnlyLocalizesPermissionLabels()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "ProtectionDialogs.cs",
            "MainWindow.ProtectionWorkflowSession.cs",
            "AllowEditRangeDialog.cs");

        source.Should().Contain("ProtectionWorkflowSession");
        source.Should().Contain("AllowEditRangePlanner.TryParseRange(");
        source.Should().Contain("SheetProtectionOptions.All");
        source.Should().NotContain("new ProtectionDialogResult");
        source.Should().Contain("UiText.Get(");
    }
}
