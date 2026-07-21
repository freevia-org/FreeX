using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CellEntryParserTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_ParsesFiniteCurrentCultureAndInvariantNumbers()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("fr-FR");

        AssertNumber("12,5", 12.5);
        AssertNumber("12.5", 12.5);
        AssertNumber("-3", -3);
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void CreateCell_ParsesBooleanLiteralsCaseInsensitively(string text, bool expected)
    {
        var cell = CellEntryParser.CreateCell(text, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<BoolValue>()
            .Which.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1.2.3")]
    [InlineData("plain text")]
    public void CreateCell_TreatsNonFiniteAndNonInvariantNumbersAsText(string text)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var cell = CellEntryParser.CreateCell(text, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be(text);
    }

    [Fact]
    public void CreateCell_LeadingApostropheStripsQuoteAndForcesText()
    {
        // R61-render-formula-bar-6-1: typing '007 must strip the apostrophe and store clean
        // text "007", matching Excel and mirroring PasteCommandFactory.ParseClipboardValue's
        // identical paste-path rule -- not the literal "'007" (apostrophe baked into the value).
        var cell = CellEntryParser.CreateCell("'007", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("007");
    }

    [Fact]
    public void CreateCell_LeadingApostropheForcesTextEvenForNumericLookingRemainder()
    {
        // Sibling no-regression: the apostrophe rule must short-circuit BEFORE numeric/boolean/date
        // coercion, so '12.5, 'TRUE, and a bare quote all stay text with the quote stripped instead
        // of being (mis)parsed as a number/boolean, and plain unquoted entries are unaffected.
        CellEntryParser.CreateCell("'12.5", Anchor, useR1C1ReferenceStyle: false)
            .Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("12.5");

        CellEntryParser.CreateCell("'TRUE", Anchor, useR1C1ReferenceStyle: false)
            .Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("TRUE");

        CellEntryParser.CreateCell("'", Anchor, useR1C1ReferenceStyle: false)
            .Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("");

        CellEntryParser.CreateCell("12.5", Anchor, useR1C1ReferenceStyle: false)
            .Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(12.5);
    }

    [Fact]
    public void CreateCell_CreatesA1FormulaWithoutLeadingEquals()
    {
        var cell = CellEntryParser.CreateCell("=A1+B1", Anchor, useR1C1ReferenceStyle: false);

        cell.FormulaText.Should().Be("A1+B1");
    }

    [Fact]
    public void CreateCell_ConvertsR1C1FormulaToA1WhenRequested()
    {
        var cell = CellEntryParser.CreateCell("=R[-1]C+R1C1", Anchor, useR1C1ReferenceStyle: true);

        cell.FormulaText.Should().Be("B1+$A$1");
    }

    private static void AssertNumber(string text, double expected)
    {
        var cell = CellEntryParser.CreateCell(text, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(expected);
    }
}
