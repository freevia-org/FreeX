using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ParserSafetyTests
{
    [Fact]
    public void Parser_TooManyTokens_ThrowsFormulaParseException()
    {
        var formula = "=" + string.Join("+", Enumerable.Repeat("1", 9_000));

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void Parser_DeepFunctionNesting_ThrowsFormulaParseException()
    {
        var formula = "=" + string.Concat(Enumerable.Repeat("ABS(", 600)) + "1" + new string(')', 600);

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void Parser_DeepPowerChain_ThrowsFormulaParseException()
    {
        var formula = "=" + string.Join("^", Enumerable.Repeat("1", 700));

        Action act = () => new Parser(new Lexer(formula).Tokenize()).Parse();

        act.Should().Throw<FormulaParseException>();
    }
}
