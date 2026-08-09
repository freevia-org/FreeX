using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R132: <see cref="SheetNameFormatter.NeedsQuoting"/> used <c>char.IsAsciiLetterOrDigit</c> while
/// the formula Lexer's identifier scan (which decides whether an UNQUOTED sheet-qualified reference
/// like <c>Café!A1</c> parses at all) is Unicode-aware (<c>char.IsLetterOrDigit</c>). A sheet name
/// made of non-ASCII Unicode letters was therefore over-quoted by the formatter even though the
/// Lexer accepts it perfectly well unquoted -- see
/// R132_UnicodeSheetNameQuotingRoundTripsThroughLexerTests (FreeX.Core.Formula.Tests) for the
/// end-to-end round-trip proof against the actual Lexer.
/// </summary>
public sealed class R132_SheetNameFormatterUnicodeParityTests
{
    [Theory]
    [InlineData("Café")]           // Latin-1 accented letter
    [InlineData("Résumé")]
    [InlineData("Δεδομένα")]       // Greek
    [InlineData("日本語")]          // CJK
    [InlineData("Данные")]         // Cyrillic
    public void QuoteIfNeeded_UnicodeLetterName_ReturnsUnquoted(string name)
    {
        // Every character here is a Unicode LETTER the Lexer's ReadIdentifierOrRef continuation
        // loop (char.IsLetterOrDigit) already accepts unquoted before the '!' -- so the canonical
        // formatter must agree these names do NOT need quoting.
        SheetNameFormatter.QuoteIfNeeded(name).Should().Be(name);
        SheetNameFormatter.NeedsQuoting(name).Should().BeFalse();
    }

    [Fact]
    public void QuoteIfNeeded_UnicodeLetterNameWithDigitSuffix_ReturnsUnquoted()
    {
        // Mixes a Unicode letter prefix with an ordinary ASCII digit suffix -- both character
        // classes are individually already covered elsewhere; this proves they compose correctly
        // through the single shared predicate.
        SheetNameFormatter.QuoteIfNeeded("Café2024").Should().Be("Café2024");
    }

    // --- Sibling no-regression: the guard must not widen past what the Lexer actually accepts ---

    [Fact]
    public void QuoteIfNeeded_UnicodeLetterNameWithEmbeddedSymbol_StillReturnsQuoted()
    {
        // '☕' is a Unicode SYMBOL (category So), not a letter or digit -- char.IsLetterOrDigit
        // returns false for it, so this must still require quoting exactly like the pre-existing
        // "Q1-Q2" hyphen case. Proves the Unicode fix (IsLetterOrDigit) didn't accidentally widen
        // into "any non-ASCII character", only "any Unicode letter/digit".
        var name = "Café☕";
        SheetNameFormatter.NeedsQuoting(name).Should().BeTrue();
        SheetNameFormatter.QuoteIfNeeded(name).Should().Be("'Café☕'");
    }

    [Fact]
    public void QuoteIfNeeded_NameStartingWithUnicodeDigit_ReturnsQuoted()
    {
        // U+0663 ARABIC-INDIC DIGIT THREE is a Unicode digit (char.IsDigit true) but not
        // char.IsAsciiDigit. The Lexer's own ReadNextToken dispatch routes ANY char.IsDigit
        // character to ReadNumber (never ReadIdentifierOrRef) when it leads a token, so a sheet
        // name starting with one can never be an unquoted sheet-qualifier either -- matching the
        // pre-existing ASCII-digit-start behavior, just extended to the same Unicode digit classes
        // the Lexer itself already recognizes as digits.
        SheetNameFormatter.NeedsQuoting("٣Data").Should().BeTrue();
    }

    [Fact]
    public void QuoteIfNeeded_UnicodeLetterNameWithDollarSign_StillReturnsQuoted()
    {
        // '$' is deliberately excluded from the shared unquoted-sheet-name-char predicate (kept
        // quoted, matching this method's prior behavior) even though the Lexer's identifier scan
        // separately allows '$' for absolute-reference syntax on OTHER token kinds it shares code
        // with (CellRef). A sheet name containing '$' must still be quoted.
        SheetNameFormatter.NeedsQuoting("Café$1").Should().BeTrue();
    }
}
