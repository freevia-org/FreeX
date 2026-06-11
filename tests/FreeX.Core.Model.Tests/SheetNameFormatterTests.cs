using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SheetNameFormatterTests
{
    // --- Simple names that must NOT be quoted ---

    [Theory]
    [InlineData("Sheet1")]
    [InlineData("SalesData")]
    [InlineData("_Private")]
    [InlineData("Data_2024")]
    [InlineData("ABC")]
    [InlineData("z")]
    public void QuoteIfNeeded_SimpleAlphanumericOrUnderscore_ReturnsUnquoted(string name)
    {
        SheetNameFormatter.QuoteIfNeeded(name).Should().Be(name);
    }

    // Period in non-first position is allowed unquoted (matches Excel behaviour and
    // FormulaSerializer.RequiresQuoting).  Over-quoting is always safe, but the canonical
    // semantics intentionally permit mid-name periods unquoted to avoid spurious diffs.
    [Theory]
    [InlineData("Data.2024")]
    [InlineData("Q1.Sales")]
    public void QuoteIfNeeded_PeriodInNonFirstPosition_ReturnsUnquoted(string name)
    {
        SheetNameFormatter.QuoteIfNeeded(name).Should().Be(name);
    }

    // --- Names that MUST be quoted ---

    [Fact]
    public void QuoteIfNeeded_NameWithHyphen_ReturnsQuoted()
    {
        // "Q1-Q2" was the motivating correctness bug: emitting Q1-Q2!A1:B2 unquoted
        // causes Excel to parse it as a subtraction expression.
        SheetNameFormatter.QuoteIfNeeded("Q1-Q2").Should().Be("'Q1-Q2'");
    }

    [Fact]
    public void QuoteIfNeeded_NameWithSpace_ReturnsQuoted()
    {
        SheetNameFormatter.QuoteIfNeeded("My Sheet").Should().Be("'My Sheet'");
    }

    [Fact]
    public void QuoteIfNeeded_NameWithEmbeddedApostrophe_DoublesApostropheAndQuotes()
    {
        // O'Brien  →  'O''Brien'
        SheetNameFormatter.QuoteIfNeeded("O'Brien").Should().Be("'O''Brien'");
    }

    [Fact]
    public void QuoteIfNeeded_NameStartingWithDigit_ReturnsQuoted()
    {
        // "2024" would lex as a numeric literal without quotes.
        SheetNameFormatter.QuoteIfNeeded("2024").Should().Be("'2024'");
    }

    [Fact]
    public void QuoteIfNeeded_NameStartingWithPeriod_ReturnsQuoted()
    {
        // Leading period is non-standard; Excel quotes such names.
        SheetNameFormatter.QuoteIfNeeded(".Hidden").Should().Be("'.Hidden'");
    }

    [Fact]
    public void QuoteIfNeeded_EmptyString_ReturnsQuoted()
    {
        SheetNameFormatter.QuoteIfNeeded("").Should().Be("''");
    }

    // --- Cell-reference-shaped names ---

    [Theory]
    [InlineData("A1")]
    [InlineData("B7")]
    [InlineData("XFD1")]
    [InlineData("XFD1048576")]
    [InlineData("a1")]   // case-insensitive
    public void QuoteIfNeeded_A1StyleCellRef_ReturnsQuoted(string name)
    {
        // A sheet named "A1" without quotes is indistinguishable from a cell reference.
        SheetNameFormatter.QuoteIfNeeded(name).Should().StartWith("'").And.EndWith("'");
    }

    [Theory]
    [InlineData("R1C1")]
    [InlineData("R2C3")]
    [InlineData("RC")]      // relative R1C1 — both row and col omitted
    [InlineData("R1C")]
    [InlineData("RC1")]
    [InlineData("r1c1")]    // lowercase
    public void QuoteIfNeeded_R1C1StyleCellRef_ReturnsQuoted(string name)
    {
        SheetNameFormatter.QuoteIfNeeded(name).Should().StartWith("'").And.EndWith("'");
    }

    // --- Boolean keyword names ---

    [Theory]
    [InlineData("TRUE")]
    [InlineData("FALSE")]
    [InlineData("true")]
    [InlineData("False")]
    public void QuoteIfNeeded_BooleanKeyword_ReturnsQuoted(string name)
    {
        SheetNameFormatter.QuoteIfNeeded(name).Should().StartWith("'").And.EndWith("'");
    }

    // --- NeedsQuoting mirrors QuoteIfNeeded ---

    [Theory]
    [InlineData("Sheet1", false)]
    [InlineData("Q1-Q2", true)]
    [InlineData("My Sheet", true)]
    [InlineData("2024", true)]
    [InlineData("A1", true)]
    [InlineData("TRUE", true)]
    [InlineData("Data.2024", false)]
    public void NeedsQuoting_ReturnsExpectedResult(string name, bool expected)
    {
        SheetNameFormatter.NeedsQuoting(name).Should().Be(expected);
    }

    // --- Apostrophe escaping round-trip ---

    [Fact]
    public void QuoteIfNeeded_MultipleApostrophes_AllAreDoubled()
    {
        // "It's O'Brien's" → "'It''s O''Brien''s'"
        SheetNameFormatter.QuoteIfNeeded("It's O'Brien's")
            .Should().Be("'It''s O''Brien''s'");
    }
}
