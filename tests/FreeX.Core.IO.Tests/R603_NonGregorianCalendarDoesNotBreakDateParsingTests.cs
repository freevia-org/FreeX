using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r603: Excel's two-digit-year window (30-99 -> 19xx, 00-29 -> 20xx) was applied as a bare
/// <c>Calendar.TwoDigitYearMax = 2029</c> at five sites. 2029 is a GREGORIAN year and that setter
/// validates against the calendar's own era, so on an ar-SA machine -- whose current culture uses
/// UmAlQura, legal range 1318-1500 -- it threw <see cref="ArgumentOutOfRangeException"/>. Nothing on
/// the CSV read path, the typed-date-entry path or DATEVALUE caught it, so those users could not
/// open a CSV containing a date-shaped field at all.
///
/// <para>Found by widening r392's culture list, not by looking for it: the adapter census reported a
/// crash rather than a wrong number, which is how a defect in a different class surfaced from a
/// culture probe.</para>
///
/// <para>The cases below cover the entry points separately, because a single guard added at one of
/// them would leave the other two crashing.</para>
/// </summary>
public sealed class R603_NonGregorianCalendarDoesNotBreakDateParsingTests
{
    private static readonly string[] NonGregorianCultures = ["ar-SA", "th-TH"];

    private static void UnderCulture(string culture, Action body)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheProbeCulturesReallyDoUseANonGregorianCalendar()
    {
        // Without this the three cases below would pass on a machine where ICU had changed these
        // cultures to a Gregorian default -- a green over the condition they exist to test.
        foreach (var culture in NonGregorianCultures)
        {
            new CultureInfo(culture).DateTimeFormat.Calendar
                .Should().NotBeOfType<GregorianCalendar>(
                    $"{culture} is here because its calendar cannot express the year 2029");
        }
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void ACsvWithADateShapedFieldOpens(string culture)
    {
        UnderCulture(culture, () =>
        {
            var bytes = Encoding.UTF8.GetBytes("6/15/45,plain\r\n");
            using var stream = new MemoryStream(bytes);

            var act = () => new CsvFileAdapter().Load(stream);
            act.Should().NotThrow();
        });
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void TypingADateShapedValueDoesNotThrow(string culture)
    {
        UnderCulture(culture, () =>
        {
            var act = () => ExcelDateEntryParser.TryParseCurrentCulture("6/15/45", allowTimeOnly: false, out _);
            act.Should().NotThrow();
        });
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("th-TH")]
    public void AGregorianMachineStillGetsExcelsTwoDigitYearWindow(string culture)
    {
        // The remedy must not be "stop applying the window": on a Gregorian calendar Excel's rule
        // still has to hold, or the fix would trade a crash for a wrong date everywhere else.
        UnderCulture(culture, () =>
        {
            var applied = FreeX.Core.Formula.ExcelTwoDigitYearWindow.ApplyTo(
                (CultureInfo)new CultureInfo("en-US").Clone());
            applied.DateTimeFormat.Calendar.TwoDigitYearMax.Should().Be(2029);
        });
    }
}
