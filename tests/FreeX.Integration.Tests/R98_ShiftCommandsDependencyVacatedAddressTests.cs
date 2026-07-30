using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R98-commands-dependency-vacated-1/-2: sibling gap to R98_InsertRowsDependencyVacatedAddressTests
/// (InsertRowsCommand, already fixed this round). The same defect -- a relocated formula cell's OLD
/// (vacated) address never appearing in CommandOutcome.AffectedCells, so
/// WorkbookCellEditService.UpdateFormulaDependencies (which drives RecalcEngine purely off
/// AffectedCells) never calls ClearFormulaDependencies for it, leaving a phantom precedent/dependent
/// edge behind in DependencyGraph -- also existed in DeleteRowsCommand, InsertColumnsCommand,
/// DeleteColumnsCommand, InsertCellsCommand, and DeleteCellsCommand. Each test below drives the real
/// command through Apply/Revert and replays the exact UpdateFormulaDependencies logic against a real
/// DependencyGraph/RecalcEngine (the same seam R98_InsertRowsDependencyVacatedAddressTests uses),
/// asserting no stale edge survives at the vacated address.
///
/// IMPORTANT fixture note (band-scoped InsertCellsCommand/DeleteCellsCommand only): their
/// AffectedCells build already includes _range.AllCells(), which happens to cover the vacated OLD
/// address of any formula cell that originated INSIDE the target range -- a naive test placing its
/// formula cell there would pass even against the unfixed code. The InsertCells/DeleteCells tests
/// below therefore deliberately place the relocating formula cell OUTSIDE _range (but still inside
/// the wider shiftRegion, e.g. beyond _range.End.Col for a Shift-Right insert spanning to MaxCol) so
/// the gap this round is closing is genuinely exercised.
/// </summary>
public sealed class R98_ShiftCommandsDependencyVacatedAddressTests
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

    // ── DeleteRowsCommand ────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_Apply_PurgesStaleDependencyGraphEdgeAtVacatedOldAddress()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        // Row 10 is above the deleted band [3,5] and shifts up to row 7. Its formula references a
        // cell outside the deleted band, so FormulaRewriter leaves the TEXT untouched.
        var oldAddr = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(oldAddr).Should().Contain(precedent);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 7, 1);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(oldAddr).Should().BeNull();

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr,
            "the vacated pre-delete address must be surfaced so the stale dependency-graph entry there is purged");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    [Fact]
    public void UndoDeleteRows_PurgesStaleDependencyGraphEdgeAtAddressVacatedByTheUndo()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var originalAddr = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(originalAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(originalAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 3);
        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue();

        var postShiftAddr = new CellAddress(sheet.Id, 7, 1);
        ReplayUpdateFormulaDependencies(sheet, workbook, engine, applyOutcome.AffectedCells!);
        graph.GetDirectPrecedents(postShiftAddr).Should().Contain(precedent, "sanity: registered after Apply");

        command.Revert(ctx);

        sheet.GetCell(originalAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(postShiftAddr).Should().BeNull();

        command.AffectedCells.Should().Contain(originalAddr);
        command.AffectedCells.Should().Contain(postShiftAddr,
            "the address vacated by the undo's move-back must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, command.AffectedCells);

        graph.HasDependencies(postShiftAddr).Should().BeFalse();
        graph.GetDirectPrecedents(originalAddr).Should().Contain(precedent);
    }

    // ── InsertColumnsCommand ─────────────────────────────────────────────────

    [Fact]
    public void InsertColumns_Apply_PurgesStaleDependencyGraphEdgeAtVacatedOldAddress()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        // Column 10 (>= beforeCol=5) relocates; formula references a cell outside the shifted band
        // so its TEXT is untouched.
        var oldAddr = new CellAddress(sheet.Id, 1, 10);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(oldAddr).Should().Contain(precedent);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 1, 13);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(oldAddr).Should().BeNull();

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr,
            "the vacated pre-shift address must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    [Fact]
    public void UndoInsertColumns_PurgesStaleDependencyGraphEdgeAtAddressVacatedByTheUndo()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var originalAddr = new CellAddress(sheet.Id, 1, 10);
        sheet.SetFormula(originalAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(originalAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 3);
        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue();

        var postShiftAddr = new CellAddress(sheet.Id, 1, 13);
        ReplayUpdateFormulaDependencies(sheet, workbook, engine, applyOutcome.AffectedCells!);
        graph.GetDirectPrecedents(postShiftAddr).Should().Contain(precedent, "sanity: registered after Apply");

        command.Revert(ctx);

        sheet.GetCell(originalAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(postShiftAddr).Should().BeNull();

        command.AffectedCells.Should().Contain(originalAddr);
        command.AffectedCells.Should().Contain(postShiftAddr,
            "the address vacated by the undo's move-back must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, command.AffectedCells);

        graph.HasDependencies(postShiftAddr).Should().BeFalse();
        graph.GetDirectPrecedents(originalAddr).Should().Contain(precedent);
    }

    // ── DeleteColumnsCommand ─────────────────────────────────────────────────

    [Fact]
    public void DeleteColumns_Apply_PurgesStaleDependencyGraphEdgeAtVacatedOldAddress()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        // Column 10 is right of the deleted band [3,5] and shifts left to column 7.
        var oldAddr = new CellAddress(sheet.Id, 1, 10);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(oldAddr).Should().Contain(precedent);

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 1, 7);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(oldAddr).Should().BeNull();

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr,
            "the vacated pre-delete address must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    [Fact]
    public void UndoDeleteColumns_PurgesStaleDependencyGraphEdgeAtAddressVacatedByTheUndo()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var originalAddr = new CellAddress(sheet.Id, 1, 10);
        sheet.SetFormula(originalAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(originalAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 3);
        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue();

        var postShiftAddr = new CellAddress(sheet.Id, 1, 7);
        ReplayUpdateFormulaDependencies(sheet, workbook, engine, applyOutcome.AffectedCells!);
        graph.GetDirectPrecedents(postShiftAddr).Should().Contain(precedent, "sanity: registered after Apply");

        command.Revert(ctx);

        sheet.GetCell(originalAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(postShiftAddr).Should().BeNull();

        command.AffectedCells.Should().Contain(originalAddr);
        command.AffectedCells.Should().Contain(postShiftAddr,
            "the address vacated by the undo's move-back must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, command.AffectedCells);

        graph.HasDependencies(postShiftAddr).Should().BeFalse();
        graph.GetDirectPrecedents(originalAddr).Should().Contain(precedent);
    }

    // ── InsertCellsCommand (band-scoped, Shift-Right) ───────────────────────
    // Fixture note: the formula cell sits at column 50, well beyond _range.End.Col=5 (the insert
    // range is B1:E1, width 4) but still inside the Shift-Right shiftRegion (Start.Col..MaxCol). A
    // naive test placing the formula INSIDE the range would pass even unfixed, since _range.AllCells()
    // already (coincidentally) covers that footprint.

    [Fact]
    public void InsertCellsShiftRight_Apply_PurgesStaleDependencyGraphEdgeAtVacatedOldAddress()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var oldAddr = new CellAddress(sheet.Id, 1, 50);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(oldAddr).Should().Contain(precedent);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 5)); // B1:E1, width 4
        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 1, 54);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(oldAddr).Should().BeNull();

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr,
            "the vacated pre-shift address (outside the target range itself) must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    [Fact]
    public void UndoInsertCellsShiftRight_PurgesStaleDependencyGraphEdgeAtAddressVacatedByTheUndo()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var originalAddr = new CellAddress(sheet.Id, 1, 50);
        sheet.SetFormula(originalAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(originalAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 5));
        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue();

        var postShiftAddr = new CellAddress(sheet.Id, 1, 54);
        ReplayUpdateFormulaDependencies(sheet, workbook, engine, applyOutcome.AffectedCells!);
        graph.GetDirectPrecedents(postShiftAddr).Should().Contain(precedent, "sanity: registered after Apply");

        command.Revert(ctx);

        sheet.GetCell(originalAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(postShiftAddr).Should().BeNull();

        command.AffectedCells.Should().Contain(originalAddr);
        command.AffectedCells.Should().Contain(postShiftAddr,
            "the address vacated by the undo's move-back must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, command.AffectedCells);

        graph.HasDependencies(postShiftAddr).Should().BeFalse();
        graph.GetDirectPrecedents(originalAddr).Should().Contain(precedent);
    }

    // ── DeleteCellsCommand (band-scoped, Shift-Left) ────────────────────────
    // Fixture note: the surviving formula cell sits at column 50, beyond _range.End.Col=5 (delete
    // range is B1:E1, width 4), so it survives by shifting left rather than being permanently deleted.

    [Fact]
    public void DeleteCellsShiftLeft_Apply_PurgesStaleDependencyGraphEdgeAtVacatedOldAddress()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var oldAddr = new CellAddress(sheet.Id, 1, 50);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);
        graph.GetDirectPrecedents(oldAddr).Should().Contain(precedent);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 5)); // B1:E1, width 4
        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 1, 46);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(oldAddr).Should().BeNull();

        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr,
            "the vacated pre-shift address (the surviving cell's original column, outside _range) must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, outcome.AffectedCells!);

        graph.HasDependencies(oldAddr).Should().BeFalse();
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    [Fact]
    public void UndoDeleteCellsShiftLeft_PurgesStaleDependencyGraphEdgeAtAddressVacatedByTheUndo()
    {
        var (workbook, sheet, graph, engine, ctx) = Setup();

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var originalAddr = new CellAddress(sheet.Id, 1, 50);
        sheet.SetFormula(originalAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(originalAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 5));
        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);
        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue();

        var postShiftAddr = new CellAddress(sheet.Id, 1, 46);
        ReplayUpdateFormulaDependencies(sheet, workbook, engine, applyOutcome.AffectedCells!);
        graph.GetDirectPrecedents(postShiftAddr).Should().Contain(precedent, "sanity: registered after Apply");

        command.Revert(ctx);

        sheet.GetCell(originalAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(postShiftAddr).Should().BeNull();

        command.AffectedCells.Should().Contain(originalAddr);
        command.AffectedCells.Should().Contain(postShiftAddr,
            "the address vacated by the undo's move-back must be surfaced");

        ReplayUpdateFormulaDependencies(sheet, workbook, engine, command.AffectedCells);

        graph.HasDependencies(postShiftAddr).Should().BeFalse();
        graph.GetDirectPrecedents(originalAddr).Should().Contain(precedent);
    }
}
