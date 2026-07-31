using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R100-calc-recalc-relocated-formula-dependency-1: RecalcEngine.Recalculate is the WPF desktop
/// host's ONLY dependency-graph touch point for InsertRowsCommand/DeleteRowsCommand/InsertColumns
/// Command/DeleteColumnsCommand (and the cell-shift equivalents). MainWindow.CellsCommands.cs runs
/// these commands via CommandBus.Execute and then feeds CommandOutcome.AffectedCells straight into
/// RecalculateIfAutomatic -&gt; RecalcEngine.Recalculate -- it never calls the unconditional
/// register/clear loop that WorkbookCellEditService.UpdateFormulaDependencies runs for the Avalonia
/// shell. These tests exercise that exact real host call graph (InsertRowsCommand.Apply -&gt;
/// RecalcEngine.Recalculate(outcome.AffectedCells)) with no hand-replayed dependency bookkeeping, so
/// they cover the WPF host's actual runtime path rather than the App.Services seam that
/// R98_InsertRowsDependencyVacatedAddressTests and R24_InsertRowsVolatileRelocationTests validate.
/// </summary>
public sealed class R100_RecalcEngineStructuralRelocationDependencyTests
{
    /// <summary>
    /// Bug #1 (RecalcEngine.cs:792, EnsureChangedFormulaDependenciesRegistered): when Insert Rows
    /// physically relocates a formula cell (Cell object + intact CachedAst) onto an address that the
    /// dependency graph already had a precedents entry for -- left behind by a DIFFERENT formula
    /// that used to occupy that same address before the shift -- the guard "!_graph.HasDependencies
    /// (addr)" wrongly treats "some registration already exists here" as proof the registration is
    /// up to date for THIS cell's current formula, and skips re-registering. The relocated formula's
    /// real precedent is never wired in, and edits to it never dirty the cell at its new home; edits
    /// to the stale prior occupant's precedent spuriously do instead.
    /// </summary>
    [Fact]
    public void InsertRows_RelocatedFormulaLandsOnStaleRegisteredAddress_RewiresToItsOwnPrecedent()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var ctx = new TestCommandContext(workbook);

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b1, new NumberValue(100));
        sheet.SetCell(b2, new NumberValue(200));

        // A10 = "=B1" and A13 = "=B2", both referencing a cell OUTSIDE the row band that Insert
        // Rows(beforeRow: 5) is about to shift, so RowColumnShiftHelpers.RewriteAllFormulas leaves
        // their formula TEXT untouched -- exactly the "text does not need rewriting" scenario the
        // defect describes.
        var a10 = new CellAddress(sheet.Id, 10, 1);
        var a13 = new CellAddress(sheet.Id, 13, 1);
        sheet.SetFormula(a10, "B1");
        sheet.SetFormula(a13, "B2");

        // Let Automatic calc run once so both cells' Cell.CachedAst gets populated and their real
        // dependencies get registered via the ordinary first-evaluation path (RecalcEngine.cs
        // ~351-355), not by hand-calling RegisterFormulaDependencies.
        engine.Recalculate(workbook, [a10, a13]);
        sheet.GetValue(a10).Should().Be(new NumberValue(100));
        sheet.GetValue(a13).Should().Be(new NumberValue(200));

        // Insert 3 rows above row 5: A10 (row >= beforeRow) relocates to row 13, A13 relocates to
        // row 16. A10's NEW address (row 13) collides with A13's OLD address, which the dependency
        // graph already registered against B2 in the Recalculate call above.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocatedA10 = new CellAddress(sheet.Id, 13, 1);
        var relocatedA13 = new CellAddress(sheet.Id, 16, 1);
        sheet.GetCell(relocatedA10)!.FormulaText.Should().Be("B1");
        sheet.GetCell(relocatedA13)!.FormulaText.Should().Be("B2");

        // The exact real-product call the WPF host makes: MainWindow.WorkbookUiState.cs's
        // RecalculateIfAutomatic feeds CommandOutcome.AffectedCells straight into
        // RecalcEngine.Recalculate with no prior registration step.
        engine.Recalculate(workbook, outcome.AffectedCells ?? []);

        // Editing B1 (the relocated cell's REAL, current precedent) must dirty the cell now sitting
        // at row 13. Before the fix this failed: the graph still thought row 13 depended on B2 (the
        // stale entry left by the old A13), so this edit never touched it and it stayed frozen at
        // its pre-insert value of 100.
        sheet.SetCell(b1, new NumberValue(999));
        engine.Recalculate(workbook, [b1]);
        sheet.GetValue(relocatedA10).Should().Be(new NumberValue(999),
            "the formula relocated to row 13 must recalculate off its own real precedent B1, not stay frozen");

        // Editing B2 must NOT spuriously recalculate the cell at row 13 (it is not really B2's
        // dependent any more) but MUST correctly recalculate the cell that legitimately relocated to
        // row 16.
        sheet.SetCell(b2, new NumberValue(777));
        engine.Recalculate(workbook, [b2]);
        sheet.GetValue(relocatedA10).Should().Be(new NumberValue(999),
            "row 13 must stay unaffected by B2 edits once its dependency graph entry correctly points at B1");
        sheet.GetValue(relocatedA13).Should().Be(new NumberValue(777),
            "the formula relocated to row 16 must still correctly recalculate off its own precedent B2");
    }

    /// <summary>
    /// Bug #2 (RecalcEngine.cs, CollectChangedFormulaCells / EnsureChangedFormulaDependenciesRegistered):
    /// an address vacated by a relocate-in-place move (its live cell is now blank) is filtered out of
    /// CollectChangedFormulaCells before ClearFormulaDependencies could ever run on it, leaving a
    /// permanent phantom precedent/dependent edge keyed at an address that holds no formula.
    /// </summary>
    [Fact]
    public void InsertRows_VacatedOldAddress_HasNoPhantomDependencyGraphEntryAfterRecalculate()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var ctx = new TestCommandContext(workbook);

        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(100));

        // A10 (row >= beforeRow: 5) relocates to row 13; nothing moves INTO row 10, so it is left
        // genuinely blank by the shift.
        var a10 = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(a10, "B1");
        engine.Recalculate(workbook, [a10]);

        graph.HasDependencies(a10).Should().BeTrue("sanity: A10's real precedent edge exists before the insert");

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.GetCell(a10).Should().BeNull("row 10 is vacated -- nothing shifted into it");

        // The real host call: RecalcEngine.Recalculate driven straight off AffectedCells.
        engine.Recalculate(workbook, outcome.AffectedCells ?? []);

        graph.HasDependencies(a10).Should().BeFalse(
            "the vacated address must not retain a stale precedent edge from the formula that used to live there");
        graph.GetDirectPrecedents(a10).Should().BeEmpty();
    }

    /// <summary>
    /// No-regression sibling: an ordinary (non-relocating) formula edit through the same
    /// RecalcEngine.Recalculate entry point must still register and recalculate correctly -- the
    /// fix must not disturb the common case where a changed formula address was never previously
    /// registered under a DIFFERENT formula's stale entry.
    /// </summary>
    [Fact]
    public void OrdinaryFormulaEdit_StillRegistersAndRecalculatesThroughSameEntryPoint()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(b1, new NumberValue(5));

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "B1*2");
        engine.Recalculate(workbook, [a1]);
        sheet.GetValue(a1).Should().Be(new NumberValue(10));

        // Ordinary in-place formula edit (no relocation, no structural command): swap A1 to depend
        // on C1 instead.
        sheet.SetCell(c1, new NumberValue(50));
        sheet.SetFormula(a1, "C1*2");
        engine.Recalculate(workbook, [a1]);
        sheet.GetValue(a1).Should().Be(new NumberValue(100),
            "an ordinary formula edit must still re-register its NEW precedent and recalculate correctly");

        // And it must track the new precedent, not the stale old one.
        sheet.SetCell(b1, new NumberValue(9999));
        engine.Recalculate(workbook, [b1]);
        sheet.GetValue(a1).Should().Be(new NumberValue(100),
            "A1 no longer depends on B1 after being re-edited, so a B1 edit must not recalculate it");

        sheet.SetCell(c1, new NumberValue(60));
        engine.Recalculate(workbook, [c1]);
        sheet.GetValue(a1).Should().Be(new NumberValue(120));
    }
}
