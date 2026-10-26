using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ConditionalFormatRangeMembershipTests
{
    [Fact]
    public void Contains_MatchesPrimaryAndAdditionalRangesWithoutCrossSheetMatches()
    {
        var sheet = SheetId.New();
        var otherSheet = SheetId.New();
        var rule = new ConditionalFormat
        {
            AppliesTo = Range(sheet, 1, 1, 2, 2),
            AdditionalRanges =
            [
                Range(sheet, 1, 4, 2, 4),
                Range(sheet, 2, 2, 3, 3)
            ]
        };

        rule.Contains(new CellAddress(sheet, 1, 1)).Should().BeTrue();
        rule.Contains(new CellAddress(sheet, 1, 4)).Should().BeTrue();
        rule.Contains(new CellAddress(sheet, 3, 3)).Should().BeTrue();
        rule.Contains(new CellAddress(sheet, 4, 4)).Should().BeFalse();
        rule.Contains(new CellAddress(otherSheet, 1, 1)).Should().BeFalse();
        rule.Overlaps(Range(sheet, 2, 4, 3, 5)).Should().BeTrue();
        rule.Overlaps(Range(sheet, 4, 4, 5, 5)).Should().BeFalse();
        rule.Overlaps(Range(otherSheet, 1, 1, 2, 2)).Should().BeFalse();
        rule.RangeCount.Should().Be(3);

        rule.AdditionalRanges = null;
        rule.Contains(new CellAddress(sheet, 1, 4)).Should().BeFalse();
        rule.Overlaps(Range(sheet, 1, 4, 2, 4)).Should().BeFalse();
        rule.RangeCount.Should().Be(1);

        rule.AdditionalRanges = [];
        rule.Contains(new CellAddress(sheet, 1, 4)).Should().BeFalse();
    }

    [Fact]
    public void Contains_RepeatedHotPathChecksDoNotAllocate()
    {
        var sheet = SheetId.New();
        var rule = new ConditionalFormat
        {
            AppliesTo = Range(sheet, 1, 1, 10, 10),
            AdditionalRanges = [Range(sheet, 20, 20, 30, 30)]
        };
        var address = new CellAddress(sheet, 25, 25);

        rule.Contains(address).Should().BeTrue();
        rule.Overlaps(new GridRange(address, address)).Should().BeTrue();

        const int iterations = 10_000;

        // AllocationProbe warms the loop up (so tiered compilation of the measured path is not
        // charged to it on a cold CI worker) and reports the lowest of several measurements, so a
        // one-off allocation the runtime charges to the runner thread cannot fail the run.
        AllocationProbe.ShouldNotAllocate(
            () =>
            {
                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    _ = rule.Contains(address);
                    _ = rule.Overlaps(new GridRange(address, address));
                }
            },
            operations: iterations * 2,
            "membership checks must test the ranges in place instead of materializing AllRanges");
    }

    private static GridRange Range(
        SheetId sheet,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheet, startRow, startColumn),
            new CellAddress(sheet, endRow, endColumn));
}
