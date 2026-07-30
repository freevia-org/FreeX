using FreeX.Core.Commands;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R95-calc-deleted-sheet-leak: <see cref="RecalcEngine"/> is an
/// app-lifetime singleton shared by every open workbook (see its class remarks), so
/// <see cref="RecalcEngine.RebuildFormulaDependencies"/> scopes its ClearForSheets/volatile/
/// spill-blocked purge strictly to <c>workbook.Sheets</c> AS IT CURRENTLY STANDS. Deleting a sheet
/// (<see cref="RemoveSheetCommand"/> calling <c>Workbook.RemoveSheet</c>) removes it from
/// <c>workbook.Sheets</c> BEFORE the forced full recalc that always follows (either the explicit
/// <c>RecalculateWorkbook()</c> call on the forward path, or the <c>RequiresFullRecalc</c> flag on
/// Undo/Redo of a structural sheet command) ever runs -- so that deleted sheet's own SheetId is
/// already excluded from the very set used to decide what to purge, and every dependency-graph
/// edge, volatile-cell registration, and spill-blocked-anchor entry that belonged entirely to it
/// leaked forever. The fix tracks each workbook's own sheet ids as of its last
/// RebuildFormulaDependencies call and diffs against the current set to catch removals, purging
/// them via the same shared helper <see cref="RecalcEngine.RetireWorkbook"/> already used at
/// workbook close.
/// </summary>
public sealed class R95_DeletedSheetDependencyGraphLeakTests
{
    private static RecalcEngine Engine(out DependencyGraph graph)
    {
        graph = new DependencyGraph();
        return new RecalcEngine(graph, new FormulaEvaluator());
    }

    [Fact]
    public void DeleteSheet_ThenForcedFullRecalc_PurgesDeletedSheetsDependencyEdgesAndVolatileCells()
    {
        var engine = Engine(out var graph);
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Sheet2 has a same-sheet formula dependency (B1 depends on A1) and a volatile formula --
        // exactly the leaked-subgraph shape the finding describes.
        var a1 = new CellAddress(sheet2.Id, 1, 1);
        var b1 = new CellAddress(sheet2.Id, 1, 2);
        var volatileCell = new CellAddress(sheet2.Id, 1, 3);
        sheet2.SetCell(a1, new NumberValue(5));
        sheet2.SetFormula(b1, "A1*2");
        sheet2.SetFormula(volatileCell, "NOW()");

        // Matches real usage: the sheet gets a normal full recalc (F9, initial load, or a prior
        // structural sheet op) at some point while it still exists, registering its edges in the
        // shared graph -- this is what RemoveSheetCommand's own forced post-delete recalc relies
        // on having already run at least once for THIS workbook.
        engine.RecalculateAllFormulas(workbook);

        graph.HasDependencies(b1).Should().BeTrue("Sheet2's B1=A1*2 edge must be registered before deletion");
        engine.VolatileCellCountForTests.Should().Be(1, "Sheet2's NOW() must be registered before deletion");

        // Exercise the REAL command dispatched by the Delete-Sheet UI path (WorkbookSession.
        // DeleteActiveSheet -> ExecuteEditCommand(new RemoveSheetCommand(...))), not a hand-built
        // model mutation.
        var ctx = new TestCommandContext(workbook);
        var outcome = new RemoveSheetCommand(sheet2.Id).Apply(ctx);
        outcome.Success.Should().BeTrue();
        workbook.Sheets.Should().NotContain(s => s.Id == sheet2.Id, "RemoveSheetCommand.Apply removes the sheet before recalc runs");

        // Mirrors the explicit RecalculateWorkbook() -> RecalculateAll -> RecalculateAllFormulas
        // compensation WorkbookSession.DeleteActiveSheet performs immediately after Apply (and
        // that Undo/Redo reaches via ApplyHistoryOutcome's RequiresFullRecalc branch instead).
        engine.RecalculateAllFormulas(workbook);

        graph.HasDependencies(b1).Should().BeFalse(
            "the deleted sheet's own dependency-graph edge must be purged, not left registered forever in the shared singleton graph");
        graph.GetDirectDependents(a1).Should().NotContain(b1,
            "the deleted sheet's precedent->dependent edge must not survive the sheet's own removal");
        engine.VolatileCellCountForTests.Should().Be(0,
            "the deleted sheet's volatile-cell registration must be purged alongside its dependency edges");
    }

    [Fact]
    public void UndoOfAddSheet_ThenForcedFullRecalc_PurgesAddedSheetsDependencyEdges()
    {
        // Sibling path: RemoveSheetCommand is not the only way a sheet leaves workbook.Sheets --
        // AddSheetCommand.Revert (Undo of Add Sheet) calls the exact same Workbook.RemoveSheet, and
        // WorkbookCellEditService.ApplyHistoryOutcome forces the same full recalc afterward via
        // outcome.RequiresFullRecalc on the Undo path.
        var engine = Engine(out var graph);
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");

        var ctx = new TestCommandContext(workbook);
        var addCommand = new AddSheetCommand("Sheet2");
        var addOutcome = addCommand.Apply(ctx);
        addOutcome.Success.Should().BeTrue();

        var addedSheet = workbook.Sheets[^1];
        var a1 = new CellAddress(addedSheet.Id, 1, 1);
        var b1 = new CellAddress(addedSheet.Id, 1, 2);
        addedSheet.SetCell(a1, new NumberValue(3));
        addedSheet.SetFormula(b1, "A1+1");

        // A full recalc while the added sheet still exists (matches real usage -- e.g. the user
        // edits cells, and some later structural op or F9 runs a full rebuild before ever undoing
        // the Add).
        engine.RecalculateAllFormulas(workbook);
        graph.HasDependencies(b1).Should().BeTrue();

        // Undo of Add Sheet: Revert removes the sheet exactly like RemoveSheetCommand.Apply does.
        addCommand.Revert(ctx);
        workbook.Sheets.Should().NotContain(s => s.Id == addedSheet.Id);

        engine.RecalculateAllFormulas(workbook);

        graph.HasDependencies(b1).Should().BeFalse(
            "Undo-of-Add-Sheet must purge the reverted sheet's dependency edges just like Delete Sheet does");
    }

    [Fact]
    public void DeleteSheet_SurvivingSheetsDependenciesAndRecalculationAreUnaffected()
    {
        // No-regression sibling: deleting one sheet must not disturb another surviving sheet's own
        // dependency edges or its ability to keep recalculating correctly.
        var engine = Engine(out var graph);
        var workbook = new Workbook("Book");
        var keep = workbook.AddSheet("Keep");
        var remove = workbook.AddSheet("Remove");

        var keepA1 = new CellAddress(keep.Id, 1, 1);
        var keepB1 = new CellAddress(keep.Id, 1, 2);
        keep.SetCell(keepA1, new NumberValue(10));
        keep.SetFormula(keepB1, "A1*3");

        var removeA1 = new CellAddress(remove.Id, 1, 1);
        var removeB1 = new CellAddress(remove.Id, 1, 2);
        remove.SetCell(removeA1, new NumberValue(1));
        remove.SetFormula(removeB1, "A1+1");

        engine.RecalculateAllFormulas(workbook);

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(remove.Id).Apply(ctx).Success.Should().BeTrue();

        var report = engine.RecalculateAllFormulas(workbook);

        graph.HasDependencies(keepB1).Should().BeTrue("the surviving sheet's own dependency edge must remain intact");
        report.RecalculatedCells.Should().Contain(keepB1, "the surviving sheet's formula must still be recalculated");
        ((NumberValue)keep.GetValue(keepB1)).Value.Should().Be(30, "the surviving sheet's formula must still evaluate correctly");
    }
}
