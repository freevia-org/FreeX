using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r234: EditCellsCommand -- the first command through the cell-change comparison r233 diagnosed as
/// the shared blocker behind thirteen entries on the known-broken list.
/// <para>
/// Those thirteen all write into a target set their guards have already established is non-empty, so
/// the post-hoc "did we write anything" test that fixed their neighbours is always true for them.
/// What they need is "did the written values DIFFER", and that could not be asked because Cell is a
/// mutable class with reference equality only. The comparison now lives in
/// CellEditCompanionSnapshot -- which these commands already build for undo -- rather than as an
/// equality override on Cell, whose identity semantics are relied on throughout the model.
/// </para>
/// <para>
/// Typing a cell's existing value back into it is the everyday gesture here: select a cell, retype
/// what it says, press Enter.
/// </para>
/// </summary>
public sealed class R234_EditCellsNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static CellAddress At(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    [Fact]
    public void RetypingACellsExistingValue_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = At(sheet, 2, 2);
        sheet.SetCell(address, new TextValue("Region"));

        new EditCellsCommand(sheet.Id, [(address, Cell.FromValue(new TextValue("Region")))])
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void TypingADifferentValue_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = At(sheet, 2, 2);
        sheet.SetCell(address, new TextValue("Region"));

        var outcome = new EditCellsCommand(
            sheet.Id, [(address, Cell.FromValue(new TextValue("Area")))]).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(2, 2).Should().Be(new TextValue("Area"));
    }

    [Fact]
    public void RetypingTheSameFormula_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = At(sheet, 2, 2);
        sheet.SetCell(address, Cell.FromFormula("=1+1"));

        new EditCellsCommand(sheet.Id, [(address, Cell.FromFormula("=1+1"))]).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingAFormula_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = At(sheet, 2, 2);
        sheet.SetCell(address, Cell.FromFormula("=1+1"));

        new EditCellsCommand(sheet.Id, [(address, Cell.FromFormula("=1+2"))]).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void WritingIntoAnEmptyCell_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        new EditCellsCommand(sheet.Id, [(At(sheet, 5, 5), Cell.FromValue(new NumberValue(1)))])
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void WritingABlankIntoACellThatDoesNotExist_IsStillAnEdit()
    {
        // A boundary worth pinning rather than assuming either way. I expected this to be a no-op
        // -- blank over nothing looks like nothing -- and the guard says otherwise, correctly: the
        // sheet had no Cell object at that address and now has one, which moves the used range and
        // changes what gets written to the file. The displayed value is the same; the model is not.
        // Pressing Delete over empty cells, which is the actual user gesture here, goes through
        // ClearContentsCommand and was guarded in r220.
        var (sheet, ctx) = Fixture();

        new EditCellsCommand(sheet.Id, [(At(sheet, 5, 5), Cell.FromValue(BlankValue.Instance))])
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void OneUnchangedCellAmongChangedOnes_IsStillARealEdit()
    {
        // TrueForAll, not Any: a batch is a no-op only when EVERY cell in it is unchanged. Getting
        // this backwards would suppress a multi-cell edit because one of its cells happened to match.
        var (sheet, ctx) = Fixture();
        sheet.SetCell(At(sheet, 1, 1), new TextValue("same"));

        var outcome = new EditCellsCommand(
            sheet.Id,
            [
                (At(sheet, 1, 1), Cell.FromValue(new TextValue("same"))),
                (At(sheet, 1, 2), Cell.FromValue(new TextValue("new")))
            ]).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(1, 2).Should().Be(new TextValue("new"));
    }
}
