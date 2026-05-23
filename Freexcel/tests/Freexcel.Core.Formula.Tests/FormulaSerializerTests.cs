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
    public void Serialize_QuotedSheetQualifiedRef_WithSpace() => RoundTrip("='My Sheet'!A1").Should().Be("'My Sheet'!A1");

    [Fact]
    public void Serialize_QuotedSheetQualifiedRef_WithApostrophe() => RoundTrip("='Bob''s Sheet'!A1").Should().Be("'Bob''s Sheet'!A1");

    [Fact]
    public void Serialize_SheetQualifiedRange() => RoundTrip("=Sheet2!A1:B2").Should().Be("Sheet2!A1:B2");

    [Fact]
    public void Serialize_FullColumnRange() => RoundTrip("=A:A").Should().Be("A:A");

    [Fact]
    public void Serialize_AbsoluteFullColumnRange() => RoundTrip("=$A:$B").Should().Be("$A:$B");

    [Fact]
    public void Serialize_FullRowRange() => RoundTrip("=1:1").Should().Be("1:1");

    [Fact]
    public void Serialize_AbsoluteFullRowRange() => RoundTrip("=$1:$2").Should().Be("$1:$2");

    [Fact]
    public void Serialize_SheetQualifiedFullColumnRange() => RoundTrip("=Sheet2!A:A").Should().Be("Sheet2!A:A");

    [Fact]
    public void Serialize_SheetQualifiedAbsoluteFullColumnRange() => RoundTrip("=Sheet2!$A:$B").Should().Be("Sheet2!$A:$B");

    [Fact]
    public void Serialize_RepeatedSheetQualifiedFullColumnRange() => RoundTrip("=Sheet2!A:Sheet2!B").Should().Be("Sheet2!A:B");

    [Fact]
    public void Serialize_RepeatedSheetQualifiedAbsoluteFullColumnRange() => RoundTrip("=Sheet2!$A:Sheet2!$B").Should().Be("Sheet2!$A:$B");

    [Fact]
    public void Serialize_SheetQualifiedFullRowRange() => RoundTrip("=Sheet2!1:2").Should().Be("Sheet2!1:2");

    [Fact]
    public void Serialize_SheetQualifiedAbsoluteFullRowRange() => RoundTrip("=Sheet2!$1:$2").Should().Be("Sheet2!$1:$2");

    [Fact]
    public void Serialize_RepeatedSheetQualifiedFullRowRange() => RoundTrip("=Sheet2!1:Sheet2!2").Should().Be("Sheet2!1:2");

    [Fact]
    public void Serialize_RepeatedSheetQualifiedAbsoluteFullRowRange() => RoundTrip("=Sheet2!$1:Sheet2!$2").Should().Be("Sheet2!$1:$2");

    [Fact]
    public void Serialize_QuotedSheetQualifiedRange_WithSpace() => RoundTrip("='My Sheet'!A1:B2").Should().Be("'My Sheet'!A1:B2");

    [Fact]
    public void Serialize_FunctionCall() => RoundTrip("=SUM(A1:A3)").Should().Be("SUM(A1:A3)");

    [Fact]
    public void Serialize_CombinedStructuredReference()
    {
        RoundTrip("=SUM(Sales[[#Data],[Amount]])")
            .Should().Be("SUM(SALES[[#Data],[Amount]])");
    }

    [Fact]
    public void Serialize_CurrentRowStructuredReference()
    {
        RoundTrip("=[@Amount]").Should().Be("[@Amount]");
    }

    [Fact]
    public void Serialize_TableQualifiedCurrentRowStructuredReference()
    {
        RoundTrip("=Sales[@Amount]").Should().Be("SALES[@Amount]");
    }

    [Fact]
    public void Serialize_MultiColumnStructuredReference()
    {
        RoundTrip("=SUM(Sales[[Amount]:[Tax]])")
            .Should().Be("SUM(SALES[[Amount]:[Tax]])");
    }

    [Fact]
    public void Serialize_ThisRowStructuredReference()
    {
        RoundTrip("=SUM(Sales[[#This Row],[Amount]:[Tax]])")
            .Should().Be("SUM(SALES[[#This Row],[Amount]:[Tax]])");
    }

    [Fact]
    public void Serialize_UnqualifiedThisRowStructuredReference()
    {
        RoundTrip("=SUM([[#This Row],[Amount]:[Tax]])")
            .Should().Be("SUM([[#This Row],[Amount]:[Tax]])");
    }

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

    [Theory]
    [InlineData("=#NULL!", "#NULL!")]
    [InlineData("=#DIV/0!", "#DIV/0!")]
    [InlineData("=#VALUE!", "#VALUE!")]
    [InlineData("=#REF!", "#REF!")]
    [InlineData("=#NAME?", "#NAME?")]
    [InlineData("=#NUM!", "#NUM!")]
    [InlineData("=#N/A", "#N/A")]
    [InlineData("=#SPILL!", "#SPILL!")]
    [InlineData("=#CALC!", "#CALC!")]
    public void Serialize_ErrorLiteral(string formula, string expected)
    {
        RoundTrip(formula).Should().Be(expected);
    }

    [Theory]
    [InlineData("=TAKE(A1:C3,,2)", "TAKE(A1:C3,,2)")]
    [InlineData("=DROP(A1:C3,,1)", "DROP(A1:C3,,1)")]
    [InlineData("=EXPAND(A1:B1,,3)", "EXPAND(A1:B1,,3)")]
    [InlineData("=EXPAND(A1:B1,2,,\"pad\")", "EXPAND(A1:B1,2,,\"pad\")")]
    [InlineData("=INDEX(A1:B2,1,)", "INDEX(A1:B2,1,)")]
    public void Serialize_OmittedFunctionArguments(string formula, string expected)
    {
        RoundTrip(formula).Should().Be(expected);
    }

    [Fact]
    public void Serialize_ParensPreserved_AddInsideMultiply()
    {
        // =(A1+B1)*C1 → must stay (A1+B1)*C1
        RoundTrip("=(A1+B1)*C1").Should().Be("(A1+B1)*C1");
    }

    [Fact]
    public void Serialize_NoUnnecessaryParens_MultiplyInsideAdd()
    {
        // =A1*B1+C1 → A1*B1+C1 (no parens needed)
        RoundTrip("=A1*B1+C1").Should().Be("A1*B1+C1");
    }

    [Fact]
    public void Serialize_ParensPreserved_SubtractOnRight()
    {
        // =A1-(B1-C1) → must stay A1-(B1-C1)
        RoundTrip("=A1-(B1-C1)").Should().Be("A1-(B1-C1)");
    }

    [Fact]
    public void Serialize_ParensPreserved_NegateOfSum()
    {
        // =-(A1+B1) → must stay -(A1+B1)
        RoundTrip("=-(A1+B1)").Should().Be("-(A1+B1)");
    }

    [Fact]
    public void Serialize_NoUnnecessaryParens_Simple()
    {
        // =A1+B1 → A1+B1 (no parens)
        RoundTrip("=A1+B1").Should().Be("A1+B1");
    }

    [Fact]
    public void Serialize_NoUnnecessaryParens_SubtractChain()
    {
        // left-associative: A1-B1-C1 parses as (A1-B1)-C1; no LHS parens needed
        RoundTrip("=A1-B1-C1").Should().Be("A1-B1-C1");
    }

    [Fact]
    public void Serialize_NoUnnecessaryParens_DivideChain()
    {
        // left-associative: 12/3/2 parses as (12/3)/2; no LHS parens needed
        RoundTrip("=12/3/2").Should().Be("12/3/2");
    }

    [Fact]
    public void Serialize_PowerLhsNeedsParens()
    {
        // right-associative: (A1^B1)^C1 ≠ A1^B1^C1; LHS parens must be kept
        RoundTrip("=(A1^B1)^C1").Should().Be("(A1^B1)^C1");
    }
}
