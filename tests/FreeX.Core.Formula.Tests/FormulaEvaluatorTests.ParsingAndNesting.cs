using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FormulaEvaluatorTests
{
    // ── Nested functions ──

    [Fact]
    public void Nested_SumAndIf()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(a2, new NumberValue(20));
        sheet.SetCell(a3, new NumberValue(30));
        _evaluator.Evaluate("=IF(SUM(A1:A3)>50,\"big\",\"small\")", sheet)
            .Should().Be(new TextValue("big"));
    }

    // ── Parser: $ flags ──

    [Fact]
    public void Parse_AbsoluteRef_BothAnchors_IsColAbsolute_And_IsRowAbsolute()
    {
        var tokens = new Lexer("=$A$1").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeTrue();
        cell.IsRowAbsolute.Should().BeTrue();
        cell.ColumnName.Should().Be("A");
        cell.Row.Should().Be(1);
    }

    [Fact]
    public void Parse_AbsoluteRef_ColOnly_IsColAbsolute_True_RowAbsolute_False()
    {
        var tokens = new Lexer("=$B3").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeTrue();
        cell.IsRowAbsolute.Should().BeFalse();
        cell.ColumnName.Should().Be("B");
        cell.Row.Should().Be(3);
    }

    [Fact]
    public void Parse_AbsoluteRef_RowOnly_IsColAbsolute_False_RowAbsolute_True()
    {
        var tokens = new Lexer("=C$5").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeFalse();
        cell.IsRowAbsolute.Should().BeTrue();
        cell.ColumnName.Should().Be("C");
        cell.Row.Should().Be(5);
    }

    [Fact]
    public void Parse_RelativeRef_BothFlags_False()
    {
        var tokens = new Lexer("=D10").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeFalse();
        cell.IsRowAbsolute.Should().BeFalse();
    }

    [Fact]
    public void Parse_CellRefBeyondWorksheetRows_ReturnsNamedRange()
    {
        var tokens = new Lexer("=A1048577").Tokenize();
        var ast = new Parser(tokens).Parse();

        ast.Should().BeOfType<NamedRangeNode>()
            .Subject.Name.Should().Be("A1048577");
    }
}
