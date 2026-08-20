using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R154/meta-F1: SortCommand.WriteCellClone (backs Data &gt; Sort, N37: a sort permutes each cell to a
/// new row/column exactly like a per-cell cut/paste and rewrites relative references by the distance
/// moved) cloned the source cell (which correctly preserves ArrayMode/LegacyArrayRows/LegacyArrayCols,
/// see Cell.Clone) and then immediately reassigned <c>clone.FormulaText</c> to the rewritten reference
/// text -- silently undoing what Clone() had just preserved, because the FormulaText setter (Cell.cs)
/// unconditionally resets those three properties on every assignment. Fixed by routing the
/// reassignment through RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity, exactly like the
/// R153/K1 remediation already did for MoveRangeCommand/DuplicateSheetCommand's equivalent loops.
/// </summary>
public sealed class R154_M1_SortLegacyArrayIdentityTests
{
    [Fact]
    public void Sort_PermutesRowContainingLegacyArrayFormula_PreservesArrayIdentity()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Sort key column A: row1=2, row2=1 -- ascending sort swaps the two rows.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));

        // Some data the array formula (in column B) sums, one source cell per row so its own-row
        // reference travels with the row during the sort.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100)); // C1
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(200)); // C2

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var legacyCell = Cell.FromFormula("SUM(C1:C1)");
        legacyCell.LegacyArrayRows = 1;
        legacyCell.LegacyArrayCols = 2;
        sheet.SetCell(b1, legacyCell);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3));
        var command = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Row1 (key=2) now sits at row2; its own-row reference "C1" travels to "C2".
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var moved = sheet.GetCell(b2)!;
        moved.FormulaText.Should().Be("SUM(C2:C2)");
        moved.LegacyArrayRows.Should().Be(1u,
            "Data > Sort permuting a row containing a legacy CSE array formula must not strip its " +
            "fixed-extent identity");
        moved.LegacyArrayCols.Should().Be(2u);
    }

    [Fact]
    public void Sort_PermutesRowContainingOrdinaryFormula_StillRewritesAndStaysDynamic_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(200));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "C1*2");

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3));
        var command = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var moved = sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!;
        moved.FormulaText.Should().Be("C2*2");
        moved.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        moved.LegacyArrayRows.Should().Be(0u);
        moved.LegacyArrayCols.Should().Be(0u);
    }
}
