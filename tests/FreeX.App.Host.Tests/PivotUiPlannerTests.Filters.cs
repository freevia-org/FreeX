using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Theory]
    [InlineData("contains:East", PivotLabelFilterKind.Contains, "East")]
    [InlineData("begins:Q", PivotLabelFilterKind.BeginsWith, "Q")]
    [InlineData("<>West", PivotLabelFilterKind.DoesNotEqual, "West")]
    public void TryParseLabelFilter_ParsesExcelStyleFilterText(string input, PivotLabelFilterKind expectedKind, string expectedValue)
    {
        PivotUiPlanner.TryParseLabelFilter(input, 2, out var filter).Should().BeTrue();
        filter.SourceFieldIndex.Should().Be(2);
        filter.Kind.Should().Be(expectedKind);
        filter.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(">10", PivotValueFilterKind.GreaterThan, 10)]
    [InlineData("<=5.5", PivotValueFilterKind.LessThanOrEqual, 5.5)]
    [InlineData("<>0", PivotValueFilterKind.DoesNotEqual, 0)]
    public void TryParseValueFilter_ParsesComparisonOperators(string input, PivotValueFilterKind expectedKind, double expectedValue)
    {
        PivotUiPlanner.TryParseValueFilter(input, 3, out var filter).Should().BeTrue();
        filter.SourceFieldIndex.Should().Be(3);
        filter.Kind.Should().Be(expectedKind);
        filter.ComparisonValue.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("top:10", PivotValueFilterKind.Top, 10)]
    [InlineData("bottom:3", PivotValueFilterKind.Bottom, 3)]
    public void TryParseValueFilter_ParsesTopBottomFilters(string input, PivotValueFilterKind expectedKind, int expectedCount)
    {
        PivotUiPlanner.TryParseValueFilter(input, 4, out var filter).Should().BeTrue();
        filter.SourceFieldIndex.Should().Be(4);
        filter.Kind.Should().Be(expectedKind);
        filter.Count.Should().Be(expectedCount);
    }

    [Fact]
    public void SetFieldSelectedItems_UpdatesMatchingFieldOnly()
    {
        var fields = new[]
        {
            new PivotFieldModel(0, SelectedItems: ["East"]),
            new PivotFieldModel(1)
        };

        var updated = PivotUiPlanner.SetFieldSelectedItems(fields, 1, ["Q1"]);

        updated[0].SelectedItems.Should().Equal("East");
        updated[1].SelectedItem.Should().Be("Q1");
        updated[1].SelectedItems.Should().Equal("Q1");
    }
}
