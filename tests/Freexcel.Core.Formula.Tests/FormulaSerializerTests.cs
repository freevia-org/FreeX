using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public class FormulaSerializerTests
{
    private static string RoundTrip(string formula)
    {
        var tokens = new Lexer(formula).Tokenize();
        var ast = new Parser(tokens).Parse();
        return FormulaSerializer.Serialize(ast);
    }

    [Fact]
    public void Serialize_Number() => RoundTrip("=42").Should().Be("42");

    [Fact]
    public void Serialize_Decimal() => RoundTrip("=3.14").Should().Be("3.14");

    [Fact]
    public void Serialize_String() => RoundTrip("=\"hello\"").Should().Be("\"hello\"");

    [Fact]
    public void Serialize_StringWithEmbeddedQuote()
    {
        RoundTrip("=\"say \"\"hi\"\"\"").Should().Be("\"say \"\"hi\"\"\"");
    }

    [Fact]
    public void Serialize_BoolTrue() => RoundTrip("=TRUE").Should().Be("TRUE");

    [Fact]
    public void Serialize_BoolFalse() => RoundTrip("=FALSE").Should().Be("FALSE");

    [Fact]
    public void Serialize_RelativeCellRef() => RoundTrip("=A1").Should().Be("A1");

    [Fact]
    public void Serialize_BothAbsoluteCellRef() => RoundTrip("=$A$1").Should().Be("$A$1");

    [Fact]
    public void Serialize_ColAbsoluteCellRef() => RoundTrip("=$B3").Should().Be("$B3");

    [Fact]
    public void Serialize_RowAbsoluteCellRef() => RoundTrip("=C$5").Should().Be("C$5");

    [Fact]
    public void Serialize_RangeRef() => RoundTrip("=A1:C3").Should().Be("A1:C3");

    [Fact]
    public void Serialize_AbsoluteRangeRef() => RoundTrip("=$A$1:$C$3").Should().Be("$A$1:$C$3");

    [Fact]
    public void Serialize_SheetQualifiedRef() => RoundTrip("=Sheet2!A1").Should().Be("Sheet2!A1");

    [Fact]
    public void Serialize_SheetQualifiedRange() => RoundTrip("=Sheet2!A1:B2").Should().Be("Sheet2!A1:B2");

    [Fact]
    public void Serialize_FunctionCall() => RoundTrip("=SUM(A1:A3)").Should().Be("SUM(A1:A3)");

    [Fact]
    public void Serialize_FunctionNoArgs() => RoundTrip("=NOW()").Should().Be("NOW()");

    [Fact]
    public void Serialize_BinaryAdd() => RoundTrip("=A1+B1").Should().Be("A1+B1");

    [Fact]
    public void Serialize_BinarySubtract() => RoundTrip("=A1-B1").Should().Be("A1-B1");

    [Fact]
    public void Serialize_BinaryMultiply() => RoundTrip("=A1*B1").Should().Be("A1*B1");

    [Fact]
    public void Serialize_BinaryDivide() => RoundTrip("=A1/B1").Should().Be("A1/B1");

    [Fact]
    public void Serialize_BinaryPower() => RoundTrip("=A1^2").Should().Be("A1^2");

    [Fact]
    public void Serialize_BinaryConcat() => RoundTrip("=A1&B1").Should().Be("A1&B1");

    [Fact]
    public void Serialize_ComparisonEqual() => RoundTrip("=A1=B1").Should().Be("A1=B1");

    [Fact]
    public void Serialize_ComparisonNotEqual() => RoundTrip("=A1<>B1").Should().Be("A1<>B1");

    [Fact]
    public void Serialize_ComparisonLessThan() => RoundTrip("=A1<B1").Should().Be("A1<B1");

    [Fact]
    public void Serialize_ComparisonGreaterThan() => RoundTrip("=A1>B1").Should().Be("A1>B1");

    [Fact]
    public void Serialize_UnaryNegate() => RoundTrip("=-A1").Should().Be("-A1");

    [Fact]
    public void Serialize_UnaryPercent() => RoundTrip("=A1%").Should().Be("A1%");

    [Fact]
    public void Serialize_ComplexFormula()
    {
        RoundTrip("=IF(A1>0,SUM(B1:B10),0)").Should().Be("IF(A1>0,SUM(B1:B10),0)");
    }

    [Fact]
    public void Serialize_ErrorNode()
    {
        var node = new ErrorNode(ErrorValue.Ref);
        FormulaSerializer.Serialize(node).Should().Be("#REF!");
    }
}
