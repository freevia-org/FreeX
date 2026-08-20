using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R153-array-legacy-F1: Cell.FormulaText's setter unconditionally resets ArrayMode to Dynamic and
/// LegacyArrayRows/LegacyArrayCols to 0 on every assignment, on the assumption that assigning
/// FormulaText always means a fresh user edit. But RowColumnShiftHelpers.RewriteAllFormulas (driven
/// by InsertRowsCommand/DeleteRowsCommand/InsertColumnsCommand/DeleteColumnsCommand,
/// RowColumnMutationSnapshot.RewriteReferences, MoveRangeCommand, SheetCommands' rename, and
/// StructuredTableDesignCommands' rename) reassigns that same setter on the EXISTING Cell object to
/// adjust reference text after a structural edit elsewhere -- not a user edit -- which used to
/// silently strip a legacy CSE array formula's fixed-extent identity (and the "You cannot change
/// part of an array" split guard that depends on it) the moment any such edit happened to shift a
/// cell the array's formula references.
/// </summary>
public sealed class R153_LegacyArrayFormulaSurvivesStructuralRewriteTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet, CellAddress h1, CellAddress h2)
        BuildLegacyCseArrayWorkbook()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A10:A14 = 1..5.
        for (uint r = 10; r <= 14; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r - 9));

        // H1:H2 (2 rows x 1 col) CSE-entered as {=SUM(A10:A14)}: a fixed-extent legacy array
        // formula whose natural result is a scalar, replicated into both declared cells.
        var h1 = new CellAddress(sheet.Id, 1, 8);
        var h2 = new CellAddress(sheet.Id, 2, 8);
        var legacyCell = Cell.FromFormula("SUM(A10:A14)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [h1]);

        sheet.GetValue(1, 8).Should().Be(new NumberValue(15), "sanity: H1 gets the SUM result");
        sheet.GetValue(2, 8).Should().Be(new NumberValue(15), "sanity: H2 is replicated");
        sheet.TryGetArrayExtent(h2, out _, out _, out _).Should().BeTrue(
            "sanity: H2 is recognized as an array member before the structural edit");

        return (engine, wb, sheet, h1, h2);
    }

    [Fact]
    public void InsertRowsBelowArray_ShiftsReferencedRange_ButPreservesLegacyArrayExtentAndGuard()
    {
        var (engine, wb, sheet, h1, h2) = BuildLegacyCseArrayWorkbook();
        var ctx = new TestCommandContext(wb);

        // An ordinary row insert far below the array's own row: it only shifts the REFERENCED rows
        // 10-14 to 11-15 and does not relocate the array's anchor cell (H1) at all.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var h1Cell = sheet.GetCell(h1);
        h1Cell.Should().NotBeNull();
        h1Cell!.FormulaText.Should().Be("SUM(A11:A15)", "the reference inside the formula shifted");

        // The core assertion: the array's fixed-extent identity must survive the reference rewrite.
        h1Cell.LegacyArrayRows.Should().Be(2u,
            "an automatic reference-adjustment rewrite is not a fresh user edit and must not strip " +
            "the array's legacy fixed-extent identity");
        h1Cell.LegacyArrayCols.Should().Be(1u);

        engine.Recalculate(wb, [h1]);

        sheet.GetValue(1, 8).Should().Be(new NumberValue(15), "H1 still computes correctly");
        sheet.GetValue(2, 8).Should().Be(new NumberValue(15),
            "H2 must still be replicated with the array's scalar result instead of silently going " +
            "blank now that LegacyArrayRows survived the rewrite");

        sheet.TryGetArrayExtent(h2, out var anchor, out var rows, out var cols).Should().BeTrue(
            "H2 must still be recognized as a declared array member after the structural edit");
        anchor.Should().Be(h1);
        rows.Should().Be(2u);
        cols.Should().Be(1u);

        // And the split guard real edit commands consult must still block writing into H2 alone.
        CommandGuards.RejectIfSplitsArray(sheet, [h2]).Should().NotBeNull(
            "'You cannot change part of an array' must still be enforced for a non-anchor member " +
            "of the surviving legacy CSE array");
    }

    [Fact]
    public void UndoInsertRows_RestoresOriginalFormulaText_AndKeepsLegacyArrayExtent()
    {
        var (_, wb, sheet, h1, h2) = BuildLegacyCseArrayWorkbook();
        var ctx = new TestCommandContext(wb);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();
        command.Revert(ctx);

        var h1Cell = sheet.GetCell(h1);
        h1Cell.Should().NotBeNull();
        h1Cell!.FormulaText.Should().Be("SUM(A10:A14)", "Undo restores the original reference text");
        h1Cell.LegacyArrayRows.Should().Be(2u,
            "restoring the pre-edit formula text on Undo must not strip the array's legacy extent " +
            "either -- RestoreFormulas reassigns the same setter RewriteAllFormulas does");
        h1Cell.LegacyArrayCols.Should().Be(1u);

        sheet.TryGetArrayExtent(h2, out _, out _, out _).Should().BeTrue(
            "H2 must still be recognized as an array member after Undo");
    }

    /// <summary>
    /// No-regression sibling: a genuine user edit of FormulaText (not a structural
    /// reference-adjustment rewrite) is unaffected by this fix and must still clear a cell's legacy
    /// array identity exactly as documented on <see cref="Cell.FormulaText"/> -- re-typing a formula
    /// into a cell that used to hold a legacy CSE array always produces a modern (Dynamic, unbounded)
    /// formula, never keeping the old fixed extent.
    /// </summary>
    [Fact]
    public void DirectUserEditOfFormulaText_StillClearsLegacyArrayIdentity()
    {
        var cell = Cell.FromFormula("SUM(A10:A14)");
        cell.LegacyArrayRows = 2;
        cell.LegacyArrayCols = 1;

        cell.FormulaText = "SUM(A1:A9)";

        cell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        cell.LegacyArrayRows.Should().Be(0u,
            "a direct FormulaText assignment representing a real user edit must still reset the " +
            "legacy array extent -- only RowColumnShiftHelpers' automatic reference rewrites are " +
            "exempted from this");
        cell.LegacyArrayCols.Should().Be(0u);
    }
}
