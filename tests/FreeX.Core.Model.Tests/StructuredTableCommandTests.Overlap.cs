using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class StructuredTableCommandTests
{
    // ── Overlap guard: CreateStructuredTableCommand ────────────────────────────

    [Fact]
    public void CreateStructuredTableCommand_RejectsRangeOverlappingExistingTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var existing = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3))
        };
        sheet.StructuredTables.Add(existing);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Col"));
        var ctx = new TestCommandContext(wb);

        // Overlapping range: shares rows 3-5, cols 2-4 with existing table
        var overlap = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 6, 4));
        var outcome = new CreateStructuredTableCommand(sheet.Id, overlap, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("cannot overlap");
        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }

    [Fact]
    public void CreateStructuredTableCommand_AllowsAdjacentNonOverlappingTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var existing = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2))
        };
        sheet.StructuredTables.Add(existing);
        // Plant a header cell for the new table that starts at col 3 (adjacent, not overlapping)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Header"));
        var ctx = new TestCommandContext(wb);

        // Adjacent range starting at col 3 — touches col 2's right edge but does not overlap
        var adjacent = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4));
        var outcome = new CreateStructuredTableCommand(sheet.Id, adjacent, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().HaveCount(2);
    }

    [Fact]
    public void CreateStructuredTableCommand_UndoUnaffectedAfterOverlapRejection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var existing = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3))
        };
        sheet.StructuredTables.Add(existing);
        var ctx = new TestCommandContext(wb);
        var command = new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 7, 4)),
            "TableStyleMedium2");

        command.Apply(ctx).Success.Should().BeFalse();
        // Revert on a failed Apply should leave state unchanged
        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }

    // ── Overlap guard: ResizeStructuredTableCommand ────────────────────────────

    [Fact]
    public void ResizeStructuredTableCommand_RejectsNewRangeOverlappingOtherTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var table1 = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            Columns = { new StructuredTableColumnModel(1, "A"), new StructuredTableColumnModel(2, "B") }
        };
        var table2 = new StructuredTableModel
        {
            Id = 2,
            Name = "Table2",
            DisplayName = "Table2",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7)),
            Columns = { new StructuredTableColumnModel(1, "X"), new StructuredTableColumnModel(2, "Y"), new StructuredTableColumnModel(3, "Z") }
        };
        sheet.StructuredTables.Add(table1);
        sheet.StructuredTables.Add(table2);
        var ctx = new TestCommandContext(wb);

        // Try to resize table1 so it extends into table2's columns
        var overlappingRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 6));
        var outcome = new ResizeStructuredTableCommand(sheet.Id, table1.Id, overlappingRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("cannot overlap");
        // Table1 range must be unchanged
        sheet.StructuredTables.First(t => t.Id == 1).Range.Should().Be(table1.Range);
    }

    [Fact]
    public void ResizeStructuredTableCommand_AllowsResizeWithinOwnFootprint()
    {
        // Resizing a table whose new range still overlaps only its own old range must succeed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Columns = { new StructuredTableColumnModel(1, "Region"), new StructuredTableColumnModel(2, "Sales") }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);

        // Shrink: same start, fewer rows — entirely within the old range
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var outcome = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Single().Range.Should().Be(newRange);
    }

    [Fact]
    public void ResizeStructuredTableCommand_UndoRestoresPreviousRangeAfterOverlapRejection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        var table1 = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Columns = { new StructuredTableColumnModel(1, "A"), new StructuredTableColumnModel(2, "B") }
        };
        var table2 = new StructuredTableModel
        {
            Id = 2,
            Name = "Table2",
            DisplayName = "Table2",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 4, 5)),
            Columns = { new StructuredTableColumnModel(1, "C"), new StructuredTableColumnModel(2, "D") }
        };
        sheet.StructuredTables.Add(table1);
        sheet.StructuredTables.Add(table2);
        var ctx = new TestCommandContext(wb);
        var originalRange = table1.Range;
        var command = new ResizeStructuredTableCommand(
            sheet.Id,
            table1.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 5)));

        command.Apply(ctx).Success.Should().BeFalse();
        command.Revert(ctx);

        sheet.StructuredTables.First(t => t.Id == 1).Range.Should().Be(originalRange);
    }
}
