using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the R142/GOALSEEK-WHATIF-1 finding: RemoveSheetCommand.Apply rewrites
/// or remaps every other cross-sheet reference kind (charts, pivots, sparklines, slicers,
/// timelines, hyperlinks, form controls, CF/DV) when a sheet is deleted, but never touched
/// Workbook.Scenarios at all -- unlike Excel, where a scenario's changing cells are confined to a
/// single worksheet, FreeX's Scenario Manager lets a saved scenario's changing cells span
/// multiple sheets (see SaveScenarioCommand, which has no same-sheet check). Left unhandled, a
/// scenario whose changing cells included the deleted sheet kept a permanently dangling reference:
/// ApplyScenarioCommand would reject the scenario forever ("Scenario changing cells must belong
/// to this workbook."), with no cleanup and no undo entry recording that the scenario itself
/// changed shape. The fix mirrors Excel's own behavior of deleting a worksheet's scenarios along
/// with the worksheet: any changing cell on the deleted sheet is dropped from every scenario that
/// referenced it, and a scenario left with zero changing cells is removed entirely -- with full
/// undo support.
/// </summary>
public sealed class R142_RemoveSheetScenarioCleanupTests
{
    [Fact]
    public void RemoveSheetCommand_DropsChangingCellOnDeletedSheet_LeavesScenarioApplicable_AndUndoRestores()
    {
        var workbook = new Workbook("RemoveSheetScenarioTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var ctx0 = new TestCommandContext(workbook);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(20));

        var cellOnSheet1 = new ScenarioCellValue(new CellAddress(sheet1.Id, 1, 1), new NumberValue(100));
        var cellOnSheet2 = new ScenarioCellValue(new CellAddress(sheet2.Id, 1, 1), new NumberValue(200));
        new SaveScenarioCommand("Base Case", [cellOnSheet1, cellOnSheet2]).Apply(ctx0).Success.Should().BeTrue();

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(sheet2.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells.Should().ContainSingle()
            .Which.Should().Be(cellOnSheet1,
                because: "the changing cell on the deleted sheet must be dropped, leaving only the surviving one");

        // The scenario must now actually be applicable -- no dangling reference left behind.
        var applyOutcome = new ApplyScenarioCommand("Base Case").Apply(ctx);
        applyOutcome.Success.Should().BeTrue();
        sheet1.GetValue(1, 1).Should().Be(new NumberValue(100));

        command.Revert(ctx);

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].ChangingCells.Should().BeEquivalentTo([cellOnSheet1, cellOnSheet2],
            because: "Undo must restore the scenario's original two-sheet changing-cell list exactly");
    }

    [Fact]
    public void RemoveSheetCommand_DeletesScenarioLeftWithNoChangingCells_AndUndoRestoresIt()
    {
        var workbook = new Workbook("RemoveSheetScenarioAllCellsTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var ctx0 = new TestCommandContext(workbook);

        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(5));

        // Entirely on the sheet about to be deleted -- Excel would delete this scenario along
        // with the worksheet it lives on.
        var cellOnSheet2 = new ScenarioCellValue(new CellAddress(sheet2.Id, 1, 1), new NumberValue(50));
        new SaveScenarioCommand("Doomed", [cellOnSheet2]).Apply(ctx0).Success.Should().BeTrue();

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(sheet2.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Scenarios.Should().BeEmpty(
            because: "a scenario with zero remaining changing cells must be dropped entirely, not left behind empty");

        command.Revert(ctx);

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Name.Should().Be("Doomed");
        workbook.Scenarios[0].ChangingCells.Should().ContainSingle().Which.Should().Be(cellOnSheet2);
    }

    [Fact]
    public void RemoveSheetCommand_ScenarioEntirelyOnSurvivingSheet_IsUntouched()
    {
        // Sibling/neighbouring-behavior guard: a scenario with no changing cell on the deleted
        // sheet at all must not be touched in any way by the new cleanup pass.
        var workbook = new Workbook("RemoveSheetScenarioUnrelatedTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        var ctx0 = new TestCommandContext(workbook);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        var untouchedCell = new ScenarioCellValue(new CellAddress(sheet1.Id, 1, 1), new NumberValue(999));
        var original = new WorkbookScenario("Untouched", [untouchedCell]);
        workbook.Scenarios.Add(original);

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(sheet3.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Should().BeSameAs(original,
            because: "a scenario unrelated to the deleted sheet must survive the delete-sheet pass unmodified, " +
                     "not merely equal but the very same instance (no needless clone/rebuild)");

        command.Revert(ctx);

        workbook.Scenarios.Should().ContainSingle();
        workbook.Scenarios[0].Should().BeSameAs(original);
    }
}
