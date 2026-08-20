using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R154/meta-F1: FillCellsCommand.CloneForTarget (backs the autofill drag handle) cloned the source
/// cell (which correctly preserves ArrayMode/LegacyArrayRows/LegacyArrayCols, see Cell.Clone) and then
/// immediately reassigned <c>result.FormulaText</c> to the rewritten reference text -- silently undoing
/// what Clone() had just preserved, because the FormulaText setter (Cell.cs) unconditionally resets
/// those three properties on every assignment. Fixed by routing the reassignment through
/// RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity, exactly like the R153/K1 remediation
/// already did for MoveRangeCommand/DuplicateSheetCommand's equivalent loops.
/// </summary>
public sealed class R154_M1_FillCellsLegacyArrayIdentityTests
{
    [Fact]
    public void FillDown_OfLegacyArrayFormulaCell_PreservesArrayIdentity()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        for (uint r = 1; r <= 3; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r)); // A1:A3 = 1,2,3

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var legacyCell = Cell.FromFormula("SUM(A1:A1)");
        legacyCell.LegacyArrayRows = 1;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(b1, legacyCell);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(b1, new CellAddress(sheet.Id, 3, 2)),
            FillCellsDirection.Down);

        command.Apply(ctx).Success.Should().BeTrue();

        var b2 = new CellAddress(sheet.Id, 2, 2);
        var filled = sheet.GetCell(b2)!;
        filled.FormulaText.Should().Be("SUM(A2:A2)");
        filled.LegacyArrayRows.Should().Be(1u,
            "the autofill drag handle must not strip a legacy CSE array formula's fixed-extent identity");
        filled.LegacyArrayCols.Should().Be(1u);
    }

    [Fact]
    public void FillDown_OfOrdinaryFormulaCell_StillRewritesAndStaysDynamic_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("B1+$C$1"));

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, new CellAddress(sheet.Id, 3, 1)),
            FillCellsDirection.Down);

        command.Apply(ctx).Success.Should().BeTrue();

        var filled = sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!;
        filled.FormulaText.Should().Be("B2+$C$1");
        filled.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        filled.LegacyArrayRows.Should().Be(0u);
        filled.LegacyArrayCols.Should().Be(0u);
    }
}
