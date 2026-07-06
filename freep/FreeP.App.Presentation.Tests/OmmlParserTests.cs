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

    [Fact]
    public void Nary_WithSubHideAndSupHide_DropsHiddenLimits()
    {
        var node = Parse(
            "<m:nary>" +
            "<m:naryPr><m:subHide/><m:supHide m:val=\"1\"/></m:naryPr>" +
            "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.Null(nary.SubLimit);
        Assert.Null(nary.SupLimit);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(nary.Operand).Text);
    }

    [Fact]
    public void Nary_WithSubHideAndSupHideExplicitlyOff_PreservesLimits()
    {
        var node = Parse(
            "<m:nary>" +
            "<m:naryPr><m:subHide m:val=\"false\"/><m:supHide m:val=\"0\"/></m:naryPr>" +
            "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.Equal("0", Assert.IsType<MathNode.Run>(nary.SubLimit).Text);
        Assert.Equal("n", Assert.IsType<MathNode.Run>(nary.SupLimit).Text);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(nary.Operand).Text);
    }

    // m:radPr/m:degHide CT_OnOff semantics.

    [Fact]
    public void Rad_WithBareDegHide_HidesDegree()
    {
        var node = Parse(
            "<m:rad>" +
            "<m:radPr><m:degHide/></m:radPr>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");

        var radical = Assert.IsType<MathNode.Rad>(node);
        Assert.Null(radical.Degree);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(radical.Radicand).Text);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("true")]
    public void Rad_WithDegHideOn_HidesDegree(string val)
    {
        var node = Parse(
            $"<m:rad>" +
            $"<m:radPr><m:degHide m:val=\"{val}\"/></m:radPr>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");

        var radical = Assert.IsType<MathNode.Rad>(node);
        Assert.Null(radical.Degree);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Rad_WithDegHideOff_PreservesDegree(string val)
    {
        var node = Parse(
            $"<m:rad>" +
            $"<m:radPr><m:degHide m:val=\"{val}\"/></m:radPr>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");

        var radical = Assert.IsType<MathNode.Rad>(node);
        Assert.Equal("3", Assert.IsType<MathNode.Run>(radical.Degree).Text);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(radical.Radicand).Text);
    }

    [Fact]
    public void Acc_WithNoChr_DefaultsToHatAndPreservesBase()
    {
        var node = Parse("<m:acc><m:e><m:r><m:t>x</m:t></m:r></m:e></m:acc>");

        var acc = Assert.IsType<MathNode.Acc>(node);
        Assert.Equal("^", acc.AccentChar);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(acc.Base).Text);
    }

    [Fact]
    public void Acc_WithExplicitChr_UsesThatAccent()
    {
        var node = Parse("<m:acc><m:accPr><m:chr m:val=\"~\"/></m:accPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:acc>");

        var acc = Assert.IsType<MathNode.Acc>(node);
        Assert.Equal("~", acc.AccentChar);
    }

    [Fact]
    public void Bar_WithNoPos_DefaultsToOverline()
    {
        var node = Parse("<m:bar><m:e><m:r><m:t>x</m:t></m:r></m:e></m:bar>");

        var bar = Assert.IsType<MathNode.Bar>(node);
        Assert.True(bar.IsOver);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(bar.Base).Text);
    }

    [Fact]
    public void Bar_WithBottomPos_UsesUnderline()
    {
        var node = Parse("<m:bar><m:barPr><m:pos m:val=\"bot\"/></m:barPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:bar>");

        var bar = Assert.IsType<MathNode.Bar>(node);
        Assert.False(bar.IsOver);
    }

    // m:d begChr explicit-empty vs absent.

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
        Assert.True(delim.Grow);
    }

    [Fact]
    public void Delim_WithExplicitEmptyEndChr_HasNoRightBracket_UnaffectedByFix()
    {
        var node = Parse("<m:d><m:dPr><m:endChr m:val=\"\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal(string.Empty, delim.EndChar);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Delim_WithGrowExplicitlyOff_DoesNotAutoSizeBrackets(string val)
    {
        var node = Parse($"<m:d><m:dPr><m:grow m:val=\"{val}\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.False(delim.Grow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("true")]
    public void Delim_WithGrowAbsentOrOn_AutoSizesBrackets(string val)
    {
        var grow = string.IsNullOrEmpty(val)
            ? string.Empty
            : $"<m:dPr><m:grow m:val=\"{val}\"/></m:dPr>";
        var node = Parse($"<m:d>{grow}<m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.True(delim.Grow);
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
    public void Parse_EqArray_StripsAlnMarkersAndPreservesAlignmentPointIndices()
    {
        var node = Parse(
            "<m:eqArr>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:aln/><m:r><m:t>=1</m:t></m:r></m:e>" +
            "<m:e><m:aln/><m:r><m:t>y=2</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>z</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(new int?[] { 1, 0, null }, eqArray.AlignmentPointIndices);

        var firstRow = Assert.IsType<MathNode.Row>(eqArray.Rows[0]);
        Assert.Equal(2, firstRow.Children.Count);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(firstRow.Children[0]).Text);
        Assert.Equal("=1", Assert.IsType<MathNode.Run>(firstRow.Children[1]).Text);
        Assert.Equal("y=2", Assert.IsType<MathNode.Run>(eqArray.Rows[1]).Text);
        Assert.Equal("z", Assert.IsType<MathNode.Run>(eqArray.Rows[2]).Text);
    }

    [Fact]
    public void Parse_RunManualBreak_StartsNewEquationArrayRow()
    {
        var node = Parse(
            "<m:r><m:t>x</m:t></m:r>" +
            "<m:r><m:rPr><m:brk/></m:rPr><m:t>y</m:t></m:r>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(2, eqArray.Rows.Count);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(eqArray.Rows[0]).Text);
        Assert.Equal("y", Assert.IsType<MathNode.Run>(eqArray.Rows[1]).Text);
        Assert.Equal(new int?[] { null, null }, eqArray.AlignmentPointIndices);
    }

    [Fact]
    public void Parse_BoxManualBreak_StartsNewEquationArrayRowAndReadsAlnAt()
    {
        var node = Parse(
            "<m:r><m:t>x</m:t></m:r>" +
            "<m:box><m:boxPr><m:brk m:alnAt=\"1\"/></m:boxPr><m:e><m:r><m:t>y</m:t></m:r></m:e></m:box>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(2, eqArray.Rows.Count);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(eqArray.Rows[0]).Text);

        var box = Assert.IsType<MathNode.Box>(eqArray.Rows[1]);
        Assert.Equal("y", Assert.IsType<MathNode.Run>(box.Base).Text);
        Assert.Equal(new int?[] { null, 1 }, eqArray.AlignmentPointIndices);
    }

    [Fact]
    public void Parse_DirectManualBreak_DoesNotCreateUnknownNode()
    {
        var node = Parse(
            "<m:r><m:t>x</m:t></m:r>" +
            "<m:brk/>" +
            "<m:r><m:t>y</m:t></m:r>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(2, eqArray.Rows.Count);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(eqArray.Rows[0]).Text);
        Assert.Equal("y", Assert.IsType<MathNode.Run>(eqArray.Rows[1]).Text);
    }

    [Fact]
    public void Parse_MatrixColumnAlignments_ReadsMcsAlnMetadata()
    {
        var node = Parse(
            "<m:m>" +
            "<m:mPr><m:mcs>" +
            "<m:mc><m:mcPr><m:aln m:val=\"left\"/></m:mcPr></m:mc>" +
            "<m:mc><m:mcPr><m:aln m:val=\"ctr\"/></m:mcPr></m:mc>" +
            "<m:mc><m:mcPr><m:aln m:val=\"right\"/></m:mcPr></m:mc>" +
            "<m:mc><m:mcPr><m:aln m:val=\"bogus\"/></m:mcPr></m:mc>" +
            "</m:mcs></m:mPr>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e><m:r><m:t>b</m:t></m:r></m:e></m:mr>" +
            "</m:m>");

        var matrix = Assert.IsType<MathNode.Matrix>(node);
        Assert.Equal(
            new[]
            {
                MathNode.Matrix.MatrixColumnAlignment.Left,
                MathNode.Matrix.MatrixColumnAlignment.Center,
                MathNode.Matrix.MatrixColumnAlignment.Right,
                MathNode.Matrix.MatrixColumnAlignment.Center
            },
            matrix.ColumnAlignments);
    }

    [Fact]
    public void Parse_MatrixProperties_ReadsBaseJustificationAndSpacingMetadata()
    {
        var node = Parse(
            "<m:m>" +
            "<m:mPr>" +
            "<m:baseJc m:val=\"top\"/>" +
            "<m:rSpRule m:val=\"2\"/>" +
            "<m:rSp m:val=\"7\"/>" +
            "<m:cGpRule m:val=\"3\"/>" +
            "<m:cGp m:val=\"24\"/>" +
            "<m:cSp m:val=\"120\"/>" +
            "</m:mPr>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e></m:mr>" +
            "</m:m>");

        var matrix = Assert.IsType<MathNode.Matrix>(node);
        Assert.Equal(MathNode.Matrix.MatrixBaseJustification.Top, matrix.BaseJustification);
        Assert.Equal(MathNode.Matrix.MatrixSpacingRule.Double, matrix.RowSpacingRule);
        Assert.Equal(7, matrix.RowSpacing);
        Assert.Equal(MathNode.Matrix.MatrixSpacingRule.Exactly, matrix.ColumnGapRule);
        Assert.Equal(24, matrix.ColumnGap);
        Assert.Equal(120, matrix.ColumnSpacingTwips);
    }

    [Fact]
    public void Parse_MatrixProperties_DefaultsMissingSpacingAndBaseJustification()
    {
        var node = Parse(
            "<m:m>" +
            "<m:mPr/>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e></m:mr>" +
            "</m:m>");

        var matrix = Assert.IsType<MathNode.Matrix>(node);
        Assert.Equal(MathNode.Matrix.MatrixBaseJustification.Center, matrix.BaseJustification);
        Assert.Null(matrix.RowSpacingRule);
        Assert.Null(matrix.RowSpacing);
        Assert.Null(matrix.ColumnGapRule);
        Assert.Null(matrix.ColumnGap);
        Assert.Null(matrix.ColumnSpacingTwips);
    }

    [Fact]
    public void Parse_MatrixWithRaggedRows_PreservesLaterExtraCells()
    {
        var node = Parse(
            "<m:m>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e></m:mr>" +
            "<m:mr><m:e><m:r><m:t>b</m:t></m:r></m:e><m:e><m:r><m:t>c</m:t></m:r></m:e><m:e><m:r><m:t>d</m:t></m:r></m:e></m:mr>" +
            "</m:m>");

        var matrix = Assert.IsType<MathNode.Matrix>(node);
        Assert.Equal(2, matrix.Rows.Count);
        Assert.Single(matrix.Rows[0]);
        Assert.Equal(3, matrix.Rows[1].Count);
        Assert.Equal("c", Assert.IsType<MathNode.Run>(matrix.Rows[1][1]).Text);
        Assert.Equal("d", Assert.IsType<MathNode.Run>(matrix.Rows[1][2]).Text);
        Assert.Empty(matrix.ColumnAlignments);
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

    [Fact]
    public void SPre_ParsesBaseSubAndSup_PreservingNestedChildren()
    {
        var node = Parse(
            "<m:sPre>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:rad><m:e><m:r><m:t>x</m:t></m:r></m:e></m:rad></m:den></m:f></m:e>" +
            "<m:sub><m:sSup><m:e><m:r><m:t>a</m:t></m:r></m:e><m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup></m:sub>" +
            "<m:sup><m:box><m:e><m:r><m:t>n</m:t></m:r></m:e></m:box></m:sup>" +
            "</m:sPre>");

        var pre = Assert.IsType<MathNode.PreSubSup>(node);
        var frac = Assert.IsType<MathNode.Frac>(pre.Base);
        Assert.Equal("1", Assert.IsType<MathNode.Run>(frac.Numerator).Text);
        var radical = Assert.IsType<MathNode.Rad>(frac.Denominator);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(radical.Radicand).Text);

        var nestedSubSup = Assert.IsType<MathNode.Sup>(pre.Sub);
        Assert.Equal("a", Assert.IsType<MathNode.Run>(nestedSubSup.Base).Text);
        Assert.Equal("2", Assert.IsType<MathNode.Run>(nestedSubSup.Script).Text);

        var supBox = Assert.IsType<MathNode.Box>(pre.Sup);
        Assert.Equal("n", Assert.IsType<MathNode.Run>(supBox.Base).Text);
    }

    [Fact]
    public void SPre_WithMissingSubAndSup_UsesEmptyUnknownScriptFallbacks()
    {
        var node = Parse("<m:sPre><m:e><m:r><m:t>x</m:t></m:r></m:e></m:sPre>");

        var pre = Assert.IsType<MathNode.PreSubSup>(node);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(pre.Base).Text);
        Assert.Equal(string.Empty, Assert.IsType<MathNode.Unknown>(pre.Sub).FallbackText);
        Assert.Equal(string.Empty, Assert.IsType<MathNode.Unknown>(pre.Sup).FallbackText);
    }

    [Fact]
    public void Box_PreservesNestedFractionAndRadicalChild()
    {
        var node = Parse(
            "<m:box><m:e>" +
            "<m:f>" +
            "<m:num><m:r><m:t>1</m:t></m:r></m:num>" +
            "<m:den><m:rad><m:e><m:r><m:t>x</m:t></m:r></m:e></m:rad></m:den>" +
            "</m:f>" +
            "</m:e></m:box>");

        var box = Assert.IsType<MathNode.Box>(node);
        var frac = Assert.IsType<MathNode.Frac>(box.Base);
        Assert.Equal("1", Assert.IsType<MathNode.Run>(frac.Numerator).Text);
        var radical = Assert.IsType<MathNode.Rad>(frac.Denominator);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(radical.Radicand).Text);
    }

    [Fact]
    public void Phantom_PreservesNestedFractionAndRadicalChild()
    {
        var node = Parse(
            "<m:phant><m:e>" +
            "<m:f>" +
            "<m:num><m:r><m:t>1</m:t></m:r></m:num>" +
            "<m:den><m:rad><m:e><m:r><m:t>x</m:t></m:r></m:e></m:rad></m:den>" +
            "</m:f>" +
            "</m:e></m:phant>");

        var phantom = Assert.IsType<MathNode.Phantom>(node);
        Assert.True(phantom.Show);
        Assert.False(phantom.ZeroWidth);
        Assert.False(phantom.ZeroAscent);
        Assert.False(phantom.ZeroDescent);
        Assert.False(phantom.TransparentSpacing);

        var frac = Assert.IsType<MathNode.Frac>(phantom.Base);
        Assert.Equal("1", Assert.IsType<MathNode.Run>(frac.Numerator).Text);
        var radical = Assert.IsType<MathNode.Rad>(frac.Denominator);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(radical.Radicand).Text);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Phantom_WithShowOff_IsHidden(string val)
    {
        var node = Parse(
            $"<m:phant><m:phantPr><m:show m:val=\"{val}\"/></m:phantPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:phant>");

        var phantom = Assert.IsType<MathNode.Phantom>(node);
        Assert.False(phantom.Show);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(phantom.Base).Text);
    }

    [Fact]
    public void Phantom_ParsesZeroAndTransparentSpacingFlags()
    {
        var node = Parse(
            "<m:phant>" +
            "<m:phantPr>" +
            "<m:zeroWid/>" +
            "<m:zeroAsc m:val=\"1\"/>" +
            "<m:zeroDesc m:val=\"on\"/>" +
            "<m:transp m:val=\"true\"/>" +
            "</m:phantPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:phant>");

        var phantom = Assert.IsType<MathNode.Phantom>(node);
        Assert.True(phantom.Show);
        Assert.True(phantom.ZeroWidth);
        Assert.True(phantom.ZeroAscent);
        Assert.True(phantom.ZeroDescent);
        Assert.True(phantom.TransparentSpacing);
    }

    [Fact]
    public void Phantom_WithMissingExpression_UsesFlattenedUnknownFallback()
    {
        var node = Parse("<m:phant><m:r><m:t>x</m:t></m:r></m:phant>");

        var phantom = Assert.IsType<MathNode.Phantom>(node);
        Assert.Equal("x", Assert.IsType<MathNode.Unknown>(phantom.Base).FallbackText);
    }

    [Fact]
    public void BorderBox_ParsesHiddenSideFlags()
    {
        var node = Parse(
            "<m:borderBox>" +
            "<m:borderBoxPr>" +
            "<m:hideTop/>" +
            "<m:hideBot m:val=\"1\"/>" +
            "<m:hideLeft m:val=\"0\"/>" +
            "<m:hideRight m:val=\"off\"/>" +
            "</m:borderBoxPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:borderBox>");

        var borderBox = Assert.IsType<MathNode.BorderBox>(node);
        Assert.False(borderBox.ShowTop);
        Assert.False(borderBox.ShowBottom);
        Assert.True(borderBox.ShowLeft);
        Assert.True(borderBox.ShowRight);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(borderBox.Base).Text);
        Assert.False(borderBox.StrikeHorizontal);
        Assert.False(borderBox.StrikeVertical);
        Assert.False(borderBox.StrikeBottomLeftToTopRight);
        Assert.False(borderBox.StrikeTopLeftToBottomRight);
    }

    [Fact]
    public void BorderBox_DefaultsAllSidesVisible_AndExplicitFalseDoesNotHide()
    {
        var defaultNode = Parse("<m:borderBox><m:e><m:r><m:t>x</m:t></m:r></m:e></m:borderBox>");
        var defaultBorderBox = Assert.IsType<MathNode.BorderBox>(defaultNode);
        Assert.True(defaultBorderBox.ShowTop);
        Assert.True(defaultBorderBox.ShowBottom);
        Assert.True(defaultBorderBox.ShowLeft);
        Assert.True(defaultBorderBox.ShowRight);
        Assert.False(defaultBorderBox.StrikeHorizontal);
        Assert.False(defaultBorderBox.StrikeVertical);
        Assert.False(defaultBorderBox.StrikeBottomLeftToTopRight);
        Assert.False(defaultBorderBox.StrikeTopLeftToBottomRight);

        var explicitFalseNode = Parse(
            "<m:borderBox>" +
            "<m:borderBoxPr>" +
            "<m:hideTop m:val=\"false\"/>" +
            "<m:hideBot m:val=\"0\"/>" +
            "<m:hideLeft m:val=\"off\"/>" +
            "<m:hideRight m:val=\"false\"/>" +
            "</m:borderBoxPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:borderBox>");
        var explicitFalseBorderBox = Assert.IsType<MathNode.BorderBox>(explicitFalseNode);
        Assert.True(explicitFalseBorderBox.ShowTop);
        Assert.True(explicitFalseBorderBox.ShowBottom);
        Assert.True(explicitFalseBorderBox.ShowLeft);
        Assert.True(explicitFalseBorderBox.ShowRight);
    }

    [Fact]
    public void BorderBox_ParsesStrikeAndDiagonalFlags()
    {
        var node = Parse(
            "<m:borderBox>" +
            "<m:borderBoxPr>" +
            "<m:strikeH/>" +
            "<m:strikeV m:val=\"true\"/>" +
            "<m:strikeBLTR m:val=\"1\"/>" +
            "<m:strikeTLBR m:val=\"off\"/>" +
            "</m:borderBoxPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:borderBox>");

        var borderBox = Assert.IsType<MathNode.BorderBox>(node);
        Assert.True(borderBox.StrikeHorizontal);
        Assert.True(borderBox.StrikeVertical);
        Assert.True(borderBox.StrikeBottomLeftToTopRight);
        Assert.False(borderBox.StrikeTopLeftToBottomRight);
    }
}
