using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// Round-65 fix (R65-calc-array-spill-6-1): CreateStructuredTableCommand.Apply only rejected a
// range overlapping ANOTHER table, never a range overlapping a LIVE dynamic-array spill. Excel
// forbids creating a table over a spill ("You cannot create a table that overlaps a spilled array
// range") because the table would absorb the spill members as static table data and the next
// recalculation would then turn the spill anchor into #SPILL! and blank the members.
public sealed partial class StructuredTableCommandTests
{
    private static void SeedHorizontalSpill(Sheet sheet, CellAddress anchor, int cols)
    {
        var cells = new ScalarValue[1, 3];
        for (var i = 0; i < cols; i++)
            cells[0, i] = new NumberValue(i + 1);
        sheet.SetSpillRange(anchor, new RangeValue(cells));
    }

    [Fact]
    public void CreateStructuredTableCommand_RejectsRangeOverlappingLiveSpill()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // A1 = SEQUENCE(3) spilling down A1:A3 (anchor holds the formula's own first value; A2:A3
        // carry live spill values registered via SetSpillRange, mirroring what RecalcEngine does).
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillCells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));
        var ctx = new TestCommandContext(wb);

        // Table range A1:B4 fully covers the spill footprint (A1:A3).
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        var outcome = new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("spill");
        sheet.StructuredTables.Should().BeEmpty();
    }

    [Fact]
    public void CreateStructuredTableCommand_UndoUnaffectedAfterSpillOverlapRejection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillCells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));
        var ctx = new TestCommandContext(wb);
        var command = new CreateStructuredTableCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            "TableStyleMedium2");

        command.Apply(ctx).Success.Should().BeFalse();
        command.Revert(ctx);

        sheet.StructuredTables.Should().BeEmpty();
    }

    [Fact]
    public void CreateStructuredTableCommand_AllowsRangeWithNoSpill()
    {
        // Sibling no-regression: an ordinary range with no live spill anywhere near it still
        // succeeds exactly as before the fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var outcome = new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle();
    }

    [Fact]
    public void CreateStructuredTableCommand_AllowsRangeAdjacentToButNotOverlappingSpill()
    {
        // Sibling no-regression: a table range that sits next to a live spill, but does not
        // actually overlap any of its cells, must still be allowed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(1,3)"));
        SeedHorizontalSpill(sheet, anchor, 3); // spills A1:C1 (A1 anchor, B1:C1 live spill cells)

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Value"));
        var ctx = new TestCommandContext(wb);

        // Table range on rows 3-5 - well below the spill's row 1 footprint.
        var range = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 5, 2));
        var outcome = new CreateStructuredTableCommand(sheet.Id, range, "TableStyleMedium2").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.StructuredTables.Should().ContainSingle();
    }
}
