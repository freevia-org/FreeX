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

    // R29-non-xlsx-format-roundtrip-2: a 3-D sheet-span reference (Sheet1:Sheet3!A1) must become a
    // properly bracketed OpenFormula range with distinct start/end sheet names, not leave the
    // "Sheet1:" span prefix as bare unbracketed text glued to a bracketed tail.
    [Fact]
    public void ToOdf_WrapsThreeDSheetSpanSingleCellReferenceInOneBracketedRange()
    {
        OdsFormulaConverter.ToOdf("SUM(Sheet1:Sheet3!A1)").Should().Be("SUM([$Sheet1.A1:$Sheet3.A1])");
    }

    [Fact]
    public void ToOdf_WrapsThreeDSheetSpanRangeReference()
    {
        OdsFormulaConverter.ToOdf("SUM(Sheet1:Sheet3!A1:B5)").Should().Be("SUM([$Sheet1.A1:$Sheet3.B5])");
    }

    [Fact]
    public void ToOdf_WrapsThreeDSheetSpanWithQuotedSheetNames()
    {
        OdsFormulaConverter.ToOdf("SUM('Sheet 1:Sheet 3'!A1)").Should().Be("SUM([$'Sheet 1'.A1:$'Sheet 3'.A1])");
    }

    // Sibling case already handled before this fix: an ordinary (non-span) cross-sheet range whose
    // two endpoints each carry their own sheet name must keep working unchanged.
    [Fact]
    public void ToOdf_StillEmitsRightEndpointSheetForOrdinaryCrossSheetRange()
    {
        OdsFormulaConverter.ToOdf("Data!A1:Other!B2").Should().Be("[$Data.A1:$Other.B2]");
    }

    // R29-non-xlsx-format-roundtrip-3: a multi-row array constant's ',' column separator and ';' row
    // separator must map to OpenFormula's distinct ';' / '|' separators instead of both collapsing to
    // ';', which would make the array's row/column shape unrecoverable.
    [Fact]
    public void ToOdf_TranslatesArrayConstantRowAndColumnSeparatorsDistinctly()
    {
        OdsFormulaConverter.ToOdf("SUM({1,2;3,4})").Should().Be("SUM({1;2|3;4})");
    }

    [Fact]
    public void ToA1_TranslatesArrayConstantSeparatorsBackFromOpenFormula()
    {
        OdsFormulaConverter.ToA1("SUM({1;2|3;4})").Should().Be("SUM({1,2;3,4})");
    }

    // Sibling case: an array constant used alongside ordinary (outside-the-braces) function
    // arguments must keep translating those with the normal ',' -> ';' rule.
    [Fact]
    public void ToOdf_TranslatesArrayConstantAndSurroundingArgumentSeparatorsTogether()
    {
        OdsFormulaConverter.ToOdf("INDEX({1,2;3,4},2,1)").Should().Be("INDEX({1;2|3;4};2;1)");
    }

    // Note: no A1->ODF->A1 round-trip assertion for the 3-D span case here. ToA1's bracket-range
    // conversion (ConvertBracketRefToA1) already reconstitutes a differing-sheet range endpoint as
    // "StartSheet!Cell1:EndSheet!Cell2" (see ToA1_PreservesRightEndpointSheetWhenCrossSheetRangeEndpointsDiffer
    // above), which FreeX's own parser cannot re-parse as a 3-D span — a separate, pre-existing gap
    // in the ODF->A1 direction, outside this finding's scope (finding 2 is specifically about ToOdf
    // emitting invalid unbracketed OpenFormula for a span; ToOdf's own output is asserted directly by
    // the Facts above). FreeX's own round-trip is unaffected in practice because OdsFileAdapter prefers
    // the verbatim "freex-a1-formula" hint over ToA1 when reloading a file FreeX itself wrote.
    [Theory]
    [InlineData("SUM({1,2;3,4})")]
    [InlineData("INDEX({1,2;3,4},2,1)")]
    public void RoundTrip_ArrayConstant_A1_To_Odf_To_A1_IsStable(string a1)
    {
        var odf = OdsFormulaConverter.ToOdf(a1);
        OdsFormulaConverter.ToA1(odf).Should().Be(a1);
    }
}
