using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R24-cell-editing-deep-2: typed percent/currency/date/fraction literals must auto-convert to
/// the matching numeric/date scalar value (like real Excel), not fall through to plain text.
/// </summary>
public sealed class CellEntryParserTypedLiteralConversionTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_ConvertsTrailingPercentToItsUnderlyingFraction()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("50%", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0.5);
    }

    [Fact]
    public void CreateCell_ConvertsNegativePercentToItsUnderlyingFraction()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("-50%", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(-0.5);
    }

    [Fact]
    public void CreateCell_ConvertsDollarLiteralToItsNumericValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("$5", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(5);
    }

    [Fact]
    public void CreateCell_ConvertsFullDateLiteralToADateTimeValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1/2/2024", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(2024, 1, 2));
    }

    [Fact]
    public void CreateCell_ConvertsMixedNumberFractionToItsDecimalValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1 1/2", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(1.5);
    }

    [Fact]
    public void CreateCell_ConvertsZeroLeadFractionToItsDecimalValue()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("0 1/2", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0.5);
    }

    [Fact]
    public void CreateCell_StillTreatsDotSeparatedTripletAsTextUnderEnUsWhereDotIsNotTheDateSeparator()
    {
        // Regression guard: en-US's date separator is '/', so a dotted triplet like "1.2.3" must
        // stay text even though .NET's own DateTime.TryParse is lenient enough to accept it.
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1.2.3", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("1.2.3");
    }

    // R31-datetime-serial-format-deep-1: a two-digit year must resolve using Excel's documented
    // 1930-2029 window (30-99 -> 19xx), not .NET's default Calendar.TwoDigitYearMax of 2049.
    [Fact]
    public void CreateCell_ConvertsTwoDigitYearLiteralUsingExcelsTwoDigitYearWindow()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("6/15/45", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(1945, 6, 15));
    }

    // Sibling: an ordinary full four-digit-year date literal must still parse correctly - the
    // two-digit-year window override must not disturb this already-working case.
    [Fact]
    public void CreateCell_StillConvertsFullFourDigitYearDateLiteral()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("6/15/2020", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(2020, 6, 15));
    }

    // R31-datetime-serial-format-deep-3: Excel cannot represent any date before 1/1/1900, so a
    // typed date-like literal earlier than that floor must stay literal text, not become a
    // negative-serial DateTimeValue.
    [Fact]
    public void CreateCell_TreatsDateLiteralBeforeExcelsEpochFloorAsText()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1/1/1850", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("1/1/1850");
    }

    // R82-formula-datetime-serial-5-2: Excel's fictitious 1900 leap day ("2/29/1900" /
    // "1900-02-29") cannot be represented as a real .NET DateTime (1900 is not a leap year), so
    // typing it directly into a cell must still resolve to serial 60 - matching DATEVALUE's
    // already-existing special-case for the same literal - instead of falling through to text.
    [Fact]
    public void CreateCell_ConvertsSlashFormFakeLeapDayLiteralToSerial60()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("2/29/1900", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>().Which.Value.Should().Be(60);
    }

    [Fact]
    public void CreateCell_ConvertsIsoFormFakeLeapDayLiteralToSerial60()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("1900-02-29", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>().Which.Value.Should().Be(60);
    }

    // Sibling: the real (non-phantom) leap day immediately before it, Feb 28 1900, must keep
    // parsing via the ordinary DateTime-based path (serial 59), unaffected by the new
    // fake-leap-day special-case.
    [Fact]
    public void CreateCell_StillConvertsOrdinaryDayBeforeFakeLeapDayViaNormalDatePath()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("2/28/1900", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<DateTimeValue>()
            .Which.ToDateTime().Should().Be(new DateTime(1900, 2, 28));
    }
}
