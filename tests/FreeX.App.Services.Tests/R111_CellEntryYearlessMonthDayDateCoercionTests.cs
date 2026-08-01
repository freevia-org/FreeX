using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R112 (family gap left open by R111): R111 fixed CSV/TXT import
/// (DelimitedTextWorkbookReader.LooksLikeCurrentCultureDateCandidate) so a bare, year-less "M/d" or
/// "M-d" token (e.g. "3/4", "1-2") coerces to a current-year date, matching real Excel. Typed cell
/// entry (CellEntryParser.LooksLikeDateCandidate) carried the exact same bug -- typing "3/4"
/// directly into a cell required either a letter or 3+ digit groups to even attempt a date parse,
/// so a bare "3/4" fell through to plain text instead of becoming a date. Both paths now route
/// through the shared FreeX.Core.IO.DateEntryShapeRecognizer.
/// </summary>
public sealed class R111_CellEntryYearlessMonthDayDateCoercionTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_CoercesYearlessSlashMonthDayToCurrentYearDate()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("3/4", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(DateTime.Now.Year, 3, 4));
    }

    [Fact]
    public void CreateCell_CoercesYearlessHyphenMonthDayToCurrentYearDate()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1-2", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(DateTime.Now.Year, 1, 2));
    }

    // No-regression: a genuine mixed-number fraction entry (whole part + space + "n/d") must still
    // parse as a fraction, not get swept up by the new year-less date shape.
    [Fact]
    public void CreateCell_StillConvertsMixedNumberFractionToItsDecimalValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1 3/4", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(1.75);
    }

    // No-regression: a full date literal with an explicit year must still parse the same as before.
    [Fact]
    public void CreateCell_StillConvertsFullDateLiteralWithYear()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("3/4/2024", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(2024, 3, 4));
    }

    // No-regression: a plain two-digit-group decimal sharing "." (which is not en-US's date
    // separator) must still be text/number, not a date.
    [Fact]
    public void CreateCell_StillTreatsDotSeparatedTripletAsTextUnderEnUs()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1.2.3", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("1.2.3");
    }

    // No-regression: a standalone time-of-day literal (no date separator at all) must still be
    // recognized as a time serial, not rejected now that colon handling routes through the shared
    // helper.
    [Fact]
    public void CreateCell_StillConvertsStandaloneTimeOfDayLiteral()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("9:30", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().Be(new TimeSpan(9, 30, 0).TotalDays);
    }

    // No-regression: an ordinary non-date string must still stay text.
    [Fact]
    public void CreateCell_StillTreatsNonDateTextAsText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("Product", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("Product");
    }
}
