using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class PageAxisPaginationRulesTests
{
    [Fact]
    public void ComputeRepeatRangeSize_ClipsRangeAndExcludesHiddenOrNegativeSizes()
    {
        var size = PageAxisPaginationRules.ComputeRepeatRangeSize(
            new WorksheetRepeatRange(2, 5),
            maxItem: 4,
            isHidden: value => value == 3,
            sizeOf: value => value == 2 ? -20.0 : value);

        size.Should().Be(4.0);
    }

    [Fact]
    public void ComputeAccumulationBreakPoints_SkipsTitlesAndHiddenItemsAndIsolatesOversizedItems()
    {
        var sizes = new Dictionary<uint, double>
        {
            [3] = 6.0,
            [5] = 5.0,
            [6] = 20.0,
            [7] = 2.0,
        };

        var breaks = PageAxisPaginationRules.ComputeAccumulationBreakPoints(
            startValue: 1,
            endValue: 7,
            repeat: new WorksheetRepeatRange(1, 2),
            isHidden: value => value == 4,
            sizeOf: value => sizes[value],
            availableBodySize: 10.0);

        breaks.Should().Equal(5u, 6u, 7u);
    }

    [Fact]
    public void MergeBreaks_UnionsComputedAndManualBreaksWithoutDuplicates()
    {
        var merged = PageAxisPaginationRules.MergeBreaks(
            userBreaks: new uint[] { 3, 5 },
            computedBreaks: [5, 8]);

        merged.Should().BeEquivalentTo([3u, 5u, 8u]);
        merged.Should().HaveCount(3);
    }

    [Fact]
    public void MergeBreaks_WithNoComputedBreaks_PreservesManualBreakSequence()
    {
        var merged = PageAxisPaginationRules.MergeBreaks(
            userBreaks: new uint[] { 9, 4, 9 },
            computedBreaks: []);

        merged.Should().Equal(9u, 4u, 9u);
    }

    [Fact]
    public void UnboundedAxisCapacity_ExceedsSpanAndAvoidsOverflow()
    {
        PageAxisPaginationRules.UnboundedAxisCapacity(5, 8).Should().Be(5);
        PageAxisPaginationRules.UnboundedAxisCapacity(8, 5).Should().Be(1);
        PageAxisPaginationRules.UnboundedAxisCapacity(1, uint.MaxValue).Should().Be(uint.MaxValue - 1);
    }
}
