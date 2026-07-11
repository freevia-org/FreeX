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
}
