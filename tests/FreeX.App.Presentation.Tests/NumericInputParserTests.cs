using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation;

namespace FreeX.App.Presentation.Tests;

public sealed class NumericInputParserTests
{
    [Fact]
    public void ChartFormatPlanners_DelegateIntegerParsingToCanonicalParser()
    {
        var plannerRoot = RepositoryFileLocator.FindDirectory(
            "src",
            "FreeX.App.Presentation",
            "Charts",
            "Editing");
        var plannerFiles = new[]
        {
            "ChartBarFormatPlanner.cs",
            "ChartBubbleFormatPlanner.cs",
            "ChartPieFormatPlanner.cs",
            "ChartStockFormatPlanner.cs"
        };

        foreach (var plannerFile in plannerFiles)
        {
            var source = File.ReadAllText(Path.Combine(plannerRoot, plannerFile));
            source.Should().Contain("NumericInputParser.TryParseInt32");
            source.Should().NotContain("private static bool TryParseClampedInt");
            source.Should().NotContain("private static bool TryParseInt(");
        }
    }

    [Theory]
    [InlineData(" -100 ", -100, 500, -100)]
    [InlineData("500", -100, 500, 500)]
    [InlineData("+42", -100, 500, 42)]
    public void TryParseInt32InRange_AcceptsIntegerStylesAndInclusiveBounds(
        string input,
        int min,
        int max,
        int expected)
    {
        NumericInputParser.TryParseInt32InRange(
                input,
                min,
                max,
                CultureInfo.GetCultureInfo("en-US"),
                CultureInfo.InvariantCulture,
                out var value)
            .Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void TryParseInt32_UsesPrimaryCultureBeforeInvariantFallback()
    {
        var primary = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        primary.NumberFormat.PositiveSign = "p";

        NumericInputParser.TryParseInt32(
                "p12",
                primary,
                CultureInfo.InvariantCulture,
                out var primaryValue)
            .Should().BeTrue();
        primaryValue.Should().Be(12);

        NumericInputParser.TryParseInt32(
                "+12",
                primary,
                CultureInfo.InvariantCulture,
                out var fallbackValue)
            .Should().BeTrue();
        fallbackValue.Should().Be(12);
    }

    [Theory]
    [InlineData("501", 0, 500, 501)]
    [InlineData("-1", 0, 500, -1)]
    public void TryParseInt32InRange_OutOfRangePreservesParsedValue(
        string input,
        int min,
        int max,
        int expectedValue)
    {
        NumericInputParser.TryParseInt32InRange(
                input,
                min,
                max,
                CultureInfo.InvariantCulture,
                CultureInfo.InvariantCulture,
                out var value)
            .Should().BeFalse();
        value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    [InlineData("invalid")]
    public void TryParseInt32_ParseFailureResetsValueToZero(string input)
    {
        NumericInputParser.TryParseInt32(
                input,
                CultureInfo.InvariantCulture,
                CultureInfo.InvariantCulture,
                out var value)
            .Should().BeFalse();
        value.Should().Be(0);
    }
}
