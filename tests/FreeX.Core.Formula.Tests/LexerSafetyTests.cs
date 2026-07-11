using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class LexerSafetyTests
{
    [Fact]
    public void Tokenize_TooManyTokens_ThrowsFormulaParseException_DuringTokenizationAlone()
    {
        // 9,000 numbers joined by '+' produces ~17,999 tokens (before EndOfFormula),
        // comfortably over FormulaSafetyLimits.MaxParseTokens (16,384). The cap must be
        // enforced inside Lexer.Tokenize() itself -- calling Tokenize() alone (never
        // constructing a Parser) must already throw, proving the token list can't grow
        // unbounded before any safety check runs.
        var formula = "=" + string.Join("+", Enumerable.Repeat("1", 9_000));

        Action act = () => new Lexer(formula).Tokenize();

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void Tokenize_WithinTokenLimit_DoesNotThrow()
    {
        var formula = "=" + string.Join("+", Enumerable.Repeat("1", 100));

        var tokens = new Lexer(formula).Tokenize();

        tokens.Should().HaveCount(200); // 100 numbers + 99 '+' operators + EndOfFormula
    }
}
