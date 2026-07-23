using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for R78-formula-dynamic-spill-5-1: MoveRangeCommand.Apply's spill-relocation
/// loop called Sheet.SetSpillRange unconditionally, without first checking Sheet.IsSpillBlocked as
/// SetSpillRange's own contract requires ("Does NOT check for blockage — call IsSpillBlocked first",
/// Sheet.cs). Moving just a live spill anchor onto a destination whose spill footprint overlaps
/// pre-existing unrelated content wrote live spill values straight past/around that content instead
/// of surfacing #SPILL! at the anchor, and left an orphaned spill-value entry that Sheet.GetValue's
/// _cells-before-_spillValues precedence masked only by accident (RecalcEngine's own spill-writing
/// branch, by contrast, already does this IsSpillBlocked check).
/// </summary>
public class R78_MoveRangeSpillBlockedTests
{
    private static (Workbook wb, Sheet sheet, CellAddress anchor) SetUpLiveSpill()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetFormula(anchor, "SEQUENCE(3,1)");
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills A1:A3 = 1,2,3
        return (wb, sheet, anchor);
    }

    [Fact]
    public void Apply_MovingSpillAnchorOntoBlockedDestination_ShowsSpillErrorAndDoesNotLeakPhantomValues()
    {
        var (wb, sheet, anchor) = SetUpLiveSpill();
        // D2 already holds unrelated content that will block the would-be spill footprint at D1:D3.
        var blocker = new CellAddress(sheet.Id, 2, 4); // D2
        sheet.SetCell(blocker, new TextValue("Hello"));
        var ctx = new TestCommandContext(wb);

        var anchorOnly = new GridRange(anchor, anchor); // A1 alone, NOT A1:A3
        var destination = new CellAddress(sheet.Id, 1, 4); // D1

        var command = new MoveRangeCommand(sheet.Id, anchorOnly, destination);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The anchor must surface #SPILL!, not the stale computed scalar carried over by the move.
        sheet.GetValue(1, 4).Should().Be(ErrorValue.Spill, "D1's spill footprint (D1:D3) is blocked by D2's content");
        sheet.TryGetSpillExtent(destination, out _, out _).Should().BeFalse(
            "a blocked anchor must not register a live spill extent");

        // The blocker and its neighbours must keep their real content -- no relocated spill values
        // written over or around them.
        sheet.GetValue(2, 4).Should().Be(new TextValue("Hello"), "the blocker's own content must be untouched");
        sheet.GetValue(3, 4).Should().Be(BlankValue.Instance,
            "D3 must stay blank, not show a phantom value 3 from a spill relocation that never legitimately happened");

        // The source array must still be fully vacated (this was a move, not a copy).
        sheet.TryGetSpillExtent(anchor, out _, out _).Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);

        // Clearing the blocker's real content directly must reveal a blank cell, not an orphaned
        // phantom spill value that SetSpillRange would have written into _spillValues for D2 had it
        // been called unconditionally.
        sheet.ClearCell(blocker);
        sheet.GetValue(2, 4).Should().Be(BlankValue.Instance,
            "no orphaned spill entry should have been left behind for D2 to leak once its real content is cleared");
    }

    [Fact]
    public void Apply_MovingSpillAnchorOntoBlockedDestination_RevertRestoresOriginalSpill()
    {
        // No-regression sibling: the blocked-destination fix in Apply must not disturb Revert's
        // ability to re-establish the ORIGINAL (unblocked) spill back at the source address.
        var (wb, sheet, anchor) = SetUpLiveSpill();
        var blocker = new CellAddress(sheet.Id, 2, 4); // D2
        sheet.SetCell(blocker, new TextValue("Hello"));
        var ctx = new TestCommandContext(wb);

        var anchorOnly = new GridRange(anchor, anchor); // A1 alone
        var destination = new CellAddress(sheet.Id, 1, 4); // D1

        var command = new MoveRangeCommand(sheet.Id, anchorOnly, destination);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 4).Should().Be(ErrorValue.Spill);

        command.Revert(ctx);

        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(3,1)");
        sheet.TryGetSpillExtent(anchor, out var rows, out var cols).Should().BeTrue(
            "undo must re-establish the original live spill at the restored source anchor");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // The destination must be fully cleaned up: no leftover #SPILL! and no leftover spill extent.
        sheet.TryGetSpillExtent(destination, out _, out _).Should().BeFalse();
        // The blocker's content, untouched throughout, must still be intact.
        sheet.GetValue(2, 4).Should().Be(new TextValue("Hello"));
    }
}
