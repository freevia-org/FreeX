using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R103-commands-dependency-deleted-band-1: DeleteRowsCommand/DeleteColumnsCommand never folded the
/// permanently-deleted band's own addresses (`deletedSnapshot`) into CommandOutcome.AffectedCells on
/// the Apply path -- only the shifted survivors' new/vacated addresses were included (see
/// R98_ShiftCommandsDependencyVacatedAddressTests, which exhaustively covers that sibling scenario
/// but has no case for a formula cell that lived INSIDE the deleted band and is never re-occupied by
/// a relocated survivor). Concretely: a formula cell sitting at the tail of its column/row (nothing
/// below/right of it) that gets deleted leaves shiftedSnapshot empty for that column/row, so
/// AffectedCells ends up without that cell's address at all -- WorkbookCellEditService.
/// UpdateFormulaDependencies (driven purely off AffectedCells) never calls ClearFormulaDependencies
/// for it, leaving a stale DependencyGraph precedent/dependent edge behind forever. These tests drive
/// the real commands through Apply and replay the exact UpdateFormulaDependencies logic against a
/// real DependencyGraph/RecalcEngine, mirroring R98_ShiftCommandsDependencyVacatedAddressTests'
/// fixture.
/// </summary>
public sealed class R103_DeleteRowsColumnsDependencyDeletedBandTests
{
    private static (Workbook Workbook, Sheet Sheet, DependencyGraph Graph, RecalcEngine Engine, TestCommandContext Ctx) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);
        return (workbook, sheet, graph, engine, ctx);
    }

    private static void ReplayUpdateFormulaDependencies(
        Sheet sheet, Workbook workbook, RecalcEngine engine, IEnumerable<CellAddress> affectedCells)
    {
        foreach (var affected in affectedCells)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                engine.ClearFormulaDependencies(affected);
                continue;
            }

            engine.RegisterFormulaDependencies(
                affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }
    }

    // ── DeleteRowsCommand: formula lives INSIDE the deleted band, nothing below it ──────────

    [Fact]
    public void DeleteRows_Apply_PurgesStaleDependencyGraphEdgeForFormulaCellInsideDeletedBand()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        // A10 is the LAST formula cell in column A -- nothing below row 10, so deleting row 10 makes
        // shiftedSnapshot empty for this column. The deleted cell is never re-occupied by any
        // relocated survivor.
        var deletedAddr = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(deletedAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(deletedAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(deletedAddr).Should().Contain(precedent);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 10, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(deletedAddr).Should().BeNull("the row band containing it was permanently deleted");

        outcome.AffectedCells.Should().Contain(deletedAddr,
            "the permanently-deleted formula cell's address must be surfaced so its stale dependency-graph entry is purged");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(deletedAddr).Should().BeFalse(
            "no relocated survivor ever re-occupies A10, so the stale precedent edge must not survive the delete");
    }

    // No-regression sibling: a formula cell BELOW the deleted band still relocates correctly and its
    // vacated address is still purged (the pre-existing R98 behaviour this change must not disturb).
    [Fact]
    public void DeleteRows_Apply_StillPurgesVacatedAddressForShiftedSurvivor_NoRegression()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var oldAddr = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 7, 1);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr);

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    // ── DeleteColumnsCommand: formula lives INSIDE the deleted band, nothing to its right ───

    [Fact]
    public void DeleteColumns_Apply_PurgesStaleDependencyGraphEdgeForFormulaCellInsideDeletedBand()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        // J1 is the LAST formula cell in row 1 -- nothing to its right, so deleting column 10 makes
        // shiftedSnapshot empty for this row. Never re-occupied by any relocated survivor.
        var deletedAddr = new CellAddress(sheet.Id, 1, 10);
        sheet.SetFormula(deletedAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(deletedAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(deletedAddr).Should().Contain(precedent);

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 10, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(deletedAddr).Should().BeNull("the column band containing it was permanently deleted");

        outcome.AffectedCells.Should().Contain(deletedAddr,
            "the permanently-deleted formula cell's address must be surfaced so its stale dependency-graph entry is purged");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(deletedAddr).Should().BeFalse(
            "no relocated survivor ever re-occupies J1, so the stale precedent edge must not survive the delete");
    }

    // No-regression sibling: a formula cell to the RIGHT of the deleted band still relocates
    // correctly and its vacated address is still purged (pre-existing R98 behaviour).
    [Fact]
    public void DeleteColumns_Apply_StillPurgesVacatedAddressForShiftedSurvivor_NoRegression()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var oldAddr = new CellAddress(sheet.Id, 1, 10);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 1, 7);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr);

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }
}
