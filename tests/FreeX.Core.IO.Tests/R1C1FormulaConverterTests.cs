using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the shared R1C1 ⇄ A1 converter extracted from SpreadsheetXmlFileAdapter so both it and the
/// SYLK adapter reuse one scanner.
/// </summary>
public sealed class R1C1FormulaConverterTests
{
    [Theory]
    // From C3 (row 3, col 3):
    [InlineData("RC[-1]+R[-1]C", 3u, 3u, "B3+C2")]      // same row prev col + prev row same col
    [InlineData("R1C1", 3u, 3u, "$A$1")]                // absolute
    [InlineData("SUM(R[-2]C:R[-1]C)", 3u, 3u, "SUM(C1:C2)")]
    [InlineData("RC", 5u, 4u, "D5")]                    // bare RC = self
    public void ToA1_ConvertsR1C1RelativeAndAbsolute(string r1c1, uint row, uint col, string expected)
    {
        R1C1FormulaConverter.ToA1(r1c1, row, col).Should().Be(expected);
    }

    [Theory]
    [InlineData("B3+C2", 3u, 3u, "RC[-1]+R[-1]C")]
    [InlineData("$A$1", 3u, 3u, "R1C1")]
    [InlineData("SUM(C1:C2)", 3u, 3u, "SUM(R[-2]C:R[-1]C)")]
    public void ToR1C1_ConvertsA1RelativeAndAbsolute(string a1, uint row, uint col, string expected)
    {
        R1C1FormulaConverter.ToR1C1(a1, row, col).Should().Be(expected);
    }

    [Fact]
    public void Roundtrip_A1ToR1C1AndBackIsStable()
    {
        const uint row = 7, col = 5;
        const string a1 = "IF(A1>0,$B$2,E7)";
        var r1c1 = R1C1FormulaConverter.ToR1C1(a1, row, col);
        R1C1FormulaConverter.ToA1(r1c1, row, col).Should().Be(a1);
    }

    [Fact]
    public void ToA1_LeavesStringLiteralsAndFunctionNamesUntouched()
    {
        // "RC" inside a string literal must not be rewritten; ROUND( is a function, not a ref.
        var result = R1C1FormulaConverter.ToA1("ROUND(RC[-1],2)&\"RC text\"", 2u, 2u);
        result.Should().Be("ROUND(A2,2)&\"RC text\"");
    }

    [Fact]
    public void LooksLikeR1C1_DetectsReferencesButNotPlainA1()
    {
        R1C1FormulaConverter.LooksLikeR1C1("RC[-1]+1").Should().BeTrue();
        R1C1FormulaConverter.LooksLikeR1C1("A1+B2").Should().BeFalse();
    }
}
