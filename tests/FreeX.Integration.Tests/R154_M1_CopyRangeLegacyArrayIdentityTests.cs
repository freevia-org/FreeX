using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R154/meta-F1: CopyRangeCommand.CaptureSourcePayloads (backs Ctrl+drag-copy) cloned the source cell
/// (which correctly preserves ArrayMode/LegacyArrayRows/LegacyArrayCols, see Cell.Clone) and then
/// immediately reassigned <c>cell.FormulaText</c> to the rewritten reference text -- silently undoing
/// what Clone() had just preserved, because the FormulaText setter (Cell.cs) unconditionally resets
/// those three properties on every assignment. Fixed by routing the reassignment through
/// RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity, exactly like the R153/K1 remediation
/// already did for MoveRangeCommand/DuplicateSheetCommand's equivalent loops.
/// </summary>
public sealed class R154_M1_CopyRangeLegacyArrayIdentityTests
{
    [Fact]
    public void CtrlDragCopy_OfLegacyArrayFormulaCell_PreservesArrayIdentity()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        for (uint r = 10; r <= 14; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 4), new NumberValue(r - 9)); // D10:D14 = 1..5

        var h1 = new CellAddress(sheet.Id, 1, 8);
        var legacyCell = Cell.FromFormula("SUM(D10:D14)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);

        var destination = new CellAddress(sheet.Id, 1, 11); // K1: colDelta +3
        var command = new CopyRangeCommand(sheet.Id, new GridRange(h1, h1), destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var copied = sheet.GetCell(destination)!;
        copied.FormulaText.Should().Be("SUM(G10:G14)");
        copied.LegacyArrayRows.Should().Be(2u,
            "Ctrl+drag-copy of a legacy CSE array formula must not strip its fixed-extent identity");
        copied.LegacyArrayCols.Should().Be(1u);

        // Source untouched by a copy.
        sheet.GetCell(h1)!.LegacyArrayRows.Should().Be(2u);
    }

    [Fact]
    public void CtrlDragCopy_OfOrdinaryFormulaCell_StillRewritesAndStaysDynamic_NoRegression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(7));
        sheet.SetFormula(b1, "A1*2");

        var destination = new CellAddress(sheet.Id, 1, 4);
        var command = new CopyRangeCommand(sheet.Id, new GridRange(b1, b1), destination);

        command.Apply(ctx).Success.Should().BeTrue();

        var copied = sheet.GetCell(destination)!;
        copied.FormulaText.Should().Be("C1*2");
        copied.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        copied.LegacyArrayRows.Should().Be(0u);
        copied.LegacyArrayCols.Should().Be(0u);
    }
}
