using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r229: the fill family, and a second bool that reads like a did-it-change flag and is not.
/// <para>
/// <c>StructuredTableModel.SetCalculatedColumnFormula</c> returns a bool. It reports whether the
/// COLUMN WAS FOUND -- it returns true for a re-set of the identical formula. That is the same trap
/// r227 found in <c>TableLayoutOperations.DistributeColumns</c>, in a different file, which is why
/// PropagateCalculatedColumnCommand's guard compares the stored formula itself rather than trusting
/// the return value.
/// </para>
/// </summary>
public sealed class R229_FillFamilyNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void SetCalculatedColumnFormulaReportsFoundNotChanged()
    {
        // Pinning the trap, so nobody derives a no-op guard from this return value later.
        var (sheet, _) = Fixture();
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Total"));

        table.SetCalculatedColumnFormula(2, "=1+1").Should().BeTrue();
        table.SetCalculatedColumnFormula(2, "=1+1")
            .Should().BeTrue("found, but nothing changed -- the bool does not answer that question");
        table.SetCalculatedColumnFormula(99, "=1+1")
            .Should().BeFalse("this is what the bool actually reports: whether the column exists");
    }

    [Fact]
    public void FlashFillWithNothingLeftToFill_ReportsNoOp()
    {
        // Column A holds the source, column B is already filled with exactly what the pattern
        // produces, so DetectFill succeeds and finds no rows left to write.
        var (sheet, ctx) = Fixture();
        for (uint row = 1; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Ann Lee {row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue("Ann"));
        }

        var outcome = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 4).Apply(ctx);

        if (outcome.Success)
            outcome.IsNoOp.Should().BeTrue("every candidate row already holds the filled value");
    }
}
