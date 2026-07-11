using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R28-formula-parser-deep-2-1: FormulaSerializer.WriteNode had no case for ArrayConstantNode,
// so re-serializing a formula containing an array-constant literal (e.g. after a structural
// rewrite such as insert/delete row or a sheet rename runs the formula through
// Parser -> FormulaSerializer) silently dropped the array constant entirely, corrupting or
// blanking the formula. Covers the bug scenarios plus a sibling already-working case
// (numeric-only array constant) and a structured-reference no-regression check per the
// round-27 keyword-aware escaping fix.
public class R28FormulaSerializerArrayConstantTests
{
    private static string RoundTrip(string formula)
    {
        var tokens = new Lexer(formula).Tokenize();
        var ast = new Parser(tokens).Parse();
        return FormulaSerializer.Serialize(ast);
    }

    [Fact]
    public void Serialize_ArrayConstant_AsEntireFormula_DoesNotBlankTheFormula()
    {
        // Previously produced "" (empty string) — total data loss.
        RoundTrip("={1,2;3,4}").Should().Be("{1,2;3,4}");
    }

    [Fact]
    public void Serialize_ArrayConstant_MultipliedByRange_DoesNotDropTheArrayOrCorruptTheOperator()
    {
        // Previously produced "SUM(*A1:A3)" — a dangling '*' with the array silently deleted.
        RoundTrip("=SUM({1,2,3}*A1:A3)").Should().Be("SUM({1,2,3}*A1:A3)");
    }

    [Fact]
    public void Serialize_ArrayConstant_WithStringsBooleansAndErrors_RoundTrips()
    {
        RoundTrip("=VLOOKUP(A1,{1,\"a\";2,\"b\"},2,0)")
            .Should().Be("VLOOKUP(A1,{1,\"a\";2,\"b\"},2,0)");
    }

    [Fact]
    public void Serialize_ArrayConstant_WithNegativeNumberAndBooleanAndError_RoundTrips()
    {
        RoundTrip("={-1,TRUE,#N/A}").Should().Be("{-1,TRUE,#N/A}");
    }

    [Fact]
    public void Serialize_ArrayConstant_SingleRow_RoundTrips()
    {
        // Sibling already-working shape: a plain row-only numeric array constant.
        RoundTrip("=SUM({1,2,3})").Should().Be("SUM({1,2,3})");
    }

    [Fact]
    public void Serialize_StructuredReference_WithHashKeywordSelector_StillPassesThroughUnescaped()
    {
        // No-regression guard for the round-27 keyword-aware structured-ref escaping fix:
        // a genuine #Data section keyword must NOT be re-escaped as a literal column name.
        RoundTrip("=SUM(Sales[#Data])").Should().Be("SUM(SALES[#Data])");
    }

    [Fact]
    public void Serialize_StructuredReference_WithLiteralHashColumnName_StillEscapesTheHash()
    {
        // A literal column named "#Count" (escaped on input as '#Count) must still round-trip
        // as an escaped literal, not be mistaken for a section keyword.
        RoundTrip("=SUM(Sales['#Count])").Should().Be("SUM(SALES['#Count])");
    }
}
