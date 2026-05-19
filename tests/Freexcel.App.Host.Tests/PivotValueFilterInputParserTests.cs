using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PivotValueFilterInputParserTests
{
    [Fact]
    public void TryCreateFilter_CreatesTopOrBottomCountFilter()
    {
        PivotValueFilterInputParser.TryCreateFilter(
                PivotValueFilterKind.Top,
                usesCount: true,
                valueText: " 5 ",
                value2Text: "",
                sourceFieldIndex: 3,
                out var filter,
                out var error)
            .Should().BeTrue(error);

        filter.Kind.Should().Be(PivotValueFilterKind.Top);
        filter.Count.Should().Be(5);
        filter.SourceFieldIndex.Should().Be(3);
    }

    [Fact]
    public void TryCreateFilter_CreatesAverageFilterWithoutValues()
    {
        PivotValueFilterInputParser.TryCreateFilter(
                PivotValueFilterKind.AboveAverage,
                usesCount: false,
                valueText: "",
                value2Text: "",
                sourceFieldIndex: 2,
                out var filter,
                out var error)
            .Should().BeTrue(error);

        filter.Kind.Should().Be(PivotValueFilterKind.AboveAverage);
        filter.ComparisonValue.Should().BeNull();
        filter.ComparisonValue2.Should().BeNull();
    }

    [Fact]
    public void TryCreateFilter_CreatesComparisonAndBetweenFilters()
    {
        PivotValueFilterInputParser.TryCreateFilter(
                PivotValueFilterKind.GreaterThan,
                usesCount: false,
                valueText: "12.5",
                value2Text: "",
                sourceFieldIndex: 1,
                out var comparison,
                out var comparisonError)
            .Should().BeTrue(comparisonError);

        comparison.ComparisonValue.Should().Be(12.5);
        comparison.ComparisonValue2.Should().BeNull();

        PivotValueFilterInputParser.TryCreateFilter(
                PivotValueFilterKind.Between,
                usesCount: false,
                valueText: "1",
                value2Text: "10",
                sourceFieldIndex: 1,
                out var between,
                out var betweenError)
            .Should().BeTrue(betweenError);

        between.ComparisonValue.Should().Be(1);
        between.ComparisonValue2.Should().Be(10);
    }

    [Theory]
    [InlineData(PivotValueFilterKind.Top, true, "0", "", "Enter a positive item count.")]
    [InlineData(PivotValueFilterKind.Top, true, "bad", "", "Enter a positive item count.")]
    [InlineData(PivotValueFilterKind.GreaterThan, false, "NaN", "", "Enter a numeric comparison value.")]
    [InlineData(PivotValueFilterKind.GreaterThan, false, "bad", "", "Enter a numeric comparison value.")]
    [InlineData(PivotValueFilterKind.Between, false, "1", "Infinity", "Enter a numeric ending comparison value.")]
    [InlineData(PivotValueFilterKind.Between, false, "1", "bad", "Enter a numeric ending comparison value.")]
    public void TryCreateFilter_RejectsInvalidInputs(
        PivotValueFilterKind kind,
        bool usesCount,
        string valueText,
        string value2Text,
        string expectedError)
    {
        PivotValueFilterInputParser.TryCreateFilter(
                kind,
                usesCount,
                valueText,
                value2Text,
                sourceFieldIndex: 0,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be(expectedError);
    }
}
