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

    // ── HA4: m:d sepChr (separator between multiple m:e children) ─────────

    [Fact]
    public void Delim_WithTwoElements_NoSepChr_DefaultsToComma()
    {
        var node = Parse("<m:d><m:e><m:r><m:t>x</m:t></m:r></m:e><m:e><m:r><m:t>y</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal(",", delim.SepChar);
        Assert.Equal(2, delim.Elements.Count);
    }

    [Fact]
    public void Delim_WithTwoElements_ExplicitSepChr_UsesThatChar()
    {
        var node = Parse("<m:d><m:dPr><m:sepChr m:val=\"|\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e><m:e><m:r><m:t>P(x)</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal("|", delim.SepChar);
    }

    [Fact]
    public void Delim_WithExplicitEmptySepChr_HasNoSeparatorGlyph()
    {
        var node = Parse("<m:d><m:dPr><m:sepChr m:val=\"\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e><m:e><m:r><m:t>y</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal(string.Empty, delim.SepChar);
    }

    [Fact]
    public void Delim_WithSingleElement_SepCharIrrelevant_LayoutHasNoSeparator()
    {
        // Single m:e: even with a default (absent) sepChr, no separator should ever
        // be rendered — the layout test (MathLayoutEngineTests) asserts the box tree;
        // here we just confirm the parser still produces exactly one element.
        var node = Parse("<m:d><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Single(delim.Elements);
        Assert.Equal(",", delim.SepChar); // parsed default, but layout must not draw it
    }

    // ── HA6: m:f fPr/type (fraction bar style) ─────────────────────────────

    [Fact]
    public void LimLow_ParsesBaseAndLowerLimit()
    {
        var node = Parse("<m:limLow><m:e><m:r><m:t>lim</m:t></m:r></m:e><m:lim><m:r><m:t>x->0</m:t></m:r></m:lim></m:limLow>");

        var limit = Assert.IsType<MathNode.Limit>(node);
        Assert.False(limit.IsUpper);
        Assert.Equal("lim", Assert.IsType<MathNode.Run>(limit.Base).Text);
        Assert.Equal("x->0", Assert.IsType<MathNode.Run>(limit.LimitValue).Text);
    }

    [Fact]
    public void LimUpp_ParsesBaseAndUpperLimit()
    {
        var node = Parse("<m:limUpp><m:e><m:r><m:t>max</m:t></m:r></m:e><m:lim><m:r><m:t>S</m:t></m:r></m:lim></m:limUpp>");

        var limit = Assert.IsType<MathNode.Limit>(node);
        Assert.True(limit.IsUpper);
        Assert.Equal("max", Assert.IsType<MathNode.Run>(limit.Base).Text);
        Assert.Equal("S", Assert.IsType<MathNode.Run>(limit.LimitValue).Text);
    }

    [Fact]
    public void Parse_EqArray_ReturnsOrderedRows()
    {
        var node = Parse("<m:eqArr><m:e><m:r><m:t>x</m:t></m:r><m:r><m:t>+1</m:t></m:r></m:e><m:e><m:r><m:t>y</m:t></m:r></m:e></m:eqArr>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(2, eqArray.Rows.Count);

        var firstRow = Assert.IsType<MathNode.Row>(eqArray.Rows[0]);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(firstRow.Children[0]).Text);
        Assert.Equal("+1", Assert.IsType<MathNode.Run>(firstRow.Children[1]).Text);
        Assert.Equal("y", Assert.IsType<MathNode.Run>(eqArray.Rows[1]).Text);
    }

    [Fact]
    public void Frac_WithNoFPr_DefaultsToBar()
    {
        var node = Parse("<m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f>");

        var frac = Assert.IsType<MathNode.Frac>(node);
        Assert.Equal(MathNode.FracType.Bar, frac.Type);
    }

    [Theory]
    [InlineData("bar", MathNode.FracType.Bar)]
    [InlineData("skw", MathNode.FracType.Skewed)]
    [InlineData("lin", MathNode.FracType.Linear)]
    [InlineData("noBar", MathNode.FracType.NoBar)]
    public void Frac_WithExplicitType_MapsToEnum(string val, MathNode.FracType expected)
    {
        var node = Parse($"<m:f><m:fPr><m:type m:val=\"{val}\"/></m:fPr><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f>");

        var frac = Assert.IsType<MathNode.Frac>(node);
        Assert.Equal(expected, frac.Type);
    }

    [Fact]
    public void Frac_WithUnknownType_DefaultsToBar()
    {
        var node = Parse("<m:f><m:fPr><m:type m:val=\"bogus\"/></m:fPr><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>2</m:t></m:r></m:den></m:f>");

        var frac = Assert.IsType<MathNode.Frac>(node);
        Assert.Equal(MathNode.FracType.Bar, frac.Type);
    }
}
