using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R153/K1 remediation: R153 taught <see cref="RowColumnShiftHelpers.RewriteAllFormulas"/> (and
/// Sheet.Clone.cs / CellStateSnapshot.cs) to preserve a legacy CSE array cell's fixed-extent
/// identity (ArrayMode / LegacyArrayRows / LegacyArrayCols) when a structural edit reassigns
/// <see cref="Cell.FormulaText"/> to adjust reference text -- but two hand-written re-implementations
/// of that same rewrite-loop shape inside <c>MoveRangeCommand.cs</c> still assigned
/// <c>cell.FormulaText</c> directly:
///
///  - <c>RewriteAllFormulasCrossSheet</c> (workbook-wide "other formula elsewhere follows the moved
///    cell across sheets" pass for a cross-sheet Cut) -- exercised below.
///  - <c>CaptureSourcePayloads</c>'s per-cell <c>Clone()</c> immediately followed by a same-text
///    <c>FormulaText</c> reassignment for the MOVED cells' own formulas -- exercised below too, since
///    it silently undid what <c>Clone()</c> had just correctly preserved.
///
/// Both are fixed by routing through the same
/// <see cref="RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity"/> helper the R153 fix
/// introduced (widened from private to internal so these two call sites can reuse it directly instead
/// of restating the save/assign/restore pattern a third time).
/// </summary>
public sealed class R153_K1_MoveRangeCrossSheetLegacyArrayTests
{
    // ══════════════════════════════════════════════════════════════════════════════════════════
    // RewriteAllFormulasCrossSheet: a legacy CSE array HOSTED ELSEWHERE references the cut range.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CrossSheetCut_OtherFormulaElsewhereIsLegacyArray_PreservesArrayIdentityAndGuard()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        // Sheet1!A10:A14 = 1..5 -- about to be cut to Sheet2.
        for (uint r = 10; r <= 14; r++)
            sheet1.SetCell(new CellAddress(sheet1.Id, r, 1), new NumberValue(r - 9));

        // Sheet1!H1:H2 (2x1) CSE-entered as {=SUM(A10:A14)} -- NOT part of the cut range, but its
        // formula references it.
        var h1 = new CellAddress(sheet1.Id, 1, 8);
        var h2 = new CellAddress(sheet1.Id, 2, 8);
        var legacyCell = Cell.FromFormula("SUM(A10:A14)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet1.SetCell(h1, legacyCell);

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [h1]);
        sheet1.GetValue(h1).Should().Be(new NumberValue(15), "sanity before the move");
        sheet1.GetValue(h2).Should().Be(new NumberValue(15), "sanity: H2 replicated before the move");

        var sourceRange = new GridRange(
            new CellAddress(sheet1.Id, 10, 1), new CellAddress(sheet1.Id, 14, 1));
        var destination = new CellAddress(sheet2.Id, 10, 1); // same row/col, different sheet
        var command = new MoveRangeCommand(sheet1.Id, sourceRange, destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var h1Cell = sheet1.GetCell(h1)!;
        h1Cell.FormulaText.Should().Be("SUM(Sheet2!A10:A14)",
            "the reference must follow the cut cells to their new sheet");

        h1Cell.LegacyArrayRows.Should().Be(2u,
            "the cross-sheet 'other formula follows the moved cell' rewrite is not a fresh user " +
            "edit and must not strip the array's legacy fixed-extent identity");
        h1Cell.LegacyArrayCols.Should().Be(1u);

        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, [h1]);
        sheet1.GetValue(h1).Should().Be(new NumberValue(15), "H1 still computes correctly");
        sheet1.GetValue(h2).Should().Be(new NumberValue(15),
            "H2 must still be replicated instead of silently going blank now that LegacyArrayRows " +
            "survived the cross-sheet rewrite");

        sheet1.TryGetArrayExtent(h2, out var anchor, out var rows, out var cols).Should().BeTrue(
            "H2 must still be recognized as a declared array member after the cross-sheet cut");
        anchor.Should().Be(h1);
        rows.Should().Be(2u);
        cols.Should().Be(1u);

        CommandGuards.RejectIfSplitsArray(sheet1, [h2]).Should().NotBeNull(
            "'You cannot change part of an array' must still be enforced for the surviving " +
            "non-anchor array member after a cross-sheet cut elsewhere in the workbook");

        // Undo restores the pre-move text (already routed through the R153-fixed RestoreFormulas).
        command.Revert(context);
        sheet1.GetCell(h1)!.FormulaText.Should().Be("SUM(A10:A14)");
        sheet1.GetCell(h1)!.LegacyArrayRows.Should().Be(2u);
    }

    /// <summary>No-regression sibling: an ordinary (non-array) formula elsewhere is rewritten exactly
    /// as R38 already pins, unaffected by routing the assignment through the preserving helper.</summary>
    [Fact]
    public void CrossSheetCut_OtherFormulaElsewhereIsOrdinary_StillRewritesNormally()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var b2 = new CellAddress(sheet1.Id, 2, 2);
        var c5 = new CellAddress(sheet1.Id, 5, 3);
        sheet1.SetCell(b2, new NumberValue(42));
        sheet1.SetFormula(c5, "B2");

        var destination = new CellAddress(sheet2.Id, 4, 4);
        var command = new MoveRangeCommand(sheet1.Id, new GridRange(b2, b2), destination);

        command.Apply(context).Success.Should().BeTrue();

        var c5Cell = sheet1.GetCell(c5)!;
        c5Cell.FormulaText.Should().Be("Sheet2!D4");
        c5Cell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        c5Cell.LegacyArrayRows.Should().Be(0u);
        c5Cell.LegacyArrayCols.Should().Be(0u);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CaptureSourcePayloads: the MOVED cell's own formula IS the legacy array's anchor.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CrossSheetCut_MovedCellIsLegacyArrayAnchorItself_PreservesArrayIdentityAtDestination()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        for (uint r = 10; r <= 14; r++)
            sheet1.SetCell(new CellAddress(sheet1.Id, r, 1), new NumberValue(r - 9));

        // The array's anchor cell (H1) is itself the cell being cut this time.
        var h1 = new CellAddress(sheet1.Id, 1, 8);
        var legacyCell = Cell.FromFormula("SUM(A10:A14)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet1.SetCell(h1, legacyCell);

        var destination = new CellAddress(sheet2.Id, 1, 8); // same row/col, different sheet
        var command = new MoveRangeCommand(sheet1.Id, new GridRange(h1, h1), destination);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet1.GetCell(h1).Should().BeNull("the source cell is vacated by the cut");
        var movedCell = sheet2.GetCell(destination)!;
        movedCell.FormulaText.Should().Be("SUM(Sheet1!A10:A14)",
            "moving the array's own anchor keeps it pointing at the same source range, gaining an " +
            "explicit sheet qualifier since it now lives on Sheet2");
        movedCell.LegacyArrayRows.Should().Be(2u,
            "CaptureSourcePayloads clones the cell (which preserves the legacy extent) and must not " +
            "immediately strip it again via a same-text FormulaText reassignment");
        movedCell.LegacyArrayCols.Should().Be(1u);
    }

    /// <summary>No-regression sibling: moving an ordinary (non-array) formula cell across sheets is
    /// unaffected -- its formula text still gets the sheet qualifier and it stays a plain Dynamic
    /// formula.</summary>
    [Fact]
    public void CrossSheetCut_MovedCellIsOrdinaryFormula_StillRewritesTextAndStaysDynamic()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet1.Id, 1, 1);
        var b2 = new CellAddress(sheet1.Id, 2, 2);
        sheet1.SetCell(a1, new NumberValue(100));
        sheet1.SetFormula(b2, "$A$1");

        var destination = new CellAddress(sheet2.Id, 4, 4);
        var command = new MoveRangeCommand(sheet1.Id, new GridRange(b2, b2), destination);

        command.Apply(context).Success.Should().BeTrue();

        var movedCell = sheet2.GetCell(destination)!;
        movedCell.FormulaText.Should().Be("Sheet1!$A$1");
        movedCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        movedCell.LegacyArrayRows.Should().Be(0u);
        movedCell.LegacyArrayCols.Should().Be(0u);
    }
}
