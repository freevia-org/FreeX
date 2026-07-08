using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the A1 &lt;-&gt; OpenFormula bracketed-reference converter that the ODS adapter emits for
/// interop (the lossless model round-trip uses a verbatim A1 hint; this converter produces the
/// standards-conforming <c>of:=</c> formula consumed by LibreOffice/Calc). The round-trip
/// (A1 -&gt; ODF -&gt; A1) must be stable for ordinary references.
/// </summary>
public sealed class OdsFormulaConverterTests
{
    [Theory]
    [InlineData("A1", "[.A1]")]
    [InlineData("A1+B2", "[.A1]+[.B2]")]
    [InlineData("SUM(A1:B2)", "SUM([.A1:.B2])")]
    [InlineData("$A$1", "[.$A$1]")]
    [InlineData("$A1+B$2", "[.$A1]+[.B$2]")]
    public void ToOdf_WrapsReferencesInBrackets(string a1, string expectedOdf)
    {
        OdsFormulaConverter.ToOdf(a1).Should().Be(expectedOdf);
    }

    [Fact]
    public void ToOdf_LeavesStringLiteralsUntouched()
    {
        // The argument-separating commas become ODF ';' (see ToOdf_TranslatesArgumentSeparators),
        // but the string literal's contents are passed through verbatim either way.
        OdsFormulaConverter.ToOdf("IF(A1=\"B2 is text\",1,0)")
            .Should().Be("IF([.A1]=\"B2 is text\";1;0)");
    }

    [Theory]
    [InlineData("IF(A1>0,1,2)", "IF([.A1]>0;1;2)")]
    [InlineData("VLOOKUP(A1,B1:C10,2,FALSE)", "VLOOKUP([.A1];[.B1:.C10];2;FALSE)")]
    public void ToOdf_TranslatesArgumentSeparatorsToSemicolons(string a1, string expectedOdf)
    {
        OdsFormulaConverter.ToOdf(a1).Should().Be(expectedOdf);
    }

    [Theory]
    [InlineData("IF([.A1]>0;1;2)", "IF(A1>0,1,2)")]
    [InlineData("VLOOKUP([.A1];[.B1:.C10];2;FALSE())", "VLOOKUP(A1,B1:C10,2,FALSE())")]
    public void ToA1_TranslatesArgumentSeparatorsToCommas(string odf, string expectedA1)
    {
        OdsFormulaConverter.ToA1(odf).Should().Be(expectedA1);
    }

    [Fact]
    public void ToOdf_DoesNotMangleFunctionNames()
    {
        // LOG10 looks superficially like a column+row but is a function call.
        OdsFormulaConverter.ToOdf("LOG10(A1)").Should().Be("LOG10([.A1])");
    }

    [Theory]
    [InlineData("[.A1]", "A1")]
    [InlineData("[.A1]+[.B2]", "A1+B2")]
    [InlineData("SUM([.A1:.B2])", "SUM(A1:B2)")]
    [InlineData("[.$A$1]", "$A$1")]
    public void ToA1_UnwrapsBracketedReferences(string odf, string expectedA1)
    {
        OdsFormulaConverter.ToA1(odf).Should().Be(expectedA1);
    }

    [Fact]
    public void ToA1_ResolvesCrossSheetReference()
    {
        OdsFormulaConverter.ToA1("[$Data.A1]").Should().Be("Data!A1");
    }

    [Theory]
    [InlineData("[$Data.A1:$Data.B2]", "Data!A1:B2")]
    [InlineData("[$Data.A1:$Other.B2]", "Data!A1:Other!B2")]
    [InlineData("[$'Input Data'.A1:$'Output Data'.B2]", "'Input Data'!A1:'Output Data'!B2")]
    public void ToA1_PreservesRightEndpointSheetWhenCrossSheetRangeEndpointsDiffer(string odf, string expectedA1)
    {
        OdsFormulaConverter.ToA1(odf).Should().Be(expectedA1);
    }

    [Fact]
    public void ToOdf_EmitsRightEndpointSheetForCrossSheetRange()
    {
        OdsFormulaConverter.ToOdf("Data!A1:Other!B2").Should().Be("[$Data.A1:$Other.B2]");
    }

    [Fact]
    public void ToA1_StripsOfNamespacePrefix()
    {
        OdsFormulaConverter.ToA1("of:SUM([.A1:.B2])").Should().Be("SUM(A1:B2)");
    }

    [Theory]
    [InlineData("A1+B2*3")]
    [InlineData("SUM(A1:A10)/COUNT(B1:B10)")]
    [InlineData("IF(A1>0,\"yes\",\"no\")")]
    [InlineData("$C$5-D6")]
    public void RoundTrip_A1_To_Odf_To_A1_IsStable(string a1)
    {
        var odf = OdsFormulaConverter.ToOdf(a1);
        OdsFormulaConverter.ToA1(odf).Should().Be(a1);
    }
}
