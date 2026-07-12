using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R31-commands-merge-autofit-deep-3: MergeCellsCommand never rejected a merge overlapping a
/// structured (Excel Table) region. Real Excel disables Merge Cells/Merge &amp; Center entirely
/// for any selection touching a Table, because merging would corrupt the table's one-row-per-record
/// structure. Verifies the fix rejects a merge overlapping a table, while a merge with no table
/// overlap still succeeds (sibling case the fix must not break).
/// </summary>
public sealed class R31_MergeCellsTableOverlapTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static StructuredTableModel AddTable(Sheet sheet, GridRange range)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
        };
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void Merge_RejectsRangeFullyInsideTable()
    {
        var (_, sheet, ctx) = Setup();
        // Table1 spans B2:C10.
        AddTable(sheet, new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 10, 3)));

        // Merge B3:C3, fully inside the table body.
        var range = new GridRange(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3));
        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("table");
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void Merge_RejectsRangePartiallyOverlappingTable()
    {
        var (_, sheet, ctx) = Setup();
        // Table1 spans B2:C10.
        AddTable(sheet, new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 10, 3)));

        // Merge A1:B2 -- only B2 (the table's header cell) overlaps.
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void Merge_AllowsRangeOutsideTable()
    {
        var (_, sheet, ctx) = Setup();
        // Table1 spans B2:C10; the merge target (E1:F1) does not overlap it at all.
        AddTable(sheet, new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 10, 3)));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 1, 6));
        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }
}
