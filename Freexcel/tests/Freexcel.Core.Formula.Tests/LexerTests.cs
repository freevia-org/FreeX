using Freexcel.Core.Formula;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public class LexerTests
{
    [Fact]
    public void Tokenizes_SimpleNumber()
    {
        var tokens = new Lexer("=42").Tokenize();
        tokens.Should().HaveCount(2); // Number + EndOfFormula
        tokens[0].Type.Should().Be(TokenType.Number);
        tokens[0].Value.Should().Be("42");
    }

    [Fact]
    public void Tokenizes_CellRef()
    {
        var tokens = new Lexer("=A1").Tokenize();
        tokens[0].Type.Should().Be(TokenType.CellRef);
        tokens[0].Value.Should().Be("A1");
    }

    [Fact]
    public void Tokenizes_FunctionCall()
    {
        var tokens = new Lexer("=SUM(A1)").Tokenize();
        tokens[0].Type.Should().Be(TokenType.FunctionName);
        tokens[0].Value.Should().Be("SUM");
        tokens[1].Type.Should().Be(TokenType.OpenParen);
    }

    [Fact]
    public void Tokenizes_ComparisonOperators()
    {
        var tokens = new Lexer("=1<>2").Tokenize();
        tokens[1].Type.Should().Be(TokenType.NotEqual);
    }

    [Fact]
    public void Tokenizes_String_WithEscapedQuotes()
    {
        var tokens = new Lexer("=\"he said \"\"hello\"\"\"").Tokenize();
        tokens[0].Type.Should().Be(TokenType.String);
        tokens[0].Value.Should().Be("he said \"hello\"");
    }

    [Fact]
    public void Tokenizes_BooleanTrue()
    {
        var tokens = new Lexer("=TRUE").Tokenize();
        tokens[0].Type.Should().Be(TokenType.Boolean);
        tokens[0].Value.Should().Be("TRUE");
    }

    [Fact]
    public void Tokenizes_AbsoluteCellRef_BothAnchors()
    {
        var tokens = new Lexer("=$A$1").Tokenize();
        tokens[0].Type.Should().Be(TokenType.CellRef);
        tokens[0].Value.Should().Be("$A$1");
    }

    [Fact]
    public void Tokenizes_AbsoluteCellRef_ColOnly()
    {
        var tokens = new Lexer("=$B3").Tokenize();
        tokens[0].Type.Should().Be(TokenType.CellRef);
        tokens[0].Value.Should().Be("$B3");
    }

    [Fact]
    public void Tokenizes_AbsoluteCellRef_RowOnly()
    {
        var tokens = new Lexer("=C$5").Tokenize();
        tokens[0].Type.Should().Be(TokenType.CellRef);
        tokens[0].Value.Should().Be("C$5");
    }

    [Fact]
    public void Tokenizes_RelativeCellRef_Unchanged()
    {
        var tokens = new Lexer("=D10").Tokenize();
        tokens[0].Type.Should().Be(TokenType.CellRef);
        tokens[0].Value.Should().Be("D10");
    }

    [Fact]
    public void Tokenizes_QuotedSheetQualifier_WithSpace()
    {
        var tokens = new Lexer("='My Sheet'!A1").Tokenize();
        tokens[0].Type.Should().Be(TokenType.SheetQualifier);
        tokens[0].Value.Should().Be("My Sheet");
        tokens[1].Type.Should().Be(TokenType.CellRef);
        tokens[1].Value.Should().Be("A1");
    }

    [Fact]
    public void Tokenizes_QuotedSheetQualifier_WithEscapedApostrophe()
    {
        var tokens = new Lexer("='Bob''s Sheet'!A1").Tokenize();
        tokens[0].Type.Should().Be(TokenType.SheetQualifier);
        tokens[0].Value.Should().Be("Bob's Sheet");
    }

    [Theory]
    [InlineData("#REF!")]
    [InlineData("#N/A")]
    [InlineData("#DIV/0!")]
    [InlineData("#VALUE!")]
    [InlineData("#NAME?")]
    [InlineData("#NULL!")]
    [InlineData("#NUM!")]
    public void Tokenizes_ErrorLiteral(string errorCode)
    {
        var tokens = new Lexer("=" + errorCode).Tokenize();
        tokens[0].Type.Should().Be(TokenType.Error);
        tokens[0].Value.Should().Be(errorCode);
    }

    [Fact]
    public void Lexer_TabWhitespace_IsSkippedLikeSpace()
    {
        var tokens = new Lexer("=1\t+\t2").Tokenize();
        tokens.Select(t => t.Type).Should().Contain(TokenType.Number).And.Contain(TokenType.Plus);
    }

    [Fact]
    public void Lexer_FourLetterColumnLikeWord_IsNotACellReference()
    {
        // "ABCD1" has 4 letters — exceeds max column XFD (3 letters), should be NamedRange/Identifier, not CellRef
        var tokens = new Lexer("=ABCD1").Tokenize();
        tokens.Should().NotContain(t => t.Type == TokenType.CellRef);
    }
}
