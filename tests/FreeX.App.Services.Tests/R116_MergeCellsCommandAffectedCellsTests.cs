using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R116-commands-merge-affected-cells-1: MergeCellsCommand.Apply blanks every non-top-left cell's
/// VALUE in the merge range (matching Excel's "keep upper-left value only" merge behaviour) but,
/// pre-fix, reported no AffectedCells at all. Every consumer of CommandOutcome
/// (WorkbookCellEditService.ApplyHistoryOutcome/UpdateFormulaDependencies, RecalcEngine.Recalculate)
/// treats a null/empty AffectedCells list as "nothing changed", so a formula elsewhere in the
/// workbook that referenced a cell whose value the merge just discarded kept showing its stale
/// pre-merge cached value until an unrelated edit or an explicit recalc happened to touch it. These
/// tests drive the real product entry point -- WorkbookCellEditService.ExecuteEditCommand, exactly
/// what the WPF/Avalonia ribbon "Merge Cells"/"Merge Across"/Format-Cells-dialog paths call -- rather
/// than asserting on the command's outcome directly.
/// </summary>
public sealed class R116_MergeCellsCommandAffectedCellsTests
{
    [Fact]
    public void Merge_HardDeletedSwallowedCell_IsReportedAffected_AndDependentFormulaRecalculates()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(5));
        sheet.SetFormula(c1, "B1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(6), "C1 = B1+1 with B1=5 before the merge");

        var range = new GridRange(a1, b1);
        var result = service.ExecuteEditCommand(workbook, new MergeCellsCommand(sheet.Id, range));

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain(b1,
            "B1's value was discarded by the merge, so it must be surfaced as an affected cell " +
            "for dependency re-registration and recalculation to happen at all");

        // Pre-fix: MergeCellsCommand reported no AffectedCells, so RecalcEngine.Recalculate
        // short-circuited to an empty report and C1 kept showing its stale pre-merge value of 6
        // even though B1's value was gone.
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(1),
            "B1 was blanked by the merge (becomes 0 in arithmetic), so C1 = B1+1 must recalculate to 1");
    }

    [Fact]
    public void Merge_StyledSwallowedCell_IsReportedAffected_AndDependentFormulaRecalculates()
    {
        // Sibling coverage for the OTHER blanking branch in MergeCellsCommand.Apply: a swallowed
        // cell that carries a non-default style is blanked via sheet.SetCell(BlankValue) rather
        // than sheet.ClearCell, to preserve its formatting across a later Unmerge (R47). That
        // branch must report the address as affected too.
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var yellowFill = workbook.RegisterStyle(new CellStyle { FillColor = CellColor.FromArgb(255, 255, 0) });

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new Cell { Value = new NumberValue(42), StyleId = yellowFill });
        sheet.SetFormula(c1, "B1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(43));

        var range = new GridRange(a1, b1);
        var result = service.ExecuteEditCommand(workbook, new MergeCellsCommand(sheet.Id, range));

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Contain(b1);
        sheet.GetCell(b1)!.StyleId.Should().Be(yellowFill, "the swallowed cell's own formatting must still survive the merge");
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(1),
            "B1's value was blanked by the merge even though its style was preserved, so C1 must recalculate to 1");
    }

    [Fact]
    public void Merge_TopLeftCellIsNotReportedAffected_NoRegression()
    {
        // No-regression sibling: the top-left cell of the merge keeps its own value untouched by
        // MergeCellsCommand.Apply, so it must not be listed among the newly-affected addresses
        // (only the swallowed, blanked cells are "affected" by this command).
        var (workbook, sheet, _, service, _) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(b1, new NumberValue(5));

        var range = new GridRange(a1, b1);
        var result = service.ExecuteEditCommand(workbook, new MergeCellsCommand(sheet.Id, range));

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().NotContain(a1);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(10));
    }

    private static (
        Workbook Workbook,
        Sheet Sheet,
        CommandBus CommandBus,
        WorkbookCellEditService Service,
        RecalcEngine RecalcEngine) CreateEditService()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, commandBus, service, recalcEngine);
    }
}
