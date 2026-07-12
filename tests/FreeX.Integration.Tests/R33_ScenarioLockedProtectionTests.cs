using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R33-protection-enforcement-2-1: per-scenario "Prevent Changes" (Locked) is Excel's own
/// per-object protection flag, independent of the sheet-level "Edit scenarios" permission. Even
/// when a protected sheet grants SheetProtectionPermission.EditScenarios, a scenario whose own
/// Locked flag is set must still reject Delete and Save-replace/overwrite -- only the sheet-level
/// permission was being checked before, so a Locked scenario was silently editable/deletable
/// whenever EditScenarios was granted. An unlocked scenario on the same protected sheet must
/// remain editable, and an unprotected sheet must be unaffected regardless of the Locked flag.
/// </summary>
public sealed class R33_ScenarioLockedProtectionTests
{
    [Fact]
    public void DeleteScenarioCommand_RejectsLockedScenarioOnProtectedSheetEvenWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))],
            Locked: true));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);

        var outcome = new DeleteScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("locked");
        workbook.Scenarios.Should().ContainSingle();
    }

    [Fact]
    public void DeleteScenarioCommand_AllowsUnlockedScenarioOnProtectedSheetWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))],
            Locked: false));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);

        var outcome = new DeleteScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void DeleteScenarioCommand_AllowsLockedScenarioOnUnprotectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(42))],
            Locked: true));

        var outcome = new DeleteScenarioCommand("Best Case").Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().BeEmpty();
    }

    [Fact]
    public void SaveScenarioCommand_RejectsOverwritingLockedScenarioOnProtectedSheetEvenWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(10))],
            Locked: true));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);

        var outcome = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(99))]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("locked");
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void SaveScenarioCommand_AllowsOverwritingUnlockedScenarioOnProtectedSheetWithEditScenariosPermission()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(10))],
            Locked: false));
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);

        var outcome = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(99))]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(99));
    }

    [Fact]
    public void SaveScenarioCommand_AllowsOverwritingLockedScenarioOnUnprotectedSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var address = new CellAddress(sheet.Id, 1, 1);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(10))],
            Locked: true));

        var outcome = new SaveScenarioCommand(
            "Best Case",
            [new ScenarioCellValue(address, new NumberValue(99))]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells[0].Value.Should().Be(new NumberValue(99));
    }
}
