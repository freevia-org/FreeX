using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for two round-25 MoveRangeCommand findings:
///
/// R25-meta-3: a sparkline hosted INSIDE the moved source range whose DataRange is also fully
/// contained in it (i.e. the sparkline and its full data move together in one MoveRange) must have
/// its DataRange translated by the move delta too (CaptureSourcePayloads/CloneSparklineAt), mirroring
/// what TranslateFullyContainedSparklineDataRanges already does for the opposite case (anchor OUTSIDE
/// the moved range, DataRange inside it — see R24_MoveRangeSparklineDataRangeTests.cs). Previously
/// CloneSparklineAt copied DataRange verbatim, so the moved sparkline kept pointing at the
/// now-cleared source cells.
///
/// R25-spill-dynamic-deep-1: moving ONLY a live spill's anchor cell (not its non-anchor body members)
/// was wrongly rejected by CommandGuards.RejectIfSplitsArray as "You cannot change part of an array",
/// even though real Excel allows a spilled array's anchor to be cut/moved independently (the array
/// respills at the destination and the old body cells go blank) — and MoveRangeCommand already has the
/// CaptureSourceSpillPayloads/SetSpillRange machinery to relocate the live spill correctly once the
/// guard lets the move through. The sibling cases the guard already covered correctly (a non-anchor
/// member moved alone, a partial anchor+some-but-not-all-of-the-body move, and the whole array moved
/// as a unit) must keep behaving exactly as before.
/// </summary>
public class R25_MoveRangeSparklineAndSpillTests
{
    [Fact]
    public void Apply_MovingSparklineAndItsFullDataRangeTogether_TranslatesDataRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A1:D1 = [1,2,3,4], sparkline anchored at E1 plotting A1:D1 - both inside the moved range.
        for (uint col = 1; col <= 4; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4)); // A1:D1
        var sparklineLocation = new CellAddress(sheet.Id, 1, 5); // E1, inside the moved range
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = dataRange,
            Location = sparklineLocation,
            Kind = SparklineKind.Line,
        });

        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5)); // A1:E1 (data + sparkline anchor)
        var destination = new CellAddress(sheet.Id, 1, 7); // G1

        var command = new MoveRangeCommand(sheet.Id, sourceRange, destination);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var sparkline = sheet.Sparklines.Should().ContainSingle().Subject;
        sparkline.Location.Should().Be(
            new CellAddress(sheet.Id, 1, 11), // K1 (E1 + the same 6-column delta as A1 -> G1)
            "the sparkline's own anchor was inside the moved range and must relocate with it");
        sparkline.DataRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 7), new CellAddress(sheet.Id, 1, 10)), // G1:J1
            "the sparkline's data moved together with its anchor, so DataRange must follow it to G1:J1 " +
            "instead of still pointing at the now-cleared A1:D1");

        // Source data must be cleared (this was a move, not a copy).
        for (uint col = 1; col <= 4; col++)
            sheet.GetValue(1, col).Should().Be(BlankValue.Instance);

        command.Revert(ctx);

        var revertedSparkline = sheet.Sparklines.Should().ContainSingle().Subject;
        revertedSparkline.Location.Should().Be(sparklineLocation, "undo must restore the sparkline's original anchor");
        revertedSparkline.DataRange.Should().Be(dataRange, "undo must restore the sparkline's original DataRange");
        for (uint col = 1; col <= 4; col++)
            sheet.GetValue(1, col).Should().Be(new NumberValue(col), "undo must restore the original data");
    }

    [Fact]
    public void Apply_MovingSparklineDataOutsideAnchor_StillTranslatesViaTheOtherCodePath()
    {
        // Sibling case (R24-sparklines-1, already covered by R24_MoveRangeSparklineDataRangeTests.cs):
        // a sparkline hosted OUTSIDE the moved range whose DataRange is fully inside it goes through
        // TranslateFullyContainedSparklineDataRanges, not CaptureSourcePayloads/CloneSparklineAt. This
        // must keep working (and not be double-translated) after the R25-meta-3 fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        for (uint col = 1; col <= 4; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4)); // A1:D1
        var sparklineLocation = new CellAddress(sheet.Id, 1, 6); // F1, outside the moved range
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = dataRange,
            Location = sparklineLocation,
            Kind = SparklineKind.Line,
        });

        var ctx = new TestCommandContext(wb);
        var destination = new CellAddress(sheet.Id, 1, 7); // G1
        var command = new MoveRangeCommand(sheet.Id, dataRange, destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var sparkline = sheet.Sparklines.Should().ContainSingle().Subject;
        sparkline.Location.Should().Be(sparklineLocation, "the sparkline's own anchor was never part of the moved range");
        sparkline.DataRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 7), new CellAddress(sheet.Id, 1, 10)),
            "the sparkline's data moved from A1:D1 to G1:J1 and its DataRange must follow (translated exactly once)");
    }

    [Fact]
    public void Apply_MovingOnlySpillAnchor_RelocatesLiveSpillInsteadOfBeingRejected()
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
        var ctx = new TestCommandContext(wb);

        var anchorOnly = new GridRange(anchor, anchor); // A1 alone, NOT A1:A3
        var destination = new CellAddress(sheet.Id, 1, 4); // D1

        var command = new MoveRangeCommand(sheet.Id, anchorOnly, destination);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.TryGetSpillExtent(destination, out var rows, out var cols).Should().BeTrue(
            "moving just the anchor must relocate the live spill to the destination, like Excel does");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 4).Should().Be(new NumberValue(1)); // D1
        sheet.GetValue(2, 4).Should().Be(new NumberValue(2)); // D2 (respilled member)
        sheet.GetValue(3, 4).Should().Be(new NumberValue(3)); // D3 (respilled member)

        // The source array must be fully vacated.
        sheet.TryGetSpillExtent(anchor, out _, out _).Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(2, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);

        command.Revert(ctx);

        sheet.GetCell(anchor)!.FormulaText.Should().Be("SEQUENCE(3,1)");
        sheet.TryGetSpillExtent(anchor, out rows, out cols).Should().BeTrue(
            "undo must re-establish the spill at the restored source anchor");
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.TryGetSpillExtent(destination, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Apply_MovingAnchorPlusPartOfSpillBody_IsStillRejected()
    {
        // Sibling case that must NOT be broken by the anchor-only fix: selecting the anchor together
        // with SOME (but not all) of the array's body is not the "anchor alone" exception, and must
        // still be rejected exactly like before.
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
        var ctx = new TestCommandContext(wb);

        var anchorPlusPartial = new GridRange(anchor, new CellAddress(sheet.Id, 2, 1)); // A1:A2, not A1:A3
        var destination = new CellAddress(sheet.Id, 1, 4); // D1

        var outcome = new MoveRangeCommand(sheet.Id, anchorPlusPartial, destination).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of an array.");
        // The array must be untouched - no silent partial move/discard.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 4).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void Apply_MovingWholeSpillArrayAsUnit_StillRelocatesSpillAndIsAllowed()
    {
        // Sibling happy-path (already covered by R20/R21 tests) re-checked here alongside the
        // anchor-only fix: moving the entire array (anchor + all members) together must still work.
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
        var ctx = new TestCommandContext(wb);

        var wholeSource = new GridRange(anchor, new CellAddress(sheet.Id, 3, 1)); // A1:A3
        var destination = new CellAddress(sheet.Id, 1, 4); // D1

        var outcome = new MoveRangeCommand(sheet.Id, wholeSource, destination).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.TryGetSpillExtent(destination, out var rows, out var cols).Should().BeTrue();
        rows.Should().Be(3u);
        cols.Should().Be(1u);
        sheet.GetValue(1, 4).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 4).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 4).Should().Be(new NumberValue(3));
    }
}
