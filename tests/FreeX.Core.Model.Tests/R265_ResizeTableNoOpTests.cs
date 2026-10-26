using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r265: Resize Table. r232 grouped it with the cell-writing commands as needing "a comparison per
/// cell"; it needs that and six more, because Revert restores the delegated totals refresh, the
/// captured cells, four filter-state collections and the table model itself.
///
/// <para>The table half is the one that could not be done before: every structural edit goes through
/// <c>CopyTable</c>, which builds a NEW instance, so reference equality there can never report
/// unchanged and a content comparison over all twenty-seven members was the prerequisite.</para>
/// </summary>
public sealed class R265_ResizeTableNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Sheet Sheet, TestCommandContext Ctx, StructuredTableModel Table) SetUpTable()
    {
        var wb = new Workbook("R265");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Item"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("One"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Two"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("Three"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, "A1", "B3"),
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Item"), new StructuredTableColumnModel(2, "Amount") },
        };
        sheet.StructuredTables.Add(table);

        return (sheet, new TestCommandContext(wb), table);
    }

    [Fact]
    public void ResizingToTheSameRangeIsANoOp()
    {
        var (sheet, ctx, table) = SetUpTable();

        new ResizeStructuredTableCommand(sheet.Id, table.Id, Range(sheet, "A1", "B3")).Apply(ctx)
            .IsNoOp.Should().BeTrue("the table already occupies exactly this range");
        sheet.StructuredTables[0].Range.Should().Be(Range(sheet, "A1", "B3"));
    }

    [Fact]
    public void GrowingTheTableIsNotANoOp()
    {
        var (sheet, ctx, table) = SetUpTable();

        new ResizeStructuredTableCommand(sheet.Id, table.Id, Range(sheet, "A1", "B4")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the table takes in another data row");
        sheet.StructuredTables[0].Range.Should().Be(Range(sheet, "A1", "B4"));
    }

    [Fact]
    public void ShrinkingTheTableIsNotANoOp()
    {
        var (sheet, ctx, table) = SetUpTable();

        new ResizeStructuredTableCommand(sheet.Id, table.Id, Range(sheet, "A1", "B2")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the table gives up a data row");
    }

    /// <summary>
    /// The table half in isolation: narrowing by a column changes the table's own Columns list, and
    /// the cell comparison alone would not see it -- the cells themselves keep their values, they
    /// just stop being part of the table.
    /// </summary>
    [Fact]
    public void NarrowingTheTableByAColumnIsNotANoOp()
    {
        var (sheet, ctx, table) = SetUpTable();

        new ResizeStructuredTableCommand(sheet.Id, table.Id, Range(sheet, "A1", "A3")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the Amount column leaves the table");
        sheet.StructuredTables[0].Columns.Should().ContainSingle();
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10), "the cell keeps its value, it just leaves the table");
    }
}
