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

    private static MathNode ParseParagraph(string oMathParaInner)
    {
        var xml = $"<m:oMathPara xmlns:m=\"{M}\">{oMathParaInner}</m:oMathPara>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

    private static MathNode ParseGraphicData(string graphicDataInner)
    {
        var xml = $"<a:graphicData xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                  $"xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\" " +
                  $"xmlns:m=\"{M}\">{graphicDataInner}</a:graphicData>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

    [Fact]
    public void PowerPointDefaults_UseCambriaMath_AndAuthoredFontStillWins()
    {
        var defaultNode = OmmlParser.ParsePowerPoint(
            $"<m:oMath xmlns:m=\"{M}\"><m:r><m:t>x</m:t></m:r></m:oMath>",
            "FALLBACK");
        var defaultRoot = Assert.IsType<MathNode.MathRoot>(defaultNode);
        Assert.Equal("Cambria Math", defaultRoot.Properties.MathFontFamily);

        var documentNode = OmmlParser.ParsePowerPoint(
            $"<m:oMath xmlns:m=\"{M}\"><m:r><m:t>x</m:t></m:r></m:oMath>",
            "FALLBACK",
            new MathNode.MathProperties(MathFontFamily: "Arial"));
        var documentRoot = Assert.IsType<MathNode.MathRoot>(documentNode);
        Assert.Equal("Arial", documentRoot.Properties.MathFontFamily);

        var authoredNode = OmmlParser.ParsePowerPoint(
            $"<m:oMath xmlns:m=\"{M}\"><m:mathPr><m:mathFont m:val=\"STIX Two Math\"/></m:mathPr>" +
            "<m:r><m:t>x</m:t></m:r></m:oMath>",
            "FALLBACK");
        var authoredRoot = Assert.IsType<MathNode.MathRoot>(authoredNode);
        Assert.Equal("STIX Two Math", authoredRoot.Properties.MathFontFamily);
    }

    [Fact]
    public void SmallFraction_UsesCtOnOffSemantics_AndPropertyByPropertyInheritance()
    {
        var absent = Parse("<m:r><m:t>x</m:t></m:r>");
        Assert.IsType<MathNode.Run>(absent);

        var bare = Assert.IsType<MathNode.MathRoot>(Parse(
            "<m:mathPr><m:smallFrac/></m:mathPr><m:r><m:t>x</m:t></m:r>"));
        Assert.True(bare.Properties.SmallFraction);

        foreach (var value in new[] { "1", "true", "on", "yes" })
        {
            var enabled = Assert.IsType<MathNode.MathRoot>(Parse(
                $"<m:mathPr><m:smallFrac m:val=\"{value}\"/></m:mathPr><m:r><m:t>x</m:t></m:r>"));
            Assert.True(enabled.Properties.SmallFraction);
        }

        foreach (var value in new[] { "0", "false", "off" })
        {
            var disabled = Assert.IsType<MathNode.MathRoot>(Parse(
                $"<m:mathPr><m:smallFrac m:val=\"{value}\"/></m:mathPr><m:r><m:t>x</m:t></m:r>"));
            Assert.False(disabled.Properties.SmallFraction);
        }

        var inherited = Assert.IsType<MathNode.MathRoot>(OmmlParser.Parse(
            $"<m:oMath xmlns:m=\"{M}\"><m:r><m:t>x</m:t></m:r></m:oMath>",
            "FALLBACK",
            new MathNode.MathProperties(SmallFraction: true)));
        Assert.True(inherited.Properties.SmallFraction);

        var overridden = Assert.IsType<MathNode.MathRoot>(OmmlParser.Parse(
            $"<m:oMath xmlns:m=\"{M}\"><m:mathPr><m:smallFrac m:val=\"0\"/></m:mathPr>" +
            "<m:r><m:t>x</m:t></m:r></m:oMath>",
            "FALLBACK",
            new MathNode.MathProperties(SmallFraction: true)));
        Assert.False(overridden.Properties.SmallFraction,
            "an explicit false must override the document default rather than coalescing as absent");
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

    [Fact]
    public void Nary_DocumentLimitDefaults_SelectByOperator_AndLocalLimLocWins()
    {
        var integral = AssertNary(Parse(
            "<m:mathPr><m:intLim m:val=\"subSup\"/><m:naryLim m:val=\"undOvr\"/></m:mathPr>" +
            "<m:nary><m:naryPr/><m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>1</m:t></m:r></m:sup><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>"));
        Assert.False(integral.LimitsAboveBelow,
            "integral operators must use document m:intLim when local m:limLoc is absent");

        var sum = AssertNary(Parse(
            "<m:mathPr><m:intLim m:val=\"subSup\"/><m:naryLim m:val=\"undOvr\"/></m:mathPr>" +
            "<m:nary><m:naryPr><m:chr m:val=\"S\"/></m:naryPr>" +
            "<m:sub><m:r><m:t>0</m:t></m:r></m:sub><m:sup><m:r><m:t>1</m:t></m:r></m:sup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>"));
        Assert.True(sum.LimitsAboveBelow,
            "non-integral n-ary operators must use document m:naryLim");

        var localOverride = AssertNary(Parse(
            "<m:mathPr><m:intLim m:val=\"subSup\"/></m:mathPr>" +
            "<m:nary><m:naryPr><m:limLoc m:val=\"undOvr\"/></m:naryPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>"));
        Assert.True(localOverride.LimitsAboveBelow,
            "local m:limLoc must override the document default");

        var valuelessLocal = AssertNary(Parse(
            "<m:mathPr><m:naryLim m:val=\"undOvr\"/></m:mathPr>" +
            "<m:nary><m:naryPr><m:chr m:val=\"S\"/><m:limLoc/></m:naryPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>"));
        Assert.False(valuelessLocal.LimitsAboveBelow,
            "a val-less local m:limLoc uses the CT_LimLoc subSup default and still overrides naryLim");
    }

    [Fact]
    public void Nary_DocumentLimitDefaults_HandleValuelessInvalid_AndNestedNodes()
    {
        var parsed = Parse(
            "<m:mathPr><m:intLim/><m:naryLim m:val=\"not-a-limit-location\"/></m:mathPr>" +
            "<m:nary><m:naryPr><m:chr m:val=\"S\"/></m:naryPr><m:e>" +
            "<m:nary><m:naryPr><m:limLoc m:val=\"undOvr\"/></m:naryPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>" +
            "</m:e></m:nary>");

        var outer = AssertNary(parsed);
        Assert.True(outer.LimitsAboveBelow,
            "an invalid naryLim must conservatively fall back to its documented undOvr default");
        var inner = Assert.IsType<MathNode.Nary>(outer.Operand);
        Assert.True(inner.LimitsAboveBelow,
            "nested n-ary parsing must receive the same immutable resolved properties and honor its local override");

        var valuelessIntegral = AssertNary(Parse(
            "<m:mathPr><m:intLim/></m:mathPr>" +
            "<m:nary><m:naryPr/><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>"));
        Assert.False(valuelessIntegral.LimitsAboveBelow,
            "val-less intLim defaults to subSup");
    }

    private static MathNode.Nary AssertNary(MathNode node) => node switch
    {
        MathNode.Nary nary => nary,
        MathNode.MathRoot root => Assert.IsType<MathNode.Nary>(root.Content),
        _ => throw new Xunit.Sdk.XunitException($"Expected n-ary node, got {node.GetType().Name}.")
    };

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

    [Fact]
    public void Nary_WithNoGrow_DefaultsOperatorGrowthOff()
    {
        var node = Parse("<m:nary><m:naryPr/><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.False(nary.GrowOperator, "absent m:naryPr/m:grow defaults off for n-ary operator growth.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("true")]
    public void Nary_WithGrowOn_PreservesOperatorGrowthFlag(string val)
    {
        var grow = string.IsNullOrEmpty(val)
            ? "<m:grow/>"
            : $"<m:grow m:val=\"{val}\"/>";
        var node = Parse($"<m:nary><m:naryPr>{grow}</m:naryPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.True(nary.GrowOperator);
    }

    [Fact]
    public void Nary_WithGrowAndHiddenLimits_PreservesGrowthAndDropsLimits()
    {
        var node = Parse(
            "<m:nary>" +
            "<m:naryPr><m:chr m:val=\"S\"/><m:grow/><m:subHide/><m:supHide/></m:naryPr>" +
            "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.True(nary.GrowOperator);
        Assert.Null(nary.SubLimit);
        Assert.Null(nary.SupLimit);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(nary.Operand).Text);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Nary_WithGrowOff_DoesNotRequestOperatorGrowth(string val)
    {
        var node = Parse($"<m:nary><m:naryPr><m:grow m:val=\"{val}\"/></m:naryPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:nary>");

        var nary = Assert.IsType<MathNode.Nary>(node);
        Assert.False(nary.GrowOperator);
    }

    // m:radPr/m:degHide CT_OnOff semantics.

    [Fact]
    public void Rad_WithDegreeAndNoDegHide_PreservesVisibleDegree()
    {
        var node = Parse(
            "<m:rad>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");

        var radical = Assert.IsType<MathNode.Rad>(node);
        Assert.Equal("3", Assert.IsType<MathNode.Run>(radical.Degree).Text);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(radical.Radicand).Text);
    }

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

    [Theory]
    [InlineData("&#x0304;")]
    [InlineData("&#x0305;")]
    [InlineData("&#x00AF;")]
    public void Acc_WithOverbarAccent_PreservesRuleAccentCharacter(string accent)
    {
        var node = Parse($"<m:acc><m:accPr><m:chr m:val=\"{accent}\"/></m:accPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:acc>");

        var acc = Assert.IsType<MathNode.Acc>(node);
        Assert.Equal(System.Net.WebUtility.HtmlDecode(accent), acc.AccentChar);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(acc.Base).Text);
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

    [Fact]
    public void GroupChr_WithNoChrAndNoPos_DefaultsToTopCurlyBrace()
    {
        var node = Parse("<m:groupChr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>");

        var groupChr = Assert.IsType<MathNode.GroupChr>(node);
        Assert.True(groupChr.IsAbove);
        Assert.Equal("\u23DE", groupChr.GrpChar);
        Assert.Equal(MathNode.GroupChr.GroupChrVerticalJustification.Top, groupChr.VerticalJustification);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(groupChr.Base).Text);
    }

    [Fact]
    public void GroupChr_WithBottomPosAndNoChr_DefaultsToBottomCurlyBrace()
    {
        var node = Parse("<m:groupChr><m:groupChrPr><m:pos m:val=\"bot\"/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>");

        var groupChr = Assert.IsType<MathNode.GroupChr>(node);
        Assert.False(groupChr.IsAbove);
        Assert.Equal("\u23DF", groupChr.GrpChar);
        Assert.Equal(MathNode.GroupChr.GroupChrVerticalJustification.Top, groupChr.VerticalJustification);
    }

    [Fact]
    public void GroupChr_WithTopPosAndNoChr_DefaultsToTopCurlyBrace()
    {
        var node = Parse("<m:groupChr><m:groupChrPr><m:pos m:val=\"top\"/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>");

        var groupChr = Assert.IsType<MathNode.GroupChr>(node);
        Assert.True(groupChr.IsAbove);
        Assert.Equal("\u23DE", groupChr.GrpChar);
        Assert.Equal(MathNode.GroupChr.GroupChrVerticalJustification.Top, groupChr.VerticalJustification);
    }

    [Fact]
    public void GroupChr_WithExplicitChr_PreservesRequestedGlyph()
    {
        var node = Parse("<m:groupChr><m:groupChrPr><m:chr m:val=\"\u23B4\"/><m:pos m:val=\"bot\"/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>");

        var groupChr = Assert.IsType<MathNode.GroupChr>(node);
        Assert.False(groupChr.IsAbove);
        Assert.Equal("\u23B4", groupChr.GrpChar);
    }

    [Fact]
    public void GroupChr_WithBareVertJc_DefaultsAttributeToBottomJustification()
    {
        var node = Parse("<m:groupChr><m:groupChrPr><m:vertJc/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>");

        var groupChr = Assert.IsType<MathNode.GroupChr>(node);
        Assert.Equal(MathNode.GroupChr.GroupChrVerticalJustification.Bottom, groupChr.VerticalJustification);
    }

    [Theory]
    [InlineData("top", MathNode.GroupChr.GroupChrVerticalJustification.Top)]
    [InlineData("bot", MathNode.GroupChr.GroupChrVerticalJustification.Bottom)]
    [InlineData("bottom", MathNode.GroupChr.GroupChrVerticalJustification.Bottom)]
    [InlineData("bogus", MathNode.GroupChr.GroupChrVerticalJustification.Top)]
    public void GroupChr_WithVertJc_PreservesSharedBaselineJustification(string val, MathNode.GroupChr.GroupChrVerticalJustification expected)
    {
        var node = Parse($"<m:groupChr><m:groupChrPr><m:vertJc m:val=\"{val}\"/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>");

        Assert.Equal(expected, Assert.IsType<MathNode.GroupChr>(node).VerticalJustification);
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
        Assert.False(run.IsBold);
        Assert.False(run.IsLiteral);
        Assert.Equal(MathNode.MathAlphabet.Default, run.Alphabet);
    }

    [Fact]
    public void Run_WithLiteralNoVal_IsLiteralAndUpright()
    {
        var node = Parse("<m:r><m:rPr><m:lit/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.Equal("x", run.Text);
        Assert.True(run.IsLiteral);
        Assert.False(run.IsItalic);
        Assert.False(run.IsBold);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Run_WithLiteralExplicitlyOff_KeepsDefaultMathVariableStyle(string val)
    {
        var node = Parse($"<m:r><m:rPr><m:lit m:val=\"{val}\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.False(run.IsLiteral);
        Assert.True(run.IsItalic);
    }

    [Fact]
    public void Run_WithLiteralAndExplicitItalicStyle_PreservesAuthoredVisualStyle()
    {
        var node = Parse("<m:r><m:rPr><m:lit/><m:sty m:val=\"i\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.True(run.IsLiteral);
        Assert.True(run.IsItalic);
    }

    [Fact]
    public void Run_WithMultipleTextChildren_ConcatenatesAllText()
    {
        var node = Parse("<m:r><m:t>sin</m:t><m:t>^2</m:t><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.Equal("sin^2x", run.Text);
    }

    [Theory]
    [InlineData("roman", MathNode.MathAlphabet.Roman)]
    [InlineData("script", MathNode.MathAlphabet.Script)]
    [InlineData("fraktur", MathNode.MathAlphabet.Fraktur)]
    [InlineData("double-struck", MathNode.MathAlphabet.DoubleStruck)]
    [InlineData("sans-serif", MathNode.MathAlphabet.SansSerif)]
    [InlineData("monospace", MathNode.MathAlphabet.Monospace)]
    public void Run_WithScr_MapsKnownMathAlphabet(string val, MathNode.MathAlphabet expected)
    {
        var node = Parse($"<m:r><m:rPr><m:scr m:val=\"{val}\"/></m:rPr><m:t>Ab1</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.Equal("Ab1", run.Text);
        Assert.Equal(expected, run.Alphabet);
    }

    [Fact]
    public void Run_WithUnknownScr_UsesDefaultAlphabet()
    {
        var node = Parse("<m:r><m:rPr><m:scr m:val=\"unknown\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.Equal(MathNode.MathAlphabet.Default, run.Alphabet);
        Assert.True(run.IsItalic);
    }

    [Fact]
    public void Run_WithStyPlain_IsUprightAndNotBold()
    {
        var node = Parse("<m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.False(run.IsItalic);
        Assert.False(run.IsBold);
    }

    [Fact]
    public void Run_WithStyItalic_IsItalicAndNotBold()
    {
        var node = Parse("<m:r><m:rPr><m:sty m:val=\"i\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.True(run.IsItalic);
        Assert.False(run.IsBold);
    }

    [Fact]
    public void Run_WithStyBold_IsUprightAndBold()
    {
        var node = Parse("<m:r><m:rPr><m:sty m:val=\"b\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.False(run.IsItalic);
        Assert.True(run.IsBold);
    }

    [Fact]
    public void Run_WithStyBoldItalic_IsItalicAndBold()
    {
        var node = Parse("<m:r><m:rPr><m:nor/><m:sty m:val=\"bi\"/></m:rPr><m:t>x</m:t></m:r>");

        var run = Assert.IsType<MathNode.Run>(node);
        Assert.True(run.IsItalic);
        Assert.True(run.IsBold);
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
    public void Delim_WithCenteredShape_PreservesSharedDelimiterShape()
    {
        var node = Parse("<m:d><m:dPr><m:shp m:val=\"centered\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>");

        var delim = Assert.IsType<MathNode.Delim>(node);
        Assert.Equal(MathNode.Delim.DelimiterShape.Centered, delim.Shape);
        Assert.True(delim.Grow, "m:shp is independent from the m:grow on/off flag");
    }

    [Fact]
    public void Delim_WithAbsentOrMatchShape_UsesExistingMatchedDelimiterShape()
    {
        var absent = Assert.IsType<MathNode.Delim>(
            Parse("<m:d><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>"));
        var match = Assert.IsType<MathNode.Delim>(
            Parse("<m:d><m:dPr><m:shp m:val=\"match\"/></m:dPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:d>"));

        Assert.Equal(MathNode.Delim.DelimiterShape.Match, absent.Shape);
        Assert.Equal(MathNode.Delim.DelimiterShape.Match, match.Shape);
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
    public void Func_FunctionNameDefaultsToUprightRun()
    {
        var node = Parse(
            "<m:func>" +
            "<m:fName><m:r><m:t>sin</m:t></m:r></m:fName>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:func>");

        var func = Assert.IsType<MathNode.Func>(node);
        var functionName = Assert.IsType<MathNode.Run>(func.FunctionName);
        Assert.Equal("sin", functionName.Text);
        Assert.False(functionName.IsItalic);

        var argument = Assert.IsType<MathNode.Run>(func.Argument);
        Assert.Equal("x", argument.Text);
        Assert.True(argument.IsItalic);
    }

    [Fact]
    public void Func_FunctionNameNormalizationPreservesBoldMetadata()
    {
        var node = Parse(
            "<m:func>" +
            "<m:fName><m:r><m:rPr><m:sty m:val=\"bi\"/></m:rPr><m:t>cos</m:t></m:r></m:fName>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:func>");

        var func = Assert.IsType<MathNode.Func>(node);
        var functionName = Assert.IsType<MathNode.Run>(func.FunctionName);
        Assert.Equal("cos", functionName.Text);
        Assert.False(functionName.IsItalic);
        Assert.True(functionName.IsBold);
    }

    [Fact]
    public void Func_WithScriptedFunctionName_NormalizesBaseNameToUpright()
    {
        var node = Parse(
            "<m:func>" +
            "<m:fName><m:sSup><m:e><m:r><m:t>sin</m:t></m:r></m:e><m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup></m:fName>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:func>");

        var func = Assert.IsType<MathNode.Func>(node);
        var scriptedName = Assert.IsType<MathNode.Sup>(func.FunctionName);
        var nameBase = Assert.IsType<MathNode.Run>(scriptedName.Base);
        Assert.Equal("sin", nameBase.Text);
        Assert.False(nameBase.IsItalic);
        Assert.Equal("2", Assert.IsType<MathNode.Run>(scriptedName.Script).Text);
        Assert.True(Assert.IsType<MathNode.Run>(func.Argument).IsItalic);
    }

    [Fact]
    public void Func_WithLimitedFunctionName_NormalizesLimitBaseToUpright()
    {
        var node = Parse(
            "<m:func>" +
            "<m:fName><m:limLow><m:e><m:r><m:t>lim</m:t></m:r></m:e><m:lim><m:r><m:t>x-&gt;0</m:t></m:r></m:lim></m:limLow></m:fName>" +
            "<m:e><m:r><m:t>f(x)</m:t></m:r></m:e>" +
            "</m:func>");

        var func = Assert.IsType<MathNode.Func>(node);
        var limitedName = Assert.IsType<MathNode.Limit>(func.FunctionName);
        var nameBase = Assert.IsType<MathNode.Run>(limitedName.Base);
        Assert.Equal("lim", nameBase.Text);
        Assert.False(nameBase.IsItalic);
        Assert.Equal("x->0", Assert.IsType<MathNode.Run>(limitedName.LimitValue).Text);
        Assert.True(Assert.IsType<MathNode.Run>(func.Argument).IsItalic);
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
    public void Parse_EqArray_BoxPropertyAlnPreservesAlignmentPointAndBox()
    {
        var node = Parse(
            "<m:eqArr>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:box><m:boxPr><m:opEmu/><m:aln/></m:boxPr><m:e><m:r><m:t>=1</m:t></m:r></m:e></m:box></m:e>" +
            "<m:e><m:r><m:t>wide</m:t></m:r><m:box><m:boxPr><m:opEmu/><m:aln/></m:boxPr><m:e><m:r><m:t>=2</m:t></m:r></m:e></m:box></m:e>" +
            "</m:eqArr>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(new int?[] { 1, 1 }, eqArray.AlignmentPointIndices);

        var firstRow = Assert.IsType<MathNode.Row>(eqArray.Rows[0]);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(firstRow.Children[0]).Text);
        var firstBox = Assert.IsType<MathNode.Box>(firstRow.Children[1]);
        Assert.Equal("=1", Assert.IsType<MathNode.Run>(firstBox.Base).Text);

        var secondRow = Assert.IsType<MathNode.Row>(eqArray.Rows[1]);
        Assert.Equal("wide", Assert.IsType<MathNode.Run>(secondRow.Children[0]).Text);
        var secondBox = Assert.IsType<MathNode.Box>(secondRow.Children[1]);
        Assert.Equal("=2", Assert.IsType<MathNode.Run>(secondBox.Base).Text);
    }

    [Fact]
    public void Parse_EqArray_PreservesMultipleAlignmentColumns()
    {
        var node = Parse(
            "<m:eqArr>" +
            "<m:e><m:r><m:t>a</m:t></m:r><m:aln/><m:r><m:t>=</m:t></m:r><m:aln/><m:r><m:t>1</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>long</m:t></m:r><m:aln/><m:r><m:t>=</m:t></m:r><m:aln/><m:r><m:t>22</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(new[] { 1, 2 }, eqArray.AlignmentPointColumns[0]);
        Assert.Equal(new[] { 1, 2 }, eqArray.AlignmentPointColumns[1]);
        Assert.Equal(new int?[] { 1, 1 }, eqArray.AlignmentPointIndices);
    }

    [Fact]
    public void Parse_EqArray_NestedAlignmentRemainsScopedToNestedArray()
    {
        var node = Parse(
            "<m:eqArr>" +
            "<m:e><m:r><m:t>outer</m:t></m:r>" +
            "<m:eqArr><m:e><m:r><m:t>x</m:t></m:r><m:aln/><m:r><m:t>=1</m:t></m:r></m:e></m:eqArr></m:e>" +
            "<m:e><m:r><m:t>tail</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var outer = Assert.IsType<MathNode.EqArray>(node);
        Assert.Null(outer.AlignmentPointIndices[0]);

        var outerRow = Assert.IsType<MathNode.Row>(outer.Rows[0]);
        var inner = Assert.IsType<MathNode.EqArray>(outerRow.Children[1]);
        Assert.Equal(new[] { 1 }, inner.AlignmentPointColumns[0]);
    }

    [Fact]
    public void Parse_EqArrayProperties_ReadsBaseJustificationAndRowSpacingMetadata()
    {
        var node = Parse(
            "<m:eqArr>" +
            "<m:eqArrPr>" +
            "<m:baseJc m:val=\"bot\"/>" +
            "<m:rSpRule m:val=\"3\"/>" +
            "<m:rSp m:val=\"18\"/>" +
            "</m:eqArrPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:aln/><m:r><m:t>=1</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>y</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(MathNode.EqArray.EqArrayBaseJustification.Bottom, eqArray.BaseJustification);
        Assert.Equal(MathNode.EqArray.EqArraySpacingRule.Exactly, eqArray.RowSpacingRule);
        Assert.Equal(18, eqArray.RowSpacing);
        Assert.Equal(new int?[] { 1, null }, eqArray.AlignmentPointIndices);
        Assert.Equal(2, eqArray.Rows.Count);
    }

    [Fact]
    public void Parse_EqArrayProperties_DefaultsMissingSpacingAndBaseJustification()
    {
        var node = Parse(
            "<m:eqArr>" +
            "<m:eqArrPr/>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var eqArray = Assert.IsType<MathNode.EqArray>(node);
        Assert.Equal(MathNode.EqArray.EqArrayBaseJustification.Center, eqArray.BaseJustification);
        Assert.Null(eqArray.RowSpacingRule);
        Assert.Null(eqArray.RowSpacing);
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
    public void Parse_MatrixColumnAlignments_RepeatsAlignmentByCount()
    {
        var node = Parse(
            "<m:m>" +
            "<m:mPr><m:mcs>" +
            "<m:mc><m:mcPr><m:count m:val=\"2\"/><m:aln m:val=\"left\"/></m:mcPr></m:mc>" +
            "<m:mc><m:mcPr><m:count m:val=\"0\"/><m:aln m:val=\"right\"/></m:mcPr></m:mc>" +
            "</m:mcs></m:mPr>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e><m:r><m:t>b</m:t></m:r></m:e><m:e><m:r><m:t>c</m:t></m:r></m:e></m:mr>" +
            "</m:m>");

        var matrix = Assert.IsType<MathNode.Matrix>(node);
        Assert.Equal(
            new[]
            {
                MathNode.Matrix.MatrixColumnAlignment.Left,
                MathNode.Matrix.MatrixColumnAlignment.Left,
                MathNode.Matrix.MatrixColumnAlignment.Right
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
        Assert.False(matrix.HidePlaceholders);
    }

    [Fact]
    public void Parse_MatrixPlcHide_PreservesHiddenPlaceholderFlag()
    {
        var node = Parse(
            "<m:m>" +
            "<m:mPr><m:plcHide/></m:mPr>" +
            "<m:mr><m:e/></m:mr>" +
            "</m:m>");

        var matrix = Assert.IsType<MathNode.Matrix>(node);
        Assert.True(matrix.HidePlaceholders);
        Assert.Single(matrix.Rows);
        Assert.Single(matrix.Rows[0]);
        Assert.Empty(Assert.IsType<MathNode.Row>(matrix.Rows[0][0]).Children);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void Parse_MatrixPlcHideExplicitlyOff_ShowsPlaceholders(string val)
    {
        var node = Parse(
            $"<m:m><m:mPr><m:plcHide m:val=\"{val}\"/></m:mPr><m:mr><m:e/></m:mr></m:m>");

        Assert.False(Assert.IsType<MathNode.Matrix>(node).HidePlaceholders);
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
    public void SSubSup_WithAlignScriptsOn_PreservesSharedAlignmentFlag()
    {
        var node = Parse(
            "<m:sSubSup>" +
            "<m:sSubSupPr><m:alnScr/></m:sSubSupPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sub><m:r><m:t>wide</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSubSup>");

        var subSup = Assert.IsType<MathNode.SubSup>(node);
        Assert.True(subSup.AlignScripts);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(subSup.Base).Text);
        Assert.Equal("wide", Assert.IsType<MathNode.Run>(subSup.Sub).Text);
        Assert.Equal("2", Assert.IsType<MathNode.Run>(subSup.Sup).Text);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("false")]
    public void SSubSup_WithAlignScriptsOff_UsesExistingUnalignedLayoutFlag(string val)
    {
        var node = Parse(
            $"<m:sSubSup><m:sSubSupPr><m:alnScr m:val=\"{val}\"/></m:sSubSupPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sub><m:r><m:t>i</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSubSup>");

        Assert.False(Assert.IsType<MathNode.SubSup>(node).AlignScripts);
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
    public void Phantom_TransparentMultiGlyphOperator_PreservesRunForSharedSpacing()
    {
        var node = Parse(
            "<m:phant>" +
            "<m:phantPr><m:show m:val=\"0\"/><m:zeroWid/><m:transp/></m:phantPr>" +
            "<m:e><m:r><m:t>-&gt;</m:t></m:r></m:e>" +
            "</m:phant>");

        var phantom = Assert.IsType<MathNode.Phantom>(node);
        Assert.False(phantom.Show);
        Assert.True(phantom.ZeroWidth);
        Assert.True(phantom.TransparentSpacing);
        Assert.Equal("->", Assert.IsType<MathNode.Run>(phantom.Base).Text);
    }

    [Fact]
    public void Phantom_WithMissingExpression_UsesFlattenedUnknownFallback()
    {
        var node = Parse("<m:phant><m:r><m:t>x</m:t></m:r></m:phant>");

        var phantom = Assert.IsType<MathNode.Phantom>(node);
        Assert.Equal("x", Assert.IsType<MathNode.Unknown>(phantom.Base).FallbackText);
    }

    [Fact]
    public void Box_WithOperatorEmulatorOn_PreservesSharedOperatorFlag()
    {
        var node = Parse(
            "<m:box>" +
            "<m:boxPr><m:opEmu/></m:boxPr>" +
            "<m:e><m:r><m:t>==</m:t></m:r></m:e>" +
            "</m:box>");

        var box = Assert.IsType<MathNode.Box>(node);
        Assert.True(box.OperatorEmulator);
        Assert.Equal("==", Assert.IsType<MathNode.Run>(box.Base).Text);
    }

    [Fact]
    public void Box_WithOperatorEmulatorOff_DoesNotPromoteOperatorSpacing()
    {
        var node = Parse(
            "<m:box>" +
            "<m:boxPr><m:opEmu m:val=\"false\"/></m:boxPr>" +
            "<m:e><m:r><m:t>==</m:t></m:r></m:e>" +
            "</m:box>");

        var box = Assert.IsType<MathNode.Box>(node);
        Assert.False(box.OperatorEmulator);
    }

    [Fact]
    public void BoxArgument_WithArgSizeMinusOne_WrapsBaseInSharedArgumentSizeNode()
    {
        var node = Parse(
            "<m:box>" +
            "<m:e><m:argPr><m:argSz m:val=\"-1\"/></m:argPr><m:r><m:t>abc</m:t></m:r></m:e>" +
            "</m:box>");

        var box = Assert.IsType<MathNode.Box>(node);
        var argSize = Assert.IsType<MathNode.ArgSize>(box.Base);
        Assert.Equal(-1, argSize.Adjustment);
        Assert.Equal("abc", Assert.IsType<MathNode.Run>(argSize.Base).Text);
    }

    [Fact]
    public void SuperscriptArgument_WithArgSizePlusOne_PreservesLargerScriptRequest()
    {
        var node = Parse(
            "<m:sSup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sup><m:argPr><m:argSz m:val=\"1\"/></m:argPr><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSup>");

        var sup = Assert.IsType<MathNode.Sup>(node);
        var argSize = Assert.IsType<MathNode.ArgSize>(sup.Script);
        Assert.Equal(1, argSize.Adjustment);
        Assert.Equal("2", Assert.IsType<MathNode.Run>(argSize.Base).Text);
    }

    [Theory]
    [InlineData("-4", -2)]
    [InlineData("4", 2)]
    public void ArgumentSize_ClampsToOmmlScriptLevelRange(string val, int expected)
    {
        var node = Parse($"<m:box><m:e><m:argPr><m:argSz m:val=\"{val}\"/></m:argPr><m:r><m:t>x</m:t></m:r></m:e></m:box>");

        var box = Assert.IsType<MathNode.Box>(node);
        Assert.Equal(expected, Assert.IsType<MathNode.ArgSize>(box.Base).Adjustment);
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

    [Theory]
    [InlineData("left", MathNode.MathParagraphJustification.Left)]
    [InlineData("right", MathNode.MathParagraphJustification.Right)]
    [InlineData("center", MathNode.MathParagraphJustification.Center)]
    [InlineData("centerGroup", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("bogus", MathNode.MathParagraphJustification.Center)]
    public void OMathPara_WithJustification_PreservesParagraphAlignmentMetadata(
        string val,
        MathNode.MathParagraphJustification expected)
    {
        var node = ParseParagraph(
            $"<m:oMathParaPr><m:jc m:val=\"{val}\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(expected, paragraph.Justification);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(paragraph.Content).Text);
    }

    [Fact]
    public void OMathPara_WithNoJustification_DefaultsToCenterGroup()
    {
        var node = ParseParagraph("<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(MathNode.MathParagraphJustification.CenterGroup, paragraph.Justification);
        Assert.Equal("x", Assert.IsType<MathNode.Run>(paragraph.Content).Text);
    }

    [Fact]
    public void OMathPara_MultipleEquations_PreservesRunAlignmentPointsAsSharedRows()
    {
        var node = ParseParagraph(
            "<m:oMath><m:r><m:t>mmmm</m:t></m:r>" +
            "<m:r><m:rPr><m:aln/></m:rPr><m:t>=1</m:t></m:r></m:oMath>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r>" +
            "<m:r><m:rPr><m:aln m:val=\"true\"/></m:rPr><m:t>=22</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        var equations = Assert.IsType<MathNode.EqArray>(paragraph.Content);
        Assert.True(equations.AlignRowsLeft);
        Assert.Equal(new int?[] { 1, 1 }, equations.AlignmentPointIndices);
        Assert.True(Assert.IsType<MathNode.Run>(
            Assert.IsType<MathNode.Row>(equations.Rows[0]).Children[1]).IsAlignmentPoint);
        Assert.True(Assert.IsType<MathNode.Run>(
            Assert.IsType<MathNode.Row>(equations.Rows[1]).Children[1]).IsAlignmentPoint);
    }

    [Fact]
    public void AlignmentPoints_RespectCtOnOffFalseForRunsAndBoxes()
    {
        var runNode = Parse(
            "<m:r><m:rPr><m:aln m:val=\"0\"/></m:rPr><m:t>x</m:t></m:r>");
        Assert.False(Assert.IsType<MathNode.Run>(runNode).IsAlignmentPoint);

        var boxNode = Parse(
            "<m:box><m:boxPr><m:aln m:val=\"off\"/></m:boxPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e></m:box>");
        Assert.False(Assert.IsType<MathNode.Box>(boxNode).IsAlignmentPoint);
    }

    [Theory]
    [InlineData("left", MathNode.MathParagraphJustification.Left)]
    [InlineData("right", MathNode.MathParagraphJustification.Right)]
    [InlineData("center", MathNode.MathParagraphJustification.Center)]
    [InlineData("centerGroup", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("center-group", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("CENTERGROUP", MathNode.MathParagraphJustification.CenterGroup)]
    public void OMathPara_DefaultJustification_InheritsDefJcWhenLocalJcIsAbsent(
        string val,
        MathNode.MathParagraphJustification expected)
    {
        var node = ParseParagraph(
            $"<m:mathPr><m:dispDef/><m:defJc m:val=\"{val}\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(expected, paragraph.Justification);
    }

    [Fact]
    public void OMathPara_BareDefJc_DefaultsToCenterGroup()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:dispDef/><m:defJc/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        Assert.Equal(
            MathNode.MathParagraphJustification.CenterGroup,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
    }

    [Fact]
    public void OMathPara_BareLocalJc_OverridesDefJcWithCenterGroup()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:dispDef/><m:defJc m:val=\"right\"/></m:mathPr>" +
            "<m:oMathParaPr><m:jc/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        Assert.Equal(
            MathNode.MathParagraphJustification.CenterGroup,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
    }

    [Fact]
    public void OMathPara_LocalJcOverridesInheritedDefJc()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:dispDef/><m:defJc m:val=\"right\"/></m:mathPr>" +
            "<m:oMathParaPr><m:jc m:val=\"left\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        Assert.Equal(
            MathNode.MathParagraphJustification.Left,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
    }

    [Fact]
    public void OMathPara_DocumentDefaultDefJc_IsInheritedWhenLocalJcIsAbsent()
    {
        var node = OmmlParser.Parse(
            $"<m:oMathPara xmlns:m=\"{M}\"><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
            "FALLBACK",
            new MathNode.MathProperties(
                DefaultJustification: MathNode.MathParagraphJustification.Right,
                DisplayDefaults: true));

        Assert.Equal(
            MathNode.MathParagraphJustification.Right,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
    }

    [Theory]
    [InlineData("", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("0", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("false", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("off", MathNode.MathParagraphJustification.CenterGroup)]
    [InlineData("1", MathNode.MathParagraphJustification.Right)]
    [InlineData("true", MathNode.MathParagraphJustification.Right)]
    [InlineData("on", MathNode.MathParagraphJustification.Right)]
    [InlineData("bogus", MathNode.MathParagraphJustification.Right)]
    public void OMathPara_DispDefControlsDefJcWithAbsentAndInvalidValues(
        string dispDefValue,
        MathNode.MathParagraphJustification expected)
    {
        var dispDef = string.IsNullOrEmpty(dispDefValue)
            ? string.Empty
            : $"<m:dispDef m:val=\"{dispDefValue}\"/>";
        var node = ParseParagraph(
            $"<m:mathPr>{dispDef}<m:defJc m:val=\"right\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        Assert.Equal(
            expected,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
    }

    [Fact]
    public void OMathPara_DispDefOverlaysIndependentlyFromInheritedDefJc()
    {
        var node = OmmlParser.Parse(
            $"<m:oMathPara xmlns:m=\"{M}\"><m:mathPr><m:dispDef/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
            "FALLBACK",
            new MathNode.MathProperties(
                DefaultJustification: MathNode.MathParagraphJustification.Right,
                DisplayDefaults: false));

        Assert.Equal(
            MathNode.MathParagraphJustification.Right,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
    }

    [Theory]
    [InlineData("before", MathNode.MathParagraphBinaryBreak.Before)]
    [InlineData("after", MathNode.MathParagraphBinaryBreak.After)]
    [InlineData("repeat", MathNode.MathParagraphBinaryBreak.Repeat)]
    [InlineData("bogus", MathNode.MathParagraphBinaryBreak.Before)]
    public void OMathPara_WithBinaryBreakPolicy_PreservesSharedMetadata(
        string val,
        MathNode.MathParagraphBinaryBreak expected)
    {
        var node = ParseParagraph(
            $"<m:oMathParaPr><m:brkBin m:val=\"{val}\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(expected, paragraph.BinaryBreak);
    }

    [Theory]
    [InlineData("--", MathNode.MathParagraphBinarySubtraction.MinusMinus)]
    [InlineData("+-", MathNode.MathParagraphBinarySubtraction.PlusMinus)]
    [InlineData("-+", MathNode.MathParagraphBinarySubtraction.MinusPlus)]
    [InlineData("bogus", MathNode.MathParagraphBinarySubtraction.MinusMinus)]
    public void OMathPara_WithBinarySubtractionPolicy_PreservesSharedMetadata(
        string val,
        MathNode.MathParagraphBinarySubtraction expected)
    {
        var node = ParseParagraph(
            $"<m:oMathParaPr><m:brkBinSub m:val=\"{val}\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(expected, paragraph.BinarySubtraction);
    }

    [Fact]
    public void OMathPara_WithStandardMathPropertiesContainer_ReadsBinaryBreakPolicies()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:brkBin m:val=\"repeat\"/><m:brkBinSub m:val=\"-+\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(MathNode.MathParagraphBinaryBreak.Repeat, paragraph.BinaryBreak);
        Assert.Equal(MathNode.MathParagraphBinarySubtraction.MinusPlus, paragraph.BinarySubtraction);
    }

    [Fact]
    public void OMathPara_WithMathFont_PreservesEquationWideFontMetadata()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal("Arial", paragraph.MathFontFamily);
    }

    [Fact]
    public void OMathPara_WithEmptyMathFont_UsesCallerFontFallback()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:mathFont m:val=\"  \"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Null(paragraph.MathFontFamily);
    }

    [Fact]
    public void OMathPara_InheritsGraphicMathPropertiesByProperty()
    {
        var node = ParseGraphicData(
            "<m:mathPr><m:brkBin m:val=\"repeat\"/><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
            "<a14:m><m:mathPr><m:mathFont m:val=\"Calibri\"/></m:mathPr><m:oMathPara>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara></a14:m>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(MathNode.MathParagraphBinaryBreak.Repeat, paragraph.BinaryBreak);
        Assert.Equal("Calibri", paragraph.MathFontFamily);
    }

    [Fact]
    public void InlineOMath_InheritsGraphicMathFontIntoSharedRoot()
    {
        var node = ParseGraphicData(
            "<m:mathPr><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
            "<a14:m><m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></a14:m>");

        var root = Assert.IsType<MathNode.MathRoot>(node);
        Assert.Equal("Arial", root.Properties.MathFontFamily);
        Assert.IsType<MathNode.Run>(root.Content);
    }

    [Fact]
    public void OMathPara_MarginsUseDocumentOverlayAndLocalValuesWin()
    {
        var node = OmmlParser.Parse(
            $"<m:oMathPara xmlns:m=\"{M}\"><m:mathPr>" +
            "<m:dispDef/><m:lMargin m:val=\"1440\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
            "FALLBACK",
            new MathNode.MathProperties(
                DisplayDefaults: true,
                LeftMarginTwips: 720,
                RightMarginTwips: 360));

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(1440, paragraph.LeftMarginTwips);
        Assert.Equal(360, paragraph.RightMarginTwips);
    }

    [Theory]
    [InlineData("<m:dispDef/>", "<m:lMargin m:val=\"720\"/><m:rMargin m:val=\"360\"/>", 720, 360)]
    [InlineData("<m:dispDef/>", "<m:lMargin/><m:rMargin/>", 1440, 1440)]
    [InlineData("<m:dispDef/>", "<m:lMargin m:val=\"0\"/><m:rMargin m:val=\"0\"/>", 0, 0)]
    [InlineData("<m:dispDef m:val=\"off\"/>", "<m:lMargin m:val=\"720\"/><m:rMargin m:val=\"360\"/>", null, null)]
    [InlineData("", "<m:lMargin m:val=\"720\"/><m:rMargin m:val=\"360\"/>", null, null)]
    public void OMathPara_MarginsHandleExplicitValuelessZeroAndDispDefGate(
        string displayDefaults,
        string values,
        int? expectedLeft,
        int? expectedRight)
    {
        var node = ParseParagraph(
            $"<m:mathPr>{displayDefaults}{values}</m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(expectedLeft, paragraph.LeftMarginTwips);
        Assert.Equal(expectedRight, paragraph.RightMarginTwips);
    }

    [Fact]
    public void OMathPara_InvalidMarginUsesNoMarginFallback()
    {
        var node = ParseParagraph(
            "<m:mathPr><m:dispDef/><m:lMargin m:val=\"bogus\"/><m:rMargin m:val=\"-1\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(0, paragraph.LeftMarginTwips);
        Assert.Equal(0, paragraph.RightMarginTwips);
    }

    [Theory]
    [InlineData("<m:dispDef/>", "", 1440, false)]
    [InlineData("<m:dispDef/>", "<m:wrapIndent/>", 1440, false)]
    [InlineData("<m:dispDef/>", "<m:wrapIndent m:val=\"720\"/>", 720, false)]
    [InlineData("<m:dispDef/>", "<m:wrapIndent m:val=\"bogus\"/>", 0, false)]
    [InlineData("<m:dispDef/>", "<m:wrapRight/>", 1440, true)]
    [InlineData("<m:dispDef/>", "<m:wrapRight m:val=\"false\"/>", 1440, false)]
    [InlineData("<m:dispDef/>", "<m:wrapRight m:val=\"bogus\"/>", 1440, true)]
    [InlineData("<m:dispDef m:val=\"off\"/>", "<m:wrapIndent m:val=\"720\"/><m:wrapRight/>", 0, false)]
    [InlineData("", "<m:wrapIndent m:val=\"720\"/><m:wrapRight/>", 0, false)]
    public void OMathPara_WrapPropertiesUseAuthorityDefaultsAndDispDefGate(
        string displayDefaults,
        string values,
        int expectedIndent,
        bool expectedRight)
    {
        var node = ParseParagraph(
            $"<m:mathPr>{displayDefaults}{values}</m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(expectedIndent, paragraph.WrapIndentTwips);
        Assert.Equal(expectedRight, paragraph.WrapRight);
    }

    [Fact]
    public void OMathPara_WrapPropertiesOverlayDocumentDefaultsPropertyByProperty()
    {
        var node = OmmlParser.Parse(
            $"<m:oMathPara xmlns:m=\"{M}\"><m:mathPr><m:dispDef/>" +
            "<m:wrapRight/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara>",
            "FALLBACK",
            new MathNode.MathProperties(
                DisplayDefaults: true,
                WrapIndentTwips: 720,
                WrapRight: false));

        var paragraph = Assert.IsType<MathNode.MathParagraph>(node);
        Assert.Equal(720, paragraph.WrapIndentTwips);
        Assert.True(paragraph.WrapRight);
    }
}
