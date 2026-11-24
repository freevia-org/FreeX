using System.Globalization;

using FluentAssertions;
using FreeX.Core.Formula;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelTextNumberParserDateTimeLocaleTests
{
    [Theory]
    [InlineData("bg-BG", false)]
    [InlineData("bg-BG", true)]
    [InlineData("fr-FR", false)]
    [InlineData("fr-FR", true)]
    [InlineData("de-DE", false)]
    [InlineData("de-DE", true)]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    public void IsoDateTime_PreservesDateTimeAndWorkbookEpoch(string cultureName, bool uses1904DateSystem)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var expected = new DateTime(2024, 6, 20, 9, 15, 0);

        ExcelTextNumberParser.TryParse("2024-06-20 09:15:00", out var number, uses1904DateSystem)
            .Should().BeTrue();

        number.Should().BeApproximately(ExcelDateSystem.DateToSerial(expected, uses1904DateSystem), 1e-9);
    }

    [Theory]
    [InlineData("bg-BG", "20.06.2024 09:15:00")]
    [InlineData("bg-BG", "20. 06. 2024 09:15:00")]
    [InlineData("fr-FR", "20/06/2024 09:15:00")]
    [InlineData("de-DE", "20.06.2024 09:15:00")]
    [InlineData("de-DE", "20. 06. 2024 09:15:00")]
    [InlineData("en-US", "6/20/2024 09:15:00")]
    [InlineData("ko-KR", "2024. 6. 20. 오전 9:15")]
    public void LocalizedShortDateTime_PreservesDateAndTime(string cultureName, string text)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);

        ExcelTextNumberParser.TryParse(text, out var number).Should().BeTrue();

        number.Should().BeApproximately(new DateTime(2024, 6, 20, 9, 15, 0).ToOADate(), 1e-9);
    }

    [Theory]
    [InlineData("g", false)]
    [InlineData("g", true)]
    [InlineData("d", false)]
    [InlineData("d", true)]
    [InlineData("t", false)]
    [InlineData("t", true)]
    public void KoreanStandardFormats_PreserveDateAndTimeSemantics(string format, bool uses1904DateSystem)
    {
        using var scope = TestCultureScope.CurrentCulture("ko-KR");
        var dateTime = new DateTime(2024, 6, 20, 9, 15, 0);
        var text = dateTime.ToString(format, CultureInfo.CurrentCulture);
        var expected = format switch
        {
            "d" => ExcelDateSystem.DateToSerial(dateTime.Date, uses1904DateSystem),
            "t" => dateTime.TimeOfDay.TotalDays,
            _ => ExcelDateSystem.DateToSerial(dateTime, uses1904DateSystem),
        };

        ExcelTextNumberParser.TryParse(text, out var number, uses1904DateSystem).Should().BeTrue();

        number.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("bg-BG", "3/4 09:15", 4, 3)]
    [InlineData("bg-BG", "1-2 09:15", 2, 1)]
    [InlineData("fr-FR", "3/4 09:15", 4, 3)]
    [InlineData("fr-FR", "1-2 09:15", 2, 1)]
    [InlineData("de-DE", "3/4 09:15", 4, 3)]
    [InlineData("de-DE", "1-2 09:15", 2, 1)]
    [InlineData("en-US", "3/4 09:15", 3, 4)]
    [InlineData("en-US", "1-2 09:15", 1, 2)]
    public void YearlessDateTime_UsesCultureDateOrderAndCurrentYear(
        string cultureName, string text, int month, int day)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var expected = new DateTime(DateTime.Today.Year, month, day, 9, 15, 0);

        ExcelTextNumberParser.TryParse(text, out var number).Should().BeTrue();

        number.Should().BeApproximately(expected.ToOADate(), 1e-9);
    }

    [Theory]
    [InlineData("bg-BG", "20 юни 2024", false)]
    [InlineData("bg-BG", "20 юни 2024", true)]
    [InlineData("fr-FR", "20 juin 2024", false)]
    [InlineData("fr-FR", "20 juin 2024", true)]
    [InlineData("de-DE", "20 Juni 2024", false)]
    [InlineData("de-DE", "20 Juni 2024", true)]
    [InlineData("en-US", "20 June 2024", false)]
    [InlineData("en-US", "20 June 2024", true)]
    public void LocalizedMonthName_PreservesDateWithAndWithoutTime(
        string cultureName, string date, bool includeTime)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var text = includeTime ? date + " 09:15:00" : date;
        var expected = new DateTime(2024, 6, 20, includeTime ? 9 : 0, includeTime ? 15 : 0, 0);

        ExcelTextNumberParser.TryParse(text, out var number).Should().BeTrue();

        number.Should().BeApproximately(expected.ToOADate(), 1e-9);
    }

    [Theory]
    [InlineData("bg-BG")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void MalformedGroupedNumbers_DoNotFallThroughToGuessedDates(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
        foreach (var text in new[] { $"1{separator}2", $"12{separator}34" })
        {
            ExcelTextNumberParser.TryParse(text, out var number).Should().BeFalse(
                $"'{text}' has neither valid grouping nor an explicit date under {cultureName}");
            number.Should().Be(0);
        }
    }

    [Theory]
    [InlineData("bg-BG")]
    [InlineData("fr-FR")]
    public void MalformedWhitespaceGroups_DoNotFallThroughToGuessedDates(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);
        foreach (var separator in new[] { " ", "\u00a0", "\u202f" })
        {
            ExcelTextNumberParser.TryParse($"1{separator}2", out var number).Should().BeFalse();
            number.Should().Be(0);
            ExcelTextNumberParser.TryParse($"12{separator}34", out number).Should().BeFalse();
            number.Should().Be(0);
            ExcelTextNumberParser.TryParse($"1{separator}2 09:15", out number).Should().BeFalse();
            number.Should().Be(0);
        }
    }

    [Theory]
    [InlineData("bg-BG")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void TimeOnlyInput_RemainsAFractionOfADay(string cultureName)
    {
        using var scope = TestCultureScope.CurrentCulture(cultureName);

        ExcelTextNumberParser.TryParse("09:15:00", out var number).Should().BeTrue();

        number.Should().BeApproximately(new TimeSpan(9, 15, 0).TotalDays, 1e-12);
    }
}
