using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class ExcelDateEntryParserTests
{
    [Theory]
    [InlineData("en-US", "6/15/2024", 2024, 6, 15)]
    [InlineData("en-GB", "15/6/2024", 2024, 6, 15)]
    [InlineData("de-DE", "15.06.2024", 2024, 6, 15)]
    public void TryParseCurrentCulture_HonorsCultureDateOrder(
        string cultureName,
        string text,
        int year,
        int month,
        int day)
    {
        using var cultureScope = TestCultureScope.CurrentCulture(cultureName);

        ExcelDateEntryParser.TryParseCurrentCulture(text, allowTimeOnly: false, out var result)
            .Should().BeTrue();
        result.Should().Be(new DateTime(year, month, day));
    }

    [Theory]
    [InlineData("6/15/29", 2029)]
    [InlineData("6/15/30", 1930)]
    [InlineData("6/15/45", 1945)]
    [InlineData("6/15/00", 2000)]
    public void TryParseCurrentCulture_UsesExcelsTwoDigitYearWindow(string text, int year)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        ExcelDateEntryParser.TryParseCurrentCulture(text, allowTimeOnly: false, out var result)
            .Should().BeTrue();
        result.Should().Be(new DateTime(year, 6, 15));
    }

    [Fact]
    public void TryParseCurrentCulture_RejectsPre1900DatesButAcceptsExcelEpoch()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        ExcelDateEntryParser.TryParseCurrentCulture("12/31/1899", false, out _).Should().BeFalse();
        ExcelDateEntryParser.TryParseCurrentCulture("1/1/1900", false, out var epoch).Should().BeTrue();
        epoch.Should().Be(new DateTime(1900, 1, 1));
    }

    [Fact]
    public void TryParseCurrentCulture_RequiresExplicitTimeOnlyOptIn()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        ExcelDateEntryParser.TryParseCurrentCulture("15:30", false, out _).Should().BeFalse();
        ExcelDateEntryParser.TryParseCurrentCulture("15:30", true, out var time).Should().BeTrue();
        time.Date.Should().Be(DateTime.MinValue.Date);
        time.TimeOfDay.Should().Be(new TimeSpan(15, 30, 0));
    }

    [Fact]
    public void TryParseCurrentCulture_RejectsNonDateAndInvalidDateText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        ExcelDateEntryParser.TryParseCurrentCulture("1234.56", true, out _).Should().BeFalse();
        ExcelDateEntryParser.TryParseCurrentCulture("2/30/2024", true, out _).Should().BeFalse();
    }
}
