using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R128: ResizeStructuredTableCommand.Apply validated protection, table-overlap, and merged-region
// overlap, but never checked overlap with a LIVE dynamic-array spill range -- unlike its sibling
// CreateStructuredTableCommand.Apply (see StructuredTableCommandTests.SpillOverlap.cs, the
// Round-65 fix). Growing a table into a range that holds a live spill silently succeeded; the next
// time that spill's anchor recalculated, Sheet.IsSpillBlocked would treat the table's Range as
// occupying the spill footprint (anchor included) and turn the anchor into #SPILL!, permanently
// blanking the previously-spilled members (RecalcEngine.ClearSpillRange already ran by then).
// Fixed by sharing CommandGuards.RejectIfStructuredTableRangeOverlapsSpill between Create and
// Resize so the two call sites cannot drift apart again.
public sealed partial class StructuredTableCommandTests
{
    [Fact]
    public void R128_ResizeStructuredTableCommand_RejectsRangeGrowingIntoLiveSpill()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet); // fills A1:B5 (Region/Status header + data)
        var table = CreateSalesTable(sheet); // Range A1:B5
        sheet.StructuredTables.Add(table);

        // D1 = SEQUENCE(3) spilling down D1:D3 -- D1 is the anchor (holds the formula's own first
        // value), D2:D3 carry live spill values registered via SetSpillRange, mirroring what
        // RecalcEngine does after a real recalculation.
        var anchor = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillCells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));
        var ctx = new TestCommandContext(wb);

        // Growing the table from A1:B5 to A1:D6 pulls column D (rows 1-3) into the table's range,
        // overlapping the live spill's member cells D2:D3.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("spill");
        var unchanged = sheet.StructuredTables.Should().ContainSingle().Subject;
        unchanged.Range.Should().Be(table.Range);

        // Undo after a rejected Apply must be a no-op (nothing was mutated to revert).
        command.Revert(ctx);
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(table.Range);
    }

    [Fact]
    public void R128_ResizeStructuredTableCommand_AllowsGrowingAdjacentToButNotOverlappingSpill()
    {
        // Sibling no-regression: a resize that grows the table right up to, but not into, a live
        // spill's footprint must still succeed exactly as before the fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTable(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Notes"));
        var table = CreateSalesTable(sheet); // Range A1:B5
        sheet.StructuredTables.Add(table);

        var anchor = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3)"));
        var spillCells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(spillCells));
        var ctx = new TestCommandContext(wb);

        // Grow only to column C -- column D (the spill's footprint) is left untouched.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.Range.Should().Be(newRange);
    }
}
