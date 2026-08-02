using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R115: Excel requires a table to have exactly one discrete cell per row/column intersection, so
/// it refuses both to create a table over a range containing a merged cell and to resize an
/// existing table into one -- symmetric with MergeCellsCommand.Apply's existing
/// "Cannot merge cells that overlap a table" guard (MergeCellsCommand.cs), enforced from the
/// other direction. Before this fix, CreateStructuredTableCommand and ResizeStructuredTableCommand
/// checked for overlap against other tables and live spill ranges but never against
/// sheet.MergedRegions, so a user could Merge Cells first and then successfully Insert Table (or
/// Format as Table / resize an existing table) over the same range.
/// </summary>
public sealed class R115_StructuredTableMergedRegionOverlapTests
{
    [Fact]
    public void CreateStructuredTableCommand_RejectsRangeOverlappingMergedRegion()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        // Merge B2:C2 (row 2, cols 2-3) -- sits inside the data body of the candidate table range.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 3)));
        var ctx = new TestCommandContext(wb);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var outcome = new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged cell");
        sheet.StructuredTables.Should().BeEmpty();
    }

    [Fact]
    public void CreateStructuredTableCommand_AllowsRangeNotOverlappingMergedRegion()
    {
        // No-regression sibling: a merged region OUTSIDE the candidate range must not block table creation.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        // Merge sits far outside the table range (cols 10-11), so it must not interfere.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 10, 10), new CellAddress(sheet.Id, 10, 11)));
        var ctx = new TestCommandContext(wb);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var outcome = new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle();
    }

    [Fact]
    public void ResizeStructuredTableCommand_RejectsNewRangeOverlappingMergedRegion()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Columns = { new StructuredTableColumnModel(1, "A"), new StructuredTableColumnModel(2, "B") }
        };
        sheet.StructuredTables.Add(table);
        // A merged region sitting in rows 4-5, which the resize below tries to grow into.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 5, 1)));
        var ctx = new TestCommandContext(wb);
        var originalRange = table.Range;

        // Grow the table downward into the merged region.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        var outcome = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("merged cell");
        sheet.StructuredTables.First(t => t.Id == 1).Range.Should().Be(originalRange);
    }

    [Fact]
    public void ResizeStructuredTableCommand_AllowsGrowingAwayFromMergedRegion()
    {
        // No-regression sibling: growing in a direction/column set that never touches the merged
        // region must still succeed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Columns = { new StructuredTableColumnModel(1, "A"), new StructuredTableColumnModel(2, "B") }
        };
        sheet.StructuredTables.Add(table);
        // Merged region is in column 5, well outside the table's columns 1-2 even after growth.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 4, 5), new CellAddress(sheet.Id, 5, 5)));
        var ctx = new TestCommandContext(wb);

        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2));
        var outcome = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Single().Range.Should().Be(newRange);
    }
}
