using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;
using Entry = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatRangeSelector.ValueEntry<int>;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class RangeSelectorTests
{
    private static IReadOnlyList<Entry> Entries(params double[] values)
    {
        var list = new List<Entry>(values.Length);
        for (var i = 0; i < values.Length; i++)
            list.Add(new Entry(i, values[i]));
        return list;
    }

    [Fact]
    public void TopN_SelectsHighestValues()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.Top10, AboveAverage = true, TopBottomRank = 2 };
        var entries = Entries(10, 50, 30, 20, 40); // keys 0..4

        var selected = ConditionalFormatRangeSelector.SelectTopBottom(rule, entries);

        // top 2 values are 50 (key 1) and 40 (key 4)
        selected.Should().BeEquivalentTo(new[] { 1, 4 });
    }

    [Fact]
    public void BottomN_SelectsLowestValues()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.Top10, AboveAverage = false, TopBottomRank = 2 };
        var entries = Entries(10, 50, 30, 20, 40);

        var selected = ConditionalFormatRangeSelector.SelectTopBottom(rule, entries);

        // bottom 2 values are 10 (key 0) and 20 (key 3)
        selected.Should().BeEquivalentTo(new[] { 0, 3 });
    }

    [Fact]
    public void TopN_TiesBrokenByRangeOrder()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.Top10, AboveAverage = true, TopBottomRank = 2 };
        var entries = Entries(50, 50, 50); // all equal

        var selected = ConditionalFormatRangeSelector.SelectTopBottom(rule, entries);

        // first two by range order
        selected.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public void TopPercent_TakesCeilingOfPercentage()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.Top10, AboveAverage = true, TopBottomPercent = true, TopBottomRank = 30 };
        var entries = Entries(1, 2, 3, 4, 5); // 30% of 5 = 1.5 → ceil = 2

        var selected = ConditionalFormatRangeSelector.SelectTopBottom(rule, entries);

        selected.Should().HaveCount(2);
        selected.Should().BeEquivalentTo(new[] { 4, 3 }); // top 2 values 5,4
    }

    [Fact]
    public void TopN_RankExceedsCount_ClampedToCount()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.Top10, AboveAverage = true, TopBottomRank = 99 };
        var entries = Entries(1, 2, 3);

        ConditionalFormatRangeSelector.SelectTopBottom(rule, entries).Should().HaveCount(3);
    }

    [Fact]
    public void TopN_EmptyRange_ReturnsEmpty()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.Top10, AboveAverage = true, TopBottomRank = 5 };
        ConditionalFormatRangeSelector.SelectTopBottom(rule, Entries()).Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_DetectsRepeatedValues()
    {
        var counts = ConditionalFormatRangeSelector.BuildOccurrenceCounts(["a", "b", "a", "c", "B"]);

        // "a" appears twice; "b"/"B" collapse to 2 under case-insensitive comparison
        ConditionalFormatRangeSelector.MatchesDuplicateState("a", counts, duplicate: true).Should().BeTrue();
        ConditionalFormatRangeSelector.MatchesDuplicateState("b", counts, duplicate: true).Should().BeTrue();
        ConditionalFormatRangeSelector.MatchesDuplicateState("c", counts, duplicate: true).Should().BeFalse();
    }

    [Fact]
    public void Unique_DetectsSingleOccurrence()
    {
        var counts = ConditionalFormatRangeSelector.BuildOccurrenceCounts(["a", "b", "a", "c"]);

        ConditionalFormatRangeSelector.MatchesDuplicateState("c", counts, duplicate: false).Should().BeTrue();
        ConditionalFormatRangeSelector.MatchesDuplicateState("a", counts, duplicate: false).Should().BeFalse();
    }

    [Fact]
    public void DuplicateState_BlankNeverMatches()
    {
        var counts = ConditionalFormatRangeSelector.BuildOccurrenceCounts(["a", "a"]);

        ConditionalFormatRangeSelector.MatchesDuplicateState("", counts, duplicate: true).Should().BeFalse();
        ConditionalFormatRangeSelector.MatchesDuplicateState(null, counts, duplicate: false).Should().BeFalse();
    }

    [Fact]
    public void BuildOccurrenceCounts_TrimsAndSkipsBlanks()
    {
        var counts = ConditionalFormatRangeSelector.BuildOccurrenceCounts(["  a ", "a", "", "   ", null]);

        counts.Should().ContainKey("a");
        counts["a"].Should().Be(2);
        counts.Should().HaveCount(1);
    }
}
