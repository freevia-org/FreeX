using FreeP.App.Compositor.MathLayout;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for <see cref="OmmlParser"/> covering ECMA-376 default-value edge
/// cases for m:nary (limLoc / chr defaults), m:d (begChr explicit-empty), and
/// m:r/m:rPr/m:nor (CT_OnOff semantics).
/// </summary>
public sealed class OmmlParserTests
{
    private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static MathNode Parse(string oMathInner)
    {
        var xml = $"<m:oMath xmlns:m=\"{M}\">{oMathInner}</m:oMath>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

    // ── HA1: m:nary limLoc default ────────────────────────────────────────

    [Fact]
    public void Nary_WithNoLimLoc_DefaultsToSubSup_NotAboveBelow()
    {
        var node = Parse("<m:nary><m:naryPr/><m:sub><m:r><m:t>0</m:t></m:r></m:sub><m:sup><m:r><m:t>1</m:t></m:r></m:sup><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.False(nary.LimitsAboveBelow, "Absent m:limLoc must default to subSup (ECMA-376 §22.1.2.66 CT_LimLoc), not undOvr.");
    }

    [Fact]
    public void Nary_WithExplicitUndOvr_IsAboveBelow()
    {
        var node = Parse("<m:nary><m:naryPr><m:limLoc m:val=\"undOvr\"/></m:naryPr><m:sub><m:r><m:t>0</m:t></m:r></m:sub><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.True(nary.LimitsAboveBelow);
    }

    [Fact]
    public void Nary_WithExplicitSubSup_IsNotAboveBelow()
    {
        var node = Parse("<m:nary><m:naryPr><m:limLoc m:val=\"subSup\"/></m:naryPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.False(nary.LimitsAboveBelow);
    }

    // ── HA2: m:nary operator char default ─────────────────────────────────

    [Fact]
    public void Nary_WithNoChr_DefaultsToIntegralSign()
    {
        var node = Parse("<m:nary><m:naryPr/><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.Equal("∫", nary.OperatorChar); // ∫
    }

    [Fact]
    public void Nary_WithExplicitChr_UsesThatChar()
    {
        var node = Parse("<m:nary><m:naryPr><m:chr m:val=\"∑\"/></m:naryPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.Equal("∑", nary.OperatorChar); // ∑
    }

    // ── HA3: m:d begChr explicit-empty vs absent ──────────────────────────

    [Fact]
    public void Delim_WithExplicitEmptyBegChr_HasNoLeftBracket()
    {
        var node = Parse("<m:d><m:dPr><m:begChr m:val=\"\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal(string.Empty, delim.BegChar);
        Assert.NotEqual("|", delim.BegChar);
    }

    [Fact]
    public void Delim_WithAbsentBegChr_DefaultsToOpenParen()
    {
        var node = Parse("<m:d><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal("(", delim.BegChar);
        Assert.Equal(")", delim.EndChar);
    }

    [Fact]
    public void Delim_WithExplicitEmptyEndChr_HasNoRightBracket_UnaffectedByFix()
    {
        var node = Parse("<m:d><m:dPr><m:endChr m:val=\"\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal(string.Empty, delim.EndChar);
    }

    // ── HA5: m:nor as CT_OnOff ─────────────────────────────────────────────

    [Fact]
    public void Run_WithNorNoVal_IsUpright()
    {
        var node = Parse("<m:r><m:rPr><m:nor/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.False(run.IsItalic);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Run_WithNorExplicitlyOff_KeepsItalic(string val)
    {
        var node = Parse($"<m:r><m:rPr><m:nor m:val=\"{val}\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.True(run.IsItalic, $"m:nor val=\"{val}\" means NOT normal, so italic must be kept.");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("true")]
    public void Run_WithNorExplicitlyOn_IsUpright(string val)
    {
        var node = Parse($"<m:r><m:rPr><m:nor m:val=\"{val}\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.False(run.IsItalic);
    }

    [Fact]
    public void Run_WithNoNor_IsItalic()
    {
        var node = Parse("<m:r><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.True(run.IsItalic);
    }
}
