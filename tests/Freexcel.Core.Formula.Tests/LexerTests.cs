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
}
