using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ForecastSheetInputParserTests
{
    [Theory]
    [InlineData("1", 1u)]
    [InlineData(" 12 ", 12u)]
    public void TryParsePeriods_AcceptsPositiveWholeNumbers(string input, uint expected)
    {
        ForecastSheetInputParser.TryParsePeriods(input, out var periods).Should().BeTrue();
        periods.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("three")]
    public void TryParsePeriods_RejectsBlankNonNumericAndNonPositiveInput(string input)
    {
        ForecastSheetInputParser.TryParsePeriods(input, out var periods).Should().BeFalse();
        periods.Should().Be(0);
    }
}
