using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotFieldFilterPlannerTests
{
    [Fact]
    public void LabelKindRoundTrip_FindsAndResolvesIndex()
    {
        var index = PivotFieldFilterPlanner.FindLabelKindIndex(PivotLabelFilterKind.Contains);
        PivotFieldFilterPlanner.LabelKindFromIndex(index).Should().Be(PivotLabelFilterKind.Contains);

        PivotFieldFilterPlanner.LabelKindFromIndex(-1).Should().Be(PivotFieldFilterPlanner.LabelFilterKinds[0].Kind);
        PivotFieldFilterPlanner.LabelKindFromIndex(999).Should().Be(PivotFieldFilterPlanner.LabelFilterKinds[^1].Kind);
    }

    [Fact]
    public void ValueKindRoundTrip_FindsAndResolvesIndex()
    {
        var index = PivotFieldFilterPlanner.FindValueKindIndex(PivotValueFilterKind.Bottom);
        PivotFieldFilterPlanner.ValueKindFromIndex(index).Should().Be(PivotValueFilterKind.Bottom);
    }

    [Fact]
    public void LabelKindNeedsSecondValue_OnlyForBetween()
    {
        PivotFieldFilterPlanner.LabelKindNeedsSecondValue(PivotLabelFilterKind.Between).Should().BeTrue();
        PivotFieldFilterPlanner.LabelKindNeedsSecondValue(PivotLabelFilterKind.Equals).Should().BeFalse();
    }

    [Theory]
    [InlineData(PivotValueFilterKind.Top, true, false, true)]
    [InlineData(PivotValueFilterKind.Bottom, true, false, true)]
    [InlineData(PivotValueFilterKind.GreaterThan, false, false, true)]
    [InlineData(PivotValueFilterKind.Between, false, true, true)]
    [InlineData(PivotValueFilterKind.NotBetween, false, true, true)]
    [InlineData(PivotValueFilterKind.AboveAverage, false, false, false)]
    [InlineData(PivotValueFilterKind.BelowAverage, false, false, false)]
    public void ValueKindInputShape_MatchesKind(
        PivotValueFilterKind kind, bool topBottom, bool second, bool primary)
    {
        PivotFieldFilterPlanner.ValueKindIsTopBottom(kind).Should().Be(topBottom);
        PivotFieldFilterPlanner.ValueKindNeedsSecondValue(kind).Should().Be(second);
        PivotFieldFilterPlanner.ValueKindNeedsPrimaryInput(kind).Should().Be(primary);
    }

    [Fact]
    public void TryCreateLabelFilter_RequiresValue_AndSecondForBetween()
    {
        PivotFieldFilterPlanner.TryCreateLabelFilter(2, PivotLabelFilterKind.Equals, "  ", null, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(PivotFieldFilterPlanner.LabelValueRequiredMessage);

        PivotFieldFilterPlanner.TryCreateLabelFilter(2, PivotLabelFilterKind.Between, "A", " ", out _, out var error2)
            .Should().BeFalse();
        error2.Should().Be(PivotFieldFilterPlanner.LabelSecondValueRequiredMessage);

        PivotFieldFilterPlanner.TryCreateLabelFilter(2, PivotLabelFilterKind.Between, " A ", " Z ", out var filter, out _)
            .Should().BeTrue();
        filter!.SourceFieldIndex.Should().Be(2);
        filter.Kind.Should().Be(PivotLabelFilterKind.Between);
        filter.Value.Should().Be("A");
        filter.Value2.Should().Be("Z");
    }

    [Fact]
    public void TryCreateValueFilter_ParsesTopCount()
    {
        PivotFieldFilterPlanner.TryCreateValueFilter(1, 0, PivotValueFilterKind.Top, "0", null, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(PivotFieldFilterPlanner.PositiveCountRequiredMessage);

        PivotFieldFilterPlanner.TryCreateValueFilter(1, 0, PivotValueFilterKind.Top, "5", null, out var filter, out _)
            .Should().BeTrue();
        filter!.Kind.Should().Be(PivotValueFilterKind.Top);
        filter.Count.Should().Be(5);
        filter.SourceFieldIndex.Should().Be(1);
    }

    [Fact]
    public void TryCreateValueFilter_ParsesComparisonAndBetween()
    {
        PivotFieldFilterPlanner.TryCreateValueFilter(1, 0, PivotValueFilterKind.GreaterThan, "x", null, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(PivotFieldFilterPlanner.NumericValueRequiredMessage);

        PivotFieldFilterPlanner.TryCreateValueFilter(1, 0, PivotValueFilterKind.Between, "10", "bad", out _, out var error2)
            .Should().BeFalse();
        error2.Should().Be(PivotFieldFilterPlanner.NumericSecondValueRequiredMessage);

        PivotFieldFilterPlanner.TryCreateValueFilter(1, 0, PivotValueFilterKind.Between, "10", "20", out var filter, out _)
            .Should().BeTrue();
        filter!.ComparisonValue.Should().Be(10);
        filter.ComparisonValue2.Should().Be(20);
    }

    [Fact]
    public void TryCreateValueFilter_AverageKinds_NeedNoNumber()
    {
        PivotFieldFilterPlanner.TryCreateValueFilter(1, 0, PivotValueFilterKind.AboveAverage, null, null, out var filter, out _)
            .Should().BeTrue();
        filter!.Kind.Should().Be(PivotValueFilterKind.AboveAverage);
        filter.ComparisonValue.Should().BeNull();
        filter.Count.Should().Be(0);
    }

    [Fact]
    public void ReplaceFieldLabelFilter_ReplacesOrRemovesOnlyTheField()
    {
        var existing = new List<PivotLabelFilterModel>
        {
            new(0, PivotLabelFilterKind.Equals, "a"),
            new(1, PivotLabelFilterKind.Equals, "b"),
        };

        var replaced = PivotFieldFilterPlanner.ReplaceFieldLabelFilter(
            existing, 0, new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "z"));
        replaced.Should().HaveCount(2);
        replaced.Single(f => f.SourceFieldIndex == 0).Value.Should().Be("z");

        var removed = PivotFieldFilterPlanner.ReplaceFieldLabelFilter(existing, 0, null);
        removed.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(1);
    }

    [Fact]
    public void ReplaceFieldValueFilter_ReplacesOrRemovesOnlyTheField()
    {
        var existing = new List<PivotValueFilterModel>
        {
            new(0, PivotValueFilterKind.Top, Count: 3, SourceFieldIndex: 0),
            new(0, PivotValueFilterKind.Top, Count: 4, SourceFieldIndex: 1),
        };

        var removed = PivotFieldFilterPlanner.ReplaceFieldValueFilter(existing, 0, null);
        removed.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(1);
    }

    [Fact]
    public void InitialDataFieldIndex_ClampsToRange()
    {
        PivotFieldFilterPlanner.InitialDataFieldIndex(null, 0).Should().Be(-1);
        PivotFieldFilterPlanner.InitialDataFieldIndex(null, 2).Should().Be(0);

        var existing = new PivotValueFilterModel(1, PivotValueFilterKind.Top, SourceFieldIndex: 0);
        PivotFieldFilterPlanner.InitialDataFieldIndex(existing, 2).Should().Be(1);
        PivotFieldFilterPlanner.InitialDataFieldIndex(existing, 1).Should().Be(0);
    }

    [Fact]
    public void ResolveAllowedItems_NullForAllOrEmpty_ElseSet()
    {
        PivotFieldFilterPlanner.ResolveAllowedItems(null).Should().BeNull();
        PivotFieldFilterPlanner.ResolveAllowedItems(["(All)"]).Should().BeNull();

        var allowed = PivotFieldFilterPlanner.ResolveAllowedItems(["North", "South"]);
        allowed.Should().NotBeNull();
        allowed!.Should().Contain("North").And.Contain("South");
    }

    [Fact]
    public void ResolveItemSelection_NullWhenAllChecked()
    {
        PivotFieldFilterPlanner.ResolveItemSelection(["a", "b", "c"], 3).Should().BeNull();
        PivotFieldFilterPlanner.ResolveItemSelection(["a"], 3).Should().Equal("a");
    }
}
