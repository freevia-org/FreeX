using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class CellEntryParserTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("-3", -3)]
    public void CreateCell_ParsesFiniteInvariantNumbers(string text, double expected)
    {
        var cell = CellEntryParser.CreateCell(text, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(expected);
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
    [InlineData("1,25")]
    [InlineData("plain text")]
    public void CreateCell_TreatsNonFiniteAndNonInvariantNumbersAsText(string text)
    {
        var cell = CellEntryParser.CreateCell(text, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be(text);
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
}
