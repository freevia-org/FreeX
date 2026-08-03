using FreeP.App.Compositor.MathLayout;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Layout/render bug-fix tests for <see cref="MathLayoutEngine"/> (Theme 27 math
/// typesetting): n-ary operand clipping (HB1/HB2) and superscript
/// under-reported ascent (HB3). See MathBoxRenderPlannerBaselineTests for the
/// HB4 (text/math baseline alignment) coverage in the renderer layer.
/// </summary>
public sealed class MathLayoutEngineTests
{
    private const double FontSizePt = 18.0;
    private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static MathNode Run(
        string text,
        bool isItalic = true,
        bool isBold = false,
        MathNode.MathAlphabet alphabet = MathNode.MathAlphabet.Default) =>
        new MathNode.Run(text, isItalic, isBold, alphabet);

    private static MathNode ParseOmml(string oMathInner)
    {
        var xml = $"<m:oMath xmlns:m=\"{M}\">{oMathInner}</m:oMath>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

    private static MathNode ParseOmmlParagraph(string oMathParaInner)
    {
        var xml = $"<m:oMathPara xmlns:m=\"{M}\">{oMathParaInner}</m:oMathPara>";
        return OmmlParser.Parse(xml, fallbackText: "FALLBACK");
    }

    private static MathNode TallFraction() =>
        new MathNode.Frac(Run("1"), Run("x"));

    private static double GetEqArrayMarkerX(MathBox.Container eqArray, int rowIndex, int childIndex)
    {
        var row = (MathBox.Container)eqArray.Children[rowIndex];
        return row.X + row.Children[childIndex].X;
    }

    // ── HB1: n-ary integral (subSup / scripts-to-the-side) style ────────────

    [Fact]
    public void Nary_Integral_WithTallOperand_ContainerFullyContainsOperand()
    {
        // ? (1/x)  — integral (LimitsAboveBelow = false ⇒ scripts-to-the-side branch)
        var nary = new MathNode.Nary(
            operatorChar: "?",
            limitsAboveBelow: false,
            subLimit: Run("0"),
            supLimit: Run("1"),
            operand: TallFraction());

        var box = MathLayoutEngine.Layout(nary, "Cambria Math", FontSizePt);

        // Recompute the operand's absolute top/bottom the same way the engine positions it,
        // by walking the (single) child container to find the operand box's Y among children.
        var naryContainer = (MathBox.Container)box.Children[0];
        var operandBox = naryContainer.Children[^1]; // operand is always added last

        double operandTop = operandBox.Y;
        double operandBottom = operandBox.Y + operandBox.Metrics.Height;

        operandTop.Should().BeGreaterThanOrEqualTo(0,
            "no child should be positioned above the container's top after HB1's shift-down fix");
        operandBottom.Should().BeLessThanOrEqualTo(naryContainer.Metrics.Height + 0.01,
            "the tall fraction operand must be fully contained within the n-ary box (not clipped)");
    }

    [Fact]
    public void Nary_Integral_WithTallOperand_TotalHeightExceedsOperatorAlone()
    {
        var shortOperand = new MathNode.Nary("?", false, Run("0"), Run("1"), Run("x"));
        var tallOperand = new MathNode.Nary("?", false, Run("0"), Run("1"), TallFraction());

        var shortBox = MathLayoutEngine.Layout(shortOperand, "Cambria Math", FontSizePt);
        var tallBox  = MathLayoutEngine.Layout(tallOperand,  "Cambria Math", FontSizePt);

        tallBox.Metrics.Height.Should().BeGreaterThan(shortBox.Metrics.Height,
            "a tall (fraction) operand must grow the n-ary box's total height (HB1), not be clipped to the operator/limit-script height");
    }

    // ── HB2: n-ary sum/product (undOvr / limits above-below) style ──────────

    [Fact]
    public void Nary_Sum_WithTallOperand_ContainerFullyContainsOperand()
    {
        // ? (1/x), no sub limit (subH = 0) — the case called out in HB2 as the
        // clearest overflow: operand-bottom = opBaseline + operandDescent > totalH.
        var nary = new MathNode.Nary(
            operatorChar: "?",
            limitsAboveBelow: true,
            subLimit: null,
            supLimit: Run("n"),
            operand: TallFraction());

        var box = MathLayoutEngine.Layout(nary, "Cambria Math", FontSizePt);

        var naryContainer = (MathBox.Container)box.Children[0];
        var operandBox = naryContainer.Children[^1];

        double operandTop = operandBox.Y;
        double operandBottom = operandBox.Y + operandBox.Metrics.Height;

        operandTop.Should().BeGreaterThanOrEqualTo(0,
            "no child should be positioned above the container's top after HB2's shift-down fix");
        operandBottom.Should().BeLessThanOrEqualTo(naryContainer.Metrics.Height + 0.01,
            "the tall fraction operand (with no sub limit) must be fully contained (not clipped below)");
    }

    [Fact]
    public void Nary_Sum_WithTallOperand_TotalHeightExceedsStackAlone()
    {
        var shortOperand = new MathNode.Nary("?", true, null, Run("n"), Run("x"));
        var tallOperand = new MathNode.Nary("?", true, null, Run("n"), TallFraction());

        var shortBox = MathLayoutEngine.Layout(shortOperand, "Cambria Math", FontSizePt);
        var tallBox  = MathLayoutEngine.Layout(tallOperand,  "Cambria Math", FontSizePt);

        tallBox.Metrics.Height.Should().BeGreaterThan(shortBox.Metrics.Height,
            "a tall (fraction) operand with no sub limit must grow totalH (HB2), not overflow past it");
    }

    [Fact]
    public void Nary_Sum_Descent_CoversOperandBottomBelowBaseline()
    {
        var nary = new MathNode.Nary("?", true, null, Run("n"), TallFraction());
        var box = MathLayoutEngine.Layout(nary, "Cambria Math", FontSizePt);

        // Descent = Height - Ascent must reach at least to the operand's bottom
        // measured from the reported baseline.
        var naryContainer = (MathBox.Container)box.Children[0];
        var operandBox = naryContainer.Children[^1];
        double operandBottomFromContainerTop = operandBox.Y + operandBox.Metrics.Height;
        double reportedBottom = box.Metrics.Ascent + box.Metrics.Descent;

        reportedBottom.Should().BeGreaterThanOrEqualTo(operandBottomFromContainerTop - 0.01);
    }

    // ── HB3: superscript raised above a normal-size base ────────────────────

    [Fact]
    public void Nary_HiddenLimits_DoNotEmitSharedGlyphDrawOps()
    {
        var node = ParseOmml(
            "<m:nary>" +
            "<m:naryPr><m:chr m:val=\"S\"/><m:limLoc m:val=\"undOvr\"/><m:subHide/><m:supHide/></m:naryPr>" +
            "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:nary>");
        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var ops = MathBoxRenderPlanner.Plan(box, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "S", "x" },
                "hidden m:nary limits must not flow into the renderer-neutral draw plan");
    }

    [Fact]
    public void Rad_WithBareDegHide_DoesNotEmitDegreeGlyph()
    {
        var node = ParseOmml(
            "<m:rad>" +
            "<m:radPr><m:degHide/></m:radPr>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");
        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var ops = MathBoxRenderPlanner.Plan(box, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "x" },
                "m:radPr/m:degHide is a CT_OnOff flag, so a bare element hides the degree before renderers consume the plan");
        ops.OfType<MathDrawOp.DrawRadical>().Should().ContainSingle();
    }

    [Fact]
    public void Rad_WithVisibleDegree_PositionsDegreeBeforeRadicalAndAboveRadicandBaseline()
    {
        var node = ParseOmml(
            "<m:rad>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");
        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = Assert.IsType<MathBox.Container>(box.Children[0]);
        container.Children.Should().HaveCount(3, "visible radical degree adds a degree box alongside radical and radicand");
        var radical = Assert.IsType<MathBox.Radical>(container.Children[0]);
        var radicand = Assert.IsType<MathBox.Glyph>(container.Children[1]);
        var degree = Assert.IsType<MathBox.Glyph>(container.Children[2]);

        degree.Text.Should().Be("3");
        radicand.Text.Should().Be("x");
        degree.FontSizePt.Should().BeApproximately(FontSizePt * 0.65, 0.01,
            "the radical degree is laid out as a script-sized shared glyph");
        degree.X.Should().BeLessThan(radical.X,
            "the visible degree must sit to the left of the radical sign in shared layout");
        radical.X.Should().BeGreaterThanOrEqualTo(degree.X + degree.Metrics.Width,
            "the radical sign should reserve the degree width instead of overlapping it");
        radicand.X.Should().BeGreaterThan(radical.X,
            "the radicand must start after the radical check-mark");
        degree.Y.Should().BeLessThan(radicand.Y,
            "the visible degree must be placed above the radicand top before any renderer draws it");
        (degree.Y + degree.Metrics.Ascent).Should().BeLessThan(radicand.Y + radicand.Metrics.Ascent,
            "the degree baseline should remain above the radicand baseline");
        box.Metrics.Ascent.Should().BeApproximately(radicand.Y + radicand.Metrics.Ascent, 0.01,
            "the radical baseline is still governed by the radicand, not the degree index");

        var ops = MathBoxRenderPlanner.Plan(box, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "x", "3" },
                "visible degree and radicand glyphs must both reach the renderer-neutral plan");
        var radicalOp = ops.OfType<MathDrawOp.DrawRadical>().Single();
        var radicandOp = ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "x");
        var degreeOp = ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "3");
        degreeOp.X.Should().BeLessThan(radicalOp.X,
            "renderers consume the shared degree position instead of recomputing it");
        degreeOp.Y.Should().BeLessThan(radicandOp.Y,
            "renderers consume a visible degree that is already above the radicand");
    }

    [Fact]
    public void Rad_WithHiddenDegree_DoesNotReserveDegreeWidthOrEmitDegreeGlyph()
    {
        var visible = ParseOmml(
            "<m:rad>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");
        var hidden = ParseOmml(
            "<m:rad>" +
            "<m:radPr><m:degHide/></m:radPr>" +
            "<m:deg><m:r><m:t>3</m:t></m:r></m:deg>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:rad>");

        var visibleBox = MathLayoutEngine.Layout(visible, "Cambria Math", FontSizePt);
        var hiddenBox = MathLayoutEngine.Layout(hidden, "Cambria Math", FontSizePt);
        var hiddenContainer = Assert.IsType<MathBox.Container>(hiddenBox.Children[0]);

        hiddenContainer.Children.Should().HaveCount(2,
            "m:radPr/m:degHide removes the degree box from the shared MathBox tree");
        hiddenBox.Metrics.Width.Should().BeLessThan(visibleBox.Metrics.Width,
            "hidden degree radicals should not keep a ghost degree gutter");

        var ops = MathBoxRenderPlanner.Plan(hiddenBox, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "x" },
            "hidden radical degrees must not reach WPF or Avalonia draw operations");
        ops.OfType<MathDrawOp.DrawRadical>().Should().ContainSingle();
    }

    [Fact]
    public void Nary_GrowOperator_WithTallOperand_IncreasesSharedOperatorSize()
    {
        var noGrow = new MathNode.Nary("S", false, null, null, TallFraction());
        var grow = new MathNode.Nary("S", false, null, null, TallFraction(), growOperator: true);

        var noGrowLayout = MathLayoutEngine.Layout(noGrow, "Cambria Math", FontSizePt);
        var growLayout = MathLayoutEngine.Layout(grow, "Cambria Math", FontSizePt);

        var noGrowContainer = Assert.IsType<MathBox.Container>(noGrowLayout.Children[0]);
        var growContainer = Assert.IsType<MathBox.Container>(growLayout.Children[0]);
        var noGrowOperator = Assert.IsType<MathBox.Glyph>(noGrowContainer.Children[0]);
        var growOperator = Assert.IsType<MathBox.Glyph>(growContainer.Children[0]);

        growOperator.FontSizePt.Should().BeGreaterThan(noGrowOperator.FontSizePt,
            "m:naryPr/m:grow should vertically scale the n-ary operator for tall operands in shared layout");
        growOperator.Metrics.Height.Should().BeGreaterThan(noGrowOperator.Metrics.Height);
        growLayout.Metrics.Height.Should().BeGreaterThan(noGrowLayout.Metrics.Height);
    }

    [Fact]
    public void OmmlNaryGrow_RenderPlanCarriesScaledOperatorGlyph()
    {
        var node = ParseOmml(
            "<m:nary>" +
            "<m:naryPr><m:chr m:val=\"S\"/><m:grow/></m:naryPr>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
            "</m:nary>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single(g => g.Text == "S");

        op.FontSizePt.Should().BeGreaterThan(FontSizePt * 1.50,
            "the renderer-neutral draw plan should carry the grown n-ary operator size to both hosts");
    }

    [Fact]
    public void OmmlNaryGrow_WithHiddenLimits_ScalesOperatorWithoutLimitGlyphs()
    {
        var node = ParseOmml(
            "<m:nary>" +
            "<m:naryPr><m:chr m:val=\"S\"/><m:grow/><m:subHide/><m:supHide/></m:naryPr>" +
            "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
            "</m:nary>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToArray();

        glyphs.Select(g => g.Text)
            .Should()
            .Equal(new[] { "S", "1", "x" },
                "hidden n-ary sub/sup limits must be removed while the grow operator and operand remain in the shared plan");
        glyphs.Single(g => g.Text == "S").FontSizePt.Should().BeGreaterThan(FontSizePt * 1.50,
            "m:naryPr/m:grow should still scale the operator after hidden limits are suppressed");
    }

    [Fact]
    public void OmmlNaryLimLoc_RenderPlanDistinguishesUnderOverFromSubSup()
    {
        var underOver = MathLayoutEngine.Layout(
            ParseOmml(
                "<m:nary>" +
                "<m:naryPr><m:chr m:val=\"S\"/><m:limLoc m:val=\"undOvr\"/></m:naryPr>" +
                "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
                "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
                "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                "</m:nary>"),
            "Cambria Math",
            FontSizePt);
        var subSup = MathLayoutEngine.Layout(
            ParseOmml(
                "<m:nary>" +
                "<m:naryPr><m:chr m:val=\"S\"/><m:limLoc m:val=\"subSup\"/></m:naryPr>" +
                "<m:sub><m:r><m:t>0</m:t></m:r></m:sub>" +
                "<m:sup><m:r><m:t>n</m:t></m:r></m:sup>" +
                "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
                "</m:nary>"),
            "Cambria Math",
            FontSizePt);

        MathBoxRenderPlanner.Plan(underOver, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Select(g => g.Text)
            .Should()
            .Equal(new[] { "n", "S", "0", "x" },
                "m:naryPr/m:limLoc=undOvr should place the upper limit above and lower limit below the operator before the operand");
        MathBoxRenderPlanner.Plan(subSup, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Select(g => g.Text)
            .Should()
            .Equal(new[] { "S", "n", "0", "x" },
                "m:naryPr/m:limLoc=subSup should keep limits in the side script stack before the operand");

        var underOverGlyphs = MathBoxRenderPlanner.Plan(underOver, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToDictionary(g => g.Text);
        var subSupGlyphs = MathBoxRenderPlanner.Plan(subSup, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToDictionary(g => g.Text);

        underOverGlyphs["n"].Y.Should().BeLessThan(underOverGlyphs["S"].Y);
        underOverGlyphs["0"].Y.Should().BeGreaterThan(underOverGlyphs["S"].Y);
        subSupGlyphs["n"].X.Should().BeGreaterThan(subSupGlyphs["S"].X);
        subSupGlyphs["0"].X.Should().BeGreaterThan(subSupGlyphs["S"].X);
        subSupGlyphs["n"].X.Should().BeLessThan(subSupGlyphs["x"].X);
        subSupGlyphs["0"].X.Should().BeLessThan(subSupGlyphs["x"].X);
    }

    [Fact]
    public void Run_WithBoldStyle_LayoutAndRenderPlanCarryBoldMetadata()
    {
        var layout = MathLayoutEngine.Layout(Run("x", isItalic: false, isBold: true), "Cambria Math", FontSizePt);

        var glyph = Assert.IsType<MathBox.Glyph>(layout.Children[0]);
        glyph.IsItalic.Should().BeFalse();
        glyph.IsBold.Should().BeTrue();

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();
        op.Text.Should().Be("x");
        op.IsItalic.Should().BeFalse();
        op.IsBold.Should().BeTrue();
    }

    [Fact]
    public void OmmlStyBoldItalic_RenderPlanCarriesItalicAndBold()
    {
        var node = ParseOmml("<m:r><m:rPr><m:sty m:val=\"bi\"/></m:rPr><m:t>x</m:t></m:r>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be("x");
        op.IsItalic.Should().BeTrue();
        op.IsBold.Should().BeTrue();
    }

    [Fact]
    public void OmmlRunWithMultipleTextChildren_RenderPlanCarriesFullRunText()
    {
        var node = ParseOmml("<m:r><m:t>sin</m:t><m:t>^2</m:t><m:t>x</m:t></m:r>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be("sin^2x",
            "PowerPoint-authored OMML can split one math run across multiple m:t nodes, and both hosts consume this shared draw plan");
    }

    [Fact]
    public void OmmlLiteralRun_RenderPlanCarriesUprightLiteralGlyph()
    {
        var node = ParseOmml("<m:r><m:rPr><m:lit/></m:rPr><m:t>x</m:t></m:r>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be("x");
        op.IsItalic.Should().BeFalse(
            "m:rPr/m:lit marks the run as literal math text before WPF or Avalonia consumes the shared draw plan");
        op.IsBold.Should().BeFalse();
    }

    [Theory]
    [InlineData(MathNode.MathAlphabet.Script, "\U0001D49C\U0001D4B6-1")]
    [InlineData(MathNode.MathAlphabet.Fraktur, "\U0001D504\U0001D51E-1")]
    [InlineData(MathNode.MathAlphabet.DoubleStruck, "\U0001D538\U0001D552-\U0001D7D9")]
    [InlineData(MathNode.MathAlphabet.SansSerif, "\U0001D5A0\U0001D5BA-\U0001D7E3")]
    [InlineData(MathNode.MathAlphabet.Monospace, "\U0001D670\U0001D68A-\U0001D7F7")]
    public void Run_WithMathAlphabet_MapsAsciiGlyphsInSharedDrawPlan(MathNode.MathAlphabet alphabet, string expectedText)
    {
        var layout = MathLayoutEngine.Layout(
            Run("Aa-1", isItalic: false, isBold: false, alphabet),
            "Cambria Math",
            FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be(expectedText);
        op.IsItalic.Should().BeFalse("explicit mathematical alphabet glyphs replace renderer font-style policy");
        op.IsBold.Should().BeFalse("explicit mathematical alphabet glyphs replace renderer font-weight policy");
    }

    [Theory]
    [InlineData("<m:scr m:val=\"script\"/><m:sty m:val=\"b\"/>", "\U0001D4D0\U0001D4EA1")]
    [InlineData("<m:scr m:val=\"fraktur\"/><m:sty m:val=\"b\"/>", "\U0001D56C\U0001D5861")]
    [InlineData("<m:scr m:val=\"sans-serif\"/><m:sty m:val=\"i\"/>", "\U0001D608\U0001D6221")]
    [InlineData("<m:scr m:val=\"sans-serif\"/><m:sty m:val=\"b\"/>", "\U0001D5D4\U0001D5EE\U0001D7ED")]
    [InlineData("<m:scr m:val=\"sans-serif\"/><m:sty m:val=\"bi\"/>", "\U0001D63C\U0001D6561")]
    public void OmmlScrWithStyVariant_RenderPlanUsesStyledUnicodeMathGlyphs(string rPrInner, string expectedText)
    {
        var node = ParseOmml($"<m:r><m:rPr>{rPrInner}</m:rPr><m:t>Aa1</m:t></m:r>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be(expectedText);
        op.IsItalic.Should().BeFalse("styled mathematical alphabet glyphs carry style in Unicode, not renderer metadata");
        op.IsBold.Should().BeFalse("styled mathematical alphabet glyphs carry weight in Unicode, not renderer metadata");
    }

    [Fact]
    public void OmmlScrDoubleStruck_RenderPlanUsesUnicodeMathGlyphs()
    {
        var node = ParseOmml("<m:r><m:rPr><m:scr m:val=\"double-struck\"/><m:sty m:val=\"bi\"/></m:rPr><m:t>NZ9?</m:t></m:r>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be("\u2115\u2124\U0001D7E1?");
        op.IsItalic.Should().BeFalse();
        op.IsBold.Should().BeFalse();
    }

    [Fact]
    public void Run_WithRomanAlphabet_PreservesExistingItalicAndBoldBehavior()
    {
        var layout = MathLayoutEngine.Layout(
            Run("x1", isItalic: true, isBold: true, MathNode.MathAlphabet.Roman),
            "Cambria Math",
            FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be("x1");
        op.IsItalic.Should().BeTrue();
        op.IsBold.Should().BeTrue();
    }

    [Fact]
    public void ArgumentSizeMinusOne_ScalesArgumentGlyphInSharedDrawPlan()
    {
        var node = ParseOmml(
            "<m:box>" +
            "<m:e><m:argPr><m:argSz m:val=\"-1\"/></m:argPr><m:r><m:t>abc</m:t></m:r></m:e>" +
            "</m:box>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var op = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        op.Text.Should().Be("abc");
        op.FontSizePt.Should().BeApproximately(FontSizePt * 0.70, 0.001,
            "m:argPr/m:argSz=-1 should reduce the argument by one shared script-size level before either host draws it");
    }

    [Fact]
    public void SuperscriptArgumentSizePlusOne_RestoresScriptGlyphTowardTextSize()
    {
        var node = ParseOmml(
            "<m:sSup>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sup><m:argPr><m:argSz m:val=\"1\"/></m:argPr><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSup>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "x", "2" });
        glyphs.Single(g => g.Text == "2").FontSizePt.Should().BeApproximately(FontSizePt, 0.001,
            "a +1 argument-size request inside the superscript should offset the default script shrink in shared layout");
    }

    [Fact]
    public void Sup_OnNormalBase_ContainerAscentGrowsToContainRaisedScript()
    {
        // sup on 'x': ideal scriptY = baseAscent - 0.40em - scriptAscent is
        // negative for a normal base, so the container must grow its Ascent
        // rather than clamp scriptY to 0 (which would under-report ascent).
        var sup = new MathNode.Sup(Run("x"), Run("2"));
        var box = MathLayoutEngine.Layout(sup, "Cambria Math", FontSizePt);

        var supContainer = (MathBox.Container)box.Children[0];
        var baseBox = supContainer.Children[0];
        var scriptBox = supContainer.Children[1];

        box.Metrics.Ascent.Should().BeGreaterThan(baseBox.Metrics.Ascent,
            "the container's ascent must grow to cover the raised superscript, not stay equal to the base's ascent");

        scriptBox.Y.Should().BeGreaterThanOrEqualTo(0,
            "the script box itself must never be positioned above the container top");

        // The script's top, from the container's own coordinate frame, should sit
        // strictly above the base's top (visually "raised"), not at/below it.
        scriptBox.Y.Should().BeLessThan(baseBox.Y + baseBox.Metrics.Height,
            "the superscript must overlap/rise above the base, not be pushed below its top");
    }

    [Fact]
    public void SubSup_OnNormalBase_ContainerAscentGrowsToContainRaisedSup()
    {
        var subSup = new MathNode.SubSup(Run("x"), Run("i"), Run("2"));
        var box = MathLayoutEngine.Layout(subSup, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var baseBox = container.Children[0];
        var supBox  = container.Children[1];

        box.Metrics.Ascent.Should().BeGreaterThan(baseBox.Metrics.Ascent,
            "SubSup must also grow its container ascent when the sup rises above a normal base");

        supBox.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void SubSup_WithAlignScripts_RightAlignsSharedScriptColumn()
    {
        var aligned = new MathNode.SubSup(Run("x"), Run("wide"), Run("2"), alignScripts: true);
        var unaligned = new MathNode.SubSup(Run("x"), Run("wide"), Run("2"));

        var alignedBox = MathLayoutEngine.Layout(aligned, "Cambria Math", FontSizePt);
        var unalignedBox = MathLayoutEngine.Layout(unaligned, "Cambria Math", FontSizePt);

        var alignedContainer = Assert.IsType<MathBox.Container>(alignedBox.Children[0]);
        var unalignedContainer = Assert.IsType<MathBox.Container>(unalignedBox.Children[0]);
        var alignedSup = alignedContainer.Children[1];
        var alignedSub = alignedContainer.Children[2];
        var unalignedSup = unalignedContainer.Children[1];

        alignedSup.X.Should().BeGreaterThan(unalignedSup.X,
            "m:sSubSupPr/m:alnScr should right-align the shorter script within the shared script column");
        (alignedSup.X + alignedSup.Metrics.Width).Should().BeApproximately(
            alignedSub.X + alignedSub.Metrics.Width,
            0.01,
            "aligned sub/sup script right edges should match in shared layout");
        alignedBox.Metrics.Width.Should().BeApproximately(unalignedBox.Metrics.Width, 0.01,
            "script alignment should reposition glyphs inside the same shared script column");
    }

    [Fact]
    public void OmmlSubSupAlignScripts_RenderPlanCarriesRightAlignedScriptGlyphs()
    {
        var node = ParseOmml(
            "<m:sSubSup>" +
            "<m:sSubSupPr><m:alnScr/></m:sSubSupPr>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "<m:sub><m:r><m:t>wide</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sSubSup>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "x", "2", "wide" });
        var sup = glyphs.Single(g => g.Text == "2");
        var sub = glyphs.Single(g => g.Text == "wide");
        sup.X.Should().BeGreaterThan(glyphs.Single(g => g.Text == "x").X,
            "the aligned script still sits to the right of the base expression");
        sup.X.Should().BeGreaterThan(sub.X,
            "right-edge alignment should shift the shorter superscript right within the shared script column");
    }

    [Fact]
    public void PreSubSup_PlacesScriptStackLeftOfBase_WithSupAboveSub()
    {
        var pre = new MathNode.PreSubSup(Run("x"), Run("i"), Run("2"));
        var box = MathLayoutEngine.Layout(pre, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var supBox = container.Children[0];
        var subBox = container.Children[1];
        var baseBox = container.Children[2];

        (supBox.X + supBox.Metrics.Width).Should().BeLessThanOrEqualTo(baseBox.X,
            "the pre-superscript must be laid out to the left of the base");
        (subBox.X + subBox.Metrics.Width).Should().BeLessThanOrEqualTo(baseBox.X,
            "the pre-subscript must be laid out to the left of the base");
        supBox.Y.Should().BeLessThan(subBox.Y,
            "the pre-superscript must sit above the pre-subscript in the left-side stack");
        (baseBox.Y + baseBox.Metrics.Ascent).Should().BeApproximately(box.Metrics.Ascent, 0.01,
            "the base baseline should remain the reported baseline for the prescript expression");
    }

    [Fact]
    public void PreSubSup_UsesReducedScriptFontSize()
    {
        var pre = new MathNode.PreSubSup(Run("x"), Run("i"), Run("2"));
        var box = MathLayoutEngine.Layout(pre, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var supGlyph = Assert.IsType<MathBox.Glyph>(container.Children[0]);
        var subGlyph = Assert.IsType<MathBox.Glyph>(container.Children[1]);
        var baseGlyph = Assert.IsType<MathBox.Glyph>(container.Children[2]);

        supGlyph.FontSizePt.Should().BeApproximately(FontSizePt * 0.70, 0.01);
        subGlyph.FontSizePt.Should().BeApproximately(FontSizePt * 0.70, 0.01);
        baseGlyph.FontSizePt.Should().Be(FontSizePt);
    }

    [Fact]
    public void PreSubSup_ContainsTallBaseAndTallScriptsWithoutClipping()
    {
        var pre = new MathNode.PreSubSup(TallFraction(), TallFraction(), TallFraction());
        var box = MathLayoutEngine.Layout(pre, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        foreach (var child in container.Children)
        {
            child.Y.Should().BeGreaterThanOrEqualTo(0,
                "prescript layout must shift upward extents into the container instead of clipping above");
            (child.Y + child.Metrics.Height).Should().BeLessThanOrEqualTo(container.Metrics.Height + 0.01,
                "prescript layout metrics must include the full base and script extents");
        }

        box.Metrics.Ascent.Should().BeGreaterThan(0);
        box.Metrics.Descent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OmmlPreSubSup_RenderPlanCarriesLeftScriptStackBeforeBase()
    {
        var node = ParseOmml(
            "<m:sPre>" +
            "<m:e><m:r><m:t>X</m:t></m:r></m:e>" +
            "<m:sub><m:r><m:t>wide</m:t></m:r></m:sub>" +
            "<m:sup><m:r><m:t>2</m:t></m:r></m:sup>" +
            "</m:sPre>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "2", "wide", "X" },
            "m:sPre should emit the pre-superscript, pre-subscript, and base from the shared draw plan before either host draws it");

        var sup = glyphs.Single(g => g.Text == "2");
        var sub = glyphs.Single(g => g.Text == "wide");
        var baseGlyph = glyphs.Single(g => g.Text == "X");

        sup.X.Should().BeLessThan(baseGlyph.X, "pre-superscript glyphs are laid out to the left of the base");
        sub.X.Should().BeLessThan(baseGlyph.X, "pre-subscript glyphs are laid out to the left of the base");
        sup.Y.Should().BeLessThan(sub.Y, "the pre-superscript sits above the pre-subscript in the shared script stack");
        sup.X.Should().BeGreaterThan(sub.X,
            "the narrower pre-superscript is right-aligned within the shared pre-script column");
        sup.FontSizePt.Should().BeLessThan(baseGlyph.FontSizePt);
        sub.FontSizePt.Should().BeLessThan(baseGlyph.FontSizePt);
    }

    [Fact]
    public void Sub_OnNormalBase_DescentGrowsForDeepSubscript_AscentUnchanged()
    {
        var sub = new MathNode.Sub(Run("x"), Run("i"));
        var box = MathLayoutEngine.Layout(sub, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var baseBox = container.Children[0];

        // A subscript never raises the baseline — only the descent (bottom
        // extent) should grow to contain a deep script.
        box.Metrics.Ascent.Should().Be(baseBox.Metrics.Ascent);
        box.Metrics.Height.Should().BeGreaterThanOrEqualTo(baseBox.Metrics.Height);
    }

    [Fact]
    public void Sup_Baseline_StaysConsistent_BaseDrawnAtDeficitOffset()
    {
        // The base box's own Y must equal the ascent deficit (so that
        // baseTop + baseAscent == container.Ascent, i.e. baseline preserved).
        var sup = new MathNode.Sup(Run("x"), Run("2"));
        var box = MathLayoutEngine.Layout(sup, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var baseBox = container.Children[0];

        (baseBox.Y + baseBox.Metrics.Ascent).Should().BeApproximately(box.Metrics.Ascent, 0.01,
            "the base's own baseline (Y + its ascent) must line up with the container's reported Ascent");
    }

    // ── HA4: m:d sepChr — separator glyph between multiple delimiter elements ──

    private static IEnumerable<MathBox> AllGlyphs(MathBox box)
    {
        if (box is MathBox.Glyph g) yield return g;
        if (box is MathBox.Container c)
            foreach (var child in c.Children)
                foreach (var g2 in AllGlyphs(child))
                    yield return g2;
    }

    private static IEnumerable<MathBox.Bracket> AllBrackets(MathBox box)
    {
        if (box is MathBox.Bracket b)
            yield return b;
        if (box is MathBox.Container c)
            foreach (var child in c.Children)
                foreach (var b2 in AllBrackets(child))
                    yield return b2;
    }

    private static IEnumerable<MathBox.HRule> AllHRules(MathBox box)
    {
        if (box is MathBox.HRule h)
            yield return h;
        if (box is MathBox.Container c)
            foreach (var child in c.Children)
                foreach (var h2 in AllHRules(child))
                    yield return h2;
    }

    [Fact]
    public void Acc_WithExplicitAccent_PlacesAccentAboveBaseOnSharedLayout()
    {
        var acc = new MathNode.Acc("~", Run("x"));
        var box = MathLayoutEngine.Layout(acc, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];

        var accentGlyph = container.Children.OfType<MathBox.Glyph>().Single(g => g.Text == "~");
        var baseGlyph = AllGlyphs(container).Cast<MathBox.Glyph>().Single(g => g.Text == "x");

        accentGlyph.Y.Should().BeLessThan(baseGlyph.Y);
        box.Metrics.Ascent.Should().BeGreaterThan(baseGlyph.Metrics.Ascent,
            "accented math must report the added accent height before WPF/Avalonia consume the shared box");
    }

    [Fact]
    public void Bar_OverlineAndUnderline_PositionHRuleAroundBase()
    {
        var over = MathLayoutEngine.Layout(new MathNode.Bar(Run("x")), "Cambria Math", FontSizePt);
        var under = MathLayoutEngine.Layout(new MathNode.Bar(Run("x"), isOver: false), "Cambria Math", FontSizePt);

        var overContainer = (MathBox.Container)over.Children[0];
        var underContainer = (MathBox.Container)under.Children[0];
        var overRule = AllHRules(overContainer).Single();
        var underRule = AllHRules(underContainer).Single();
        var overBase = AllGlyphs(overContainer).Cast<MathBox.Glyph>().Single(g => g.Text == "x");
        var underBase = AllGlyphs(underContainer).Cast<MathBox.Glyph>().Single(g => g.Text == "x");

        overRule.Y.Should().BeLessThan(overBase.Y);
        underRule.Y.Should().BeGreaterThan(underBase.Y + underBase.Metrics.Height);
        over.Metrics.Ascent.Should().BeGreaterThan(under.Metrics.Ascent,
            "overline adds ascent while underline adds descent in the shared layout");
    }

    [Theory]
    [InlineData("&#x0304;")]
    [InlineData("&#x0305;")]
    [InlineData("&#x00AF;")]
    public void Acc_WithOverbarAccent_EmitsSharedHorizontalRulePlan(string accent)
    {
        var mathNode = ParseOmml($"<m:acc><m:accPr><m:chr m:val=\"{accent}\"/></m:accPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:acc>");
        var box = MathLayoutEngine.Layout(mathNode, "Cambria Math", FontSizePt);

        var ops = MathBoxRenderPlanner.Plan(box, 10, 20, SrgbColor.Black, "Cambria Math");
        var rule = ops.OfType<MathDrawOp.DrawHRule>().Single();
        var baseGlyph = ops.OfType<MathDrawOp.DrawGlyph>().Single(g => g.Text == "x");

        rule.Y.Should().BeLessThan(baseGlyph.Y,
            "PowerPoint-authored accent bars should be represented as shared line ops above the base before either host draws");
        rule.Width.Should().BeGreaterThan(0);
        ops.OfType<MathDrawOp.DrawGlyph>()
            .Should().NotContain(g => g.Text == System.Net.WebUtility.HtmlDecode(accent),
                "accent bar characters should not depend on renderer-specific combining-glyph shaping");
    }

    [Fact]
    public void Delim_TwoElements_ExplicitPipeSepChr_RendersPipeBetweenElements()
    {
        var delim = new MathNode.Delim("{", "}", new MathNode[] { Run("x"), Run("P(x)") }, sepChar: "|");
        var box = MathLayoutEngine.Layout(delim, "Cambria Math", FontSizePt);

        var glyphs = AllGlyphs(box).Cast<MathBox.Glyph>().ToList();
        glyphs.Should().Contain(g => g.Text == "|", "the explicit m:sepChr=\"|\" must be rendered between the two m:e elements");
    }

    [Fact]
    public void Delim_TwoElements_DefaultSepChr_RendersComma()
    {
        var delim = new MathNode.Delim("(", ")", new MathNode[] { Run("x"), Run("y") });
        var box = MathLayoutEngine.Layout(delim, "Cambria Math", FontSizePt);

        var glyphs = AllGlyphs(box).Cast<MathBox.Glyph>().ToList();
        glyphs.Should().Contain(g => g.Text == ",", "the default (absent) m:sepChr is \",\" per ECMA-376 §22.1.2.20");
    }

    [Fact]
    public void Delim_SingleElement_NoSeparatorGlyph()
    {
        var delim = new MathNode.Delim("(", ")", new MathNode[] { Run("x") });
        var box = MathLayoutEngine.Layout(delim, "Cambria Math", FontSizePt);

        var glyphs = AllGlyphs(box).Cast<MathBox.Glyph>().ToList();
        glyphs.Should().NotContain(g => g.Text == ",", "a single m:e must never get a separator glyph");
    }

    [Fact]
    public void Delim_TwoElements_ExplicitEmptySepChr_NoSeparatorGlyph()
    {
        var delim = new MathNode.Delim("(", ")", new MathNode[] { Run("x"), Run("y") }, sepChar: "");
        var box = MathLayoutEngine.Layout(delim, "Cambria Math", FontSizePt);

        var glyphs = AllGlyphs(box).Cast<MathBox.Glyph>().ToList();
        glyphs.Should().NotContain(g => g.Text == ",");
        glyphs.Should().HaveCount(2, "only the two element glyphs should render, no separator");
    }

    [Fact]
    public void Delim_WithExplicitEmptyBegChr_DoesNotReserveOpenBracketSlot()
    {
        var twoSided = MathLayoutEngine.Layout(
            new MathNode.Delim("(", ")", new MathNode[] { Run("x") }),
            "Cambria Math",
            FontSizePt);
        var oneSided = MathLayoutEngine.Layout(
            new MathNode.Delim("", ")", new MathNode[] { Run("x") }),
            "Cambria Math",
            FontSizePt);

        var brackets = AllBrackets(oneSided).ToList();
        var closeBracket = brackets.Should().ContainSingle(
            "m:begChr m:val=\"\" suppresses only the opening bracket").Which;
        closeBracket.Character.Should().Be(")");

        var innerGlyph = AllGlyphs(oneSided).Cast<MathBox.Glyph>().Single(g => g.Text == "x");
        innerGlyph.X.Should().BeApproximately(0, 0.01,
            "the inner expression should start at the left edge when no opening bracket is requested");

        (twoSided.Metrics.Width - oneSided.Metrics.Width).Should().BeApproximately(closeBracket.Metrics.Width, 0.01,
            "a delimiter with an explicit empty opening bracket is one bracket slot narrower than the two-sided form");
    }

    [Fact]
    public void Delim_WithExplicitEmptyEndChr_DoesNotReserveCloseBracketSlot()
    {
        var twoSided = MathLayoutEngine.Layout(
            new MathNode.Delim("(", ")", new MathNode[] { Run("x") }),
            "Cambria Math",
            FontSizePt);
        var oneSided = MathLayoutEngine.Layout(
            new MathNode.Delim("(", "", new MathNode[] { Run("x") }),
            "Cambria Math",
            FontSizePt);

        var brackets = AllBrackets(oneSided).ToList();
        var openBracket = brackets.Should().ContainSingle(
            "m:endChr m:val=\"\" suppresses only the closing bracket").Which;
        openBracket.Character.Should().Be("(");
        openBracket.X.Should().BeApproximately(0, 0.01);

        var innerGlyph = AllGlyphs(oneSided).Cast<MathBox.Glyph>().Single(g => g.Text == "x");
        innerGlyph.X.Should().BeApproximately(openBracket.Metrics.Width, 0.01,
            "normal opening delimiter spacing is preserved when only the closing bracket is suppressed");

        (twoSided.Metrics.Width - oneSided.Metrics.Width).Should().BeApproximately(openBracket.Metrics.Width, 0.01,
            "a delimiter with an explicit empty closing bracket is one bracket slot narrower than the two-sided form");
    }

    // ── HA6: m:f fPr/type — fraction bar style ──────────────────────────────

    [Fact]
    public void Delim_WithGrowFalse_UsesNormalBracketHeightWithoutClippingTallInnerExpression()
    {
        var grow = MathLayoutEngine.Layout(
            new MathNode.Delim("(", ")", new MathNode[] { TallFraction() }),
            "Cambria Math",
            FontSizePt);
        var noGrow = MathLayoutEngine.Layout(
            new MathNode.Delim("(", ")", new MathNode[] { TallFraction() }, grow: false),
            "Cambria Math",
            FontSizePt);

        var growBracket = AllBrackets(grow).First();
        var noGrowBracket = AllBrackets(noGrow).First();

        noGrowBracket.ScaledHeight.Should().BeLessThan(growBracket.ScaledHeight);
        noGrow.Metrics.Height.Should().BeGreaterThan(noGrowBracket.ScaledHeight);
        noGrow.Metrics.Height.Should().BeApproximately(grow.Metrics.Height / 1.10, 0.01);
    }

    [Fact]
    public void Delim_WithCenteredShape_UsesOrdinaryBracketHeightWithoutChangingInnerLayout()
    {
        var match = MathLayoutEngine.Layout(
            new MathNode.Delim("(", ")", new MathNode[] { TallFraction() }),
            "Cambria Math",
            FontSizePt);
        var centered = MathLayoutEngine.Layout(
            new MathNode.Delim(
                "(",
                ")",
                new MathNode[] { TallFraction() },
                shape: MathNode.Delim.DelimiterShape.Centered),
            "Cambria Math",
            FontSizePt);

        var matchBracket = AllBrackets(match).First();
        var centeredBracket = AllBrackets(centered).First();
        var centeredDelimiter = Assert.IsType<MathBox.Container>(centered.Children[0]);

        centeredBracket.ScaledHeight.Should().BeLessThan(matchBracket.ScaledHeight,
            "m:dPr/m:shp=centered keeps ordinary delimiter glyph height even when m:grow remains on");
        centered.Metrics.Height.Should().BeApproximately(centeredDelimiter.Children[1].Metrics.Height, 0.01,
            "the tall inner expression still determines the shared delimiter container height");
        centered.Metrics.Height.Should().BeApproximately(match.Metrics.Height / 1.10, 0.01);
    }

    [Fact]
    public void EqArray_StacksRowsAndReportsFullHeight()
    {
        var eqArray = new MathNode.EqArray(new MathNode[]
        {
            Run("x"),
            TallFraction(),
            Run("mmmm")
        });

        var box = MathLayoutEngine.Layout(eqArray, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];

        container.Children.Should().HaveCount(3);

        var firstRow = container.Children[0];
        var secondRow = container.Children[1];
        var thirdRow = container.Children[2];

        secondRow.Y.Should().BeGreaterThan(firstRow.Y + firstRow.Metrics.Height,
            "equation arrays stack direct m:e rows vertically with a row gap");
        thirdRow.Y.Should().BeGreaterThan(secondRow.Y + secondRow.Metrics.Height);

        container.Metrics.Width.Should().BeApproximately(
            container.Children.Max(child => child.Metrics.Width),
            0.01,
            "the equation array reports the max row width");

        var lastBottom = thirdRow.Y + thirdRow.Metrics.Height;
        container.Metrics.Height.Should().BeApproximately(lastBottom, 0.01,
            "the equation array height must include every stacked row and gap");
        container.Metrics.Ascent.Should().BeGreaterThan(0);
        container.Metrics.Descent.Should().BeGreaterThanOrEqualTo(0,
            "reported baseline/descent must not imply clipping below the box");
        container.Children.Should().OnlyContain(child =>
            child.Y >= 0 && child.Y + child.Metrics.Height <= container.Metrics.Height + 0.01);
    }

    [Fact]
    public void EqArray_AlignsRowsOnInvisibleAlignmentPoints()
    {
        var eqArray = new MathNode.EqArray(
            new MathNode[]
            {
                new MathNode.Row(new MathNode[] { Run("mmmm"), Run("=1") }),
                new MathNode.Row(new MathNode[] { Run("x"), Run("=22") }),
                Run("center")
            },
            new int?[] { 1, 1, null });

        var box = MathLayoutEngine.Layout(eqArray, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];

        var firstRow = (MathBox.Container)container.Children[0];
        var secondRow = (MathBox.Container)container.Children[1];
        var thirdRow = container.Children[2];

        double firstMarkerX = firstRow.X + firstRow.Children[1].X;
        double secondMarkerX = secondRow.X + secondRow.Children[1].X;

        firstMarkerX.Should().BeApproximately(secondMarkerX, 0.01,
            "m:eqArr rows with m:aln markers should share the same marker x-coordinate");
        firstRow.X.Should().BeLessThan(secondRow.X,
            "the row with a wider expression before m:aln shifts left to keep the marker aligned");
        thirdRow.X.Should().BeApproximately((container.Metrics.Width - thirdRow.Metrics.Width) / 2.0, 0.01,
            "rows without m:aln keep the previous centered equation-array behavior");
    }

    [Fact]
    public void OmmlEqArray_BoxPropertyAlignmentMarkers_AlignBoxedTermsInSharedPlan()
    {
        var node = ParseOmml(
            "<m:eqArr>" +
            "<m:e><m:r><m:t>mmmm</m:t></m:r><m:box><m:boxPr><m:aln/></m:boxPr><m:e><m:r><m:t>=1</m:t></m:r></m:e></m:box></m:e>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:box><m:boxPr><m:aln/></m:boxPr><m:e><m:r><m:t>=22</m:t></m:r></m:e></m:box></m:e>" +
            "<m:e><m:r><m:t>center</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);
        var container = Assert.IsType<MathBox.Container>(layout.Children[0]);
        var firstRow = Assert.IsType<MathBox.Container>(container.Children[0]);
        var secondRow = Assert.IsType<MathBox.Container>(container.Children[1]);
        var thirdRow = container.Children[2];

        double firstMarkerX = firstRow.X + firstRow.Children[1].X;
        double secondMarkerX = secondRow.X + secondRow.Children[1].X;

        firstMarkerX.Should().BeApproximately(secondMarkerX, 0.01,
            "m:boxPr/m:aln should feed the same shared equation-array alignment metadata as a direct m:aln marker");
        firstRow.X.Should().BeLessThan(secondRow.X,
            "the row with wider content before the boxed alignment point shifts left in shared layout");
        thirdRow.X.Should().BeApproximately((container.Metrics.Width - thirdRow.Metrics.Width) / 2.0, 0.01,
            "unaligned rows keep centered equation-array placement");

        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();
        glyphs.Select(g => g.Text).Should().Equal(new[] { "mmmm", "=1", "x", "=22", "center" });
        glyphs.Single(g => g.Text == "=1").X.Should().BeApproximately(glyphs.Single(g => g.Text == "=22").X, 0.01,
            "boxed aligned terms should reach WPF and Avalonia at the same shared draw-plan X coordinate");
    }

    [Fact]
    public void EqArray_RowSpacingRuleChangesVerticalGapWithoutChangingRowOrder()
    {
        var rows = new MathNode[]
        {
            Run("a"),
            Run("b"),
            Run("c")
        };
        var defaultEqArray = new MathNode.EqArray(rows);
        var spacedEqArray = new MathNode.EqArray(
            rows,
            rowSpacingRule: MathNode.EqArray.EqArraySpacingRule.Double);

        var defaultContainer = (MathBox.Container)MathLayoutEngine
            .Layout(defaultEqArray, "Cambria Math", FontSizePt)
            .Children[0];
        var spacedContainer = (MathBox.Container)MathLayoutEngine
            .Layout(spacedEqArray, "Cambria Math", FontSizePt)
            .Children[0];

        var defaultTexts = defaultContainer.Children.Cast<MathBox.Glyph>().Select(g => g.Text);
        var spacedTexts = spacedContainer.Children.Cast<MathBox.Glyph>().Select(g => g.Text);

        spacedContainer.Children[1].Y.Should().BeGreaterThan(defaultContainer.Children[1].Y,
            "m:eqArrPr/m:rSpRule should increase the shared row gap");
        spacedContainer.Metrics.Height.Should().BeGreaterThan(defaultContainer.Metrics.Height);
        spacedTexts.Should().Equal(defaultTexts,
            "spacing metadata must not reorder equation-array rows");
    }

    [Fact]
    public void EqArray_BaseJustificationChangesReportedAscentWithoutMovingRowsOrAlignmentPoints()
    {
        var rows = new MathNode[]
        {
            new MathNode.Row(new MathNode[] { Run("mmmm"), Run("=1") }),
            new MathNode.Row(new MathNode[] { Run("x"), Run("=22") }),
            TallFraction()
        };
        var alignmentPoints = new int?[] { 1, 1, null };
        var topEqArray = new MathNode.EqArray(
            rows,
            alignmentPoints,
            baseJustification: MathNode.EqArray.EqArrayBaseJustification.Top);
        var centerEqArray = new MathNode.EqArray(
            rows,
            alignmentPoints,
            baseJustification: MathNode.EqArray.EqArrayBaseJustification.Center);
        var bottomEqArray = new MathNode.EqArray(
            rows,
            alignmentPoints,
            baseJustification: MathNode.EqArray.EqArrayBaseJustification.Bottom);

        var top = (MathBox.Container)MathLayoutEngine.Layout(topEqArray, "Cambria Math", FontSizePt).Children[0];
        var center = (MathBox.Container)MathLayoutEngine.Layout(centerEqArray, "Cambria Math", FontSizePt).Children[0];
        var bottom = (MathBox.Container)MathLayoutEngine.Layout(bottomEqArray, "Cambria Math", FontSizePt).Children[0];

        top.Children.Select(child => child.Y).Should().Equal(
            center.Children.Select(child => child.Y),
            "baseJc changes the equation-array baseline/ascent contract, not row layout");
        bottom.Children.Select(child => child.Y).Should().Equal(center.Children.Select(child => child.Y));

        GetEqArrayMarkerX(top, 0, 1).Should().BeApproximately(GetEqArrayMarkerX(top, 1, 1), 0.01,
            "top baseline behavior must preserve direct m:aln alignment");
        GetEqArrayMarkerX(bottom, 0, 1).Should().BeApproximately(GetEqArrayMarkerX(bottom, 1, 1), 0.01,
            "bottom baseline behavior must preserve direct m:aln alignment");

        top.Metrics.Ascent.Should().BeLessThan(center.Metrics.Ascent);
        center.Metrics.Ascent.Should().BeLessThan(bottom.Metrics.Ascent);
        bottom.Metrics.Ascent.Should().BeLessThanOrEqualTo(bottom.Metrics.Height);
    }

    [Fact]
    public void OmmlEqArraySpacingAndBaseJustification_RenderPlanCarriesSharedRowOffsets()
    {
        var defaultNode = ParseOmml(
            "<m:eqArr>" +
            "<m:e><m:r><m:t>mmmm</m:t></m:r><m:aln/><m:r><m:t>=1</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:aln/><m:r><m:t>=22</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>z</m:t></m:r></m:e>" +
            "</m:eqArr>");
        var spacedNode = ParseOmml(
            "<m:eqArr>" +
            "<m:eqArrPr><m:baseJc m:val=\"bot\"/><m:rSpRule m:val=\"3\"/><m:rSp m:val=\"24\"/></m:eqArrPr>" +
            "<m:e><m:r><m:t>mmmm</m:t></m:r><m:aln/><m:r><m:t>=1</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>x</m:t></m:r><m:aln/><m:r><m:t>=22</m:t></m:r></m:e>" +
            "<m:e><m:r><m:t>z</m:t></m:r></m:e>" +
            "</m:eqArr>");

        var defaultEqArray = (MathBox.Container)MathLayoutEngine
            .Layout(defaultNode, "Cambria Math", FontSizePt)
            .Children[0];
        var spacedRoot = MathLayoutEngine.Layout(spacedNode, "Cambria Math", FontSizePt);
        var spacedEqArray = (MathBox.Container)spacedRoot.Children[0];

        spacedEqArray.Children[1].Y.Should().BeGreaterThan(defaultEqArray.Children[1].Y,
            "m:eqArrPr/m:rSpRule and m:rSp should increase shared row offsets before any host draws");
        spacedEqArray.Metrics.Ascent.Should().BeGreaterThan(defaultEqArray.Metrics.Ascent,
            "m:eqArrPr/m:baseJc=bot should report a bottom-row baseline contract through shared MathBox metrics");

        var glyphs = MathBoxRenderPlanner.Plan(spacedRoot, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "mmmm", "=1", "x", "=22", "z" });
        glyphs.Single(g => g.Text == "=1").X.Should().BeApproximately(glyphs.Single(g => g.Text == "=22").X, 0.01,
            "direct m:aln alignment points should remain aligned when row spacing and base justification are present");
        glyphs.Single(g => g.Text == "=22").Y.Should().BeGreaterThan(glyphs.Single(g => g.Text == "=1").Y);
        glyphs.Single(g => g.Text == "z").Y.Should().BeGreaterThan(glyphs.Single(g => g.Text == "=22").Y);
    }

    [Fact]
    public void OmmlManualBreak_LayoutsAsStackedEquationArrayRows()
    {
        var node = ParseOmml(
            "<m:r><m:t>x</m:t></m:r>" +
            "<m:r><m:rPr><m:brk/></m:rPr><m:t>y</m:t></m:r>");

        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];

        container.Children.Should().HaveCount(2);
        container.Children[1].Y.Should().BeGreaterThan(
            container.Children[0].Y + container.Children[0].Metrics.Height,
            "m:brk starts the following run on a new displayed equation line");
    }

    [Fact]
    public void EmptyFallbackGlyph_DoesNotThrowOrReserveWidth()
    {
        var box = MathLayoutEngine.Layout(new MathNode.Unknown(string.Empty), "Cambria Math", FontSizePt);
        var glyph = Assert.IsType<MathBox.Glyph>(box.Children[0]);

        glyph.Text.Should().BeEmpty();
        glyph.Metrics.Width.Should().Be(0);
        glyph.Metrics.Height.Should().Be(0);
    }

    [Fact]
    public void Matrix_UsesMaxRowCellCount_ForRaggedRows()
    {
        var matrix = new MathNode.Matrix(new[]
        {
            new MathNode[] { Run("a") },
            new MathNode[] { Run("b"), Run("c"), Run("ddd") }
        });

        var box = MathLayoutEngine.Layout(matrix, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];
        var glyphs = container.Children.Cast<MathBox.Glyph>().Select(g => g.Text).ToList();

        glyphs.Should().Equal(new[] { "a", "b", "c", "ddd" },
            "matrix layout must not size itself from only the first row and drop later cells");
        container.Children[2].X.Should().BeGreaterThan(container.Children[1].X,
            "the second cell in the wider later row must be assigned to its own column");
        container.Children[3].X.Should().BeGreaterThan(container.Children[2].X,
            "the third cell in the wider later row must be assigned to its own column");
    }

    [Fact]
    public void Matrix_EmptyAuthoredCell_DefaultsToSharedPlaceholderGlyph()
    {
        var node = ParseOmml(
            "<m:m>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e/></m:mr>" +
            "</m:m>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var glyphs = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal(new[] { "a", "\u25A1" },
            "an authored empty matrix cell should become a shared placeholder glyph before WPF or Avalonia draws it");
        glyphs[1].X.Should().BeGreaterThan(glyphs[0].X,
            "the placeholder must occupy its own matrix column in the renderer-neutral plan");
    }

    [Fact]
    public void Matrix_WithPlcHide_SuppressesSharedPlaceholderGlyph()
    {
        var node = ParseOmml(
            "<m:m>" +
            "<m:mPr><m:plcHide/></m:mPr>" +
            "<m:mr><m:e><m:r><m:t>a</m:t></m:r></m:e><m:e/></m:mr>" +
            "</m:m>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Select(g => g.Text)
            .Should().Equal(new[] { "a" },
                "m:mPr/m:plcHide must remove empty-cell placeholders in the shared plan before either renderer consumes it");
    }

    [Fact]
    public void Matrix_AppliesPerColumnAlignmentWithinColumnWidths()
    {
        var matrix = new MathNode.Matrix(
            new[]
            {
                new MathNode[] { Run("wide"), Run("wide"), Run("wide") },
                new MathNode[] { Run("x"), Run("y"), Run("z") }
            },
            new[]
            {
                MathNode.Matrix.MatrixColumnAlignment.Left,
                MathNode.Matrix.MatrixColumnAlignment.Center,
                MathNode.Matrix.MatrixColumnAlignment.Right
            });

        var box = MathLayoutEngine.Layout(matrix, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];
        var wideLeft = container.Children[0];
        var wideCenter = container.Children[1];
        var wideRight = container.Children[2];
        var leftCell = container.Children[3];
        var centerCell = container.Children[4];
        var rightCell = container.Children[5];

        leftCell.X.Should().BeApproximately(wideLeft.X, 0.01,
            "left-aligned matrix cells start at their column origin");
        (centerCell.X + centerCell.Metrics.Width / 2.0)
            .Should().BeApproximately(wideCenter.X + wideCenter.Metrics.Width / 2.0, 0.01,
                "center-aligned matrix cells remain centered in the column");
        (rightCell.X + rightCell.Metrics.Width)
            .Should().BeApproximately(wideRight.X + wideRight.Metrics.Width, 0.01,
                "right-aligned matrix cells end at the column right edge");
    }

    [Fact]
    public void OmmlMatrixColumnAlignmentCount_RepeatsAlignmentAcrossSharedColumns()
    {
        var node = ParseOmml(
            "<m:m>" +
            "<m:mPr><m:mcs>" +
            "<m:mc><m:mcPr><m:count m:val=\"2\"/><m:aln m:val=\"left\"/></m:mcPr></m:mc>" +
            "<m:mc><m:mcPr><m:aln m:val=\"right\"/></m:mcPr></m:mc>" +
            "</m:mcs></m:mPr>" +
            "<m:mr><m:e><m:r><m:t>wide</m:t></m:r></m:e><m:e><m:r><m:t>wide</m:t></m:r></m:e><m:e><m:r><m:t>wide</m:t></m:r></m:e></m:mr>" +
            "<m:mr><m:e><m:r><m:t>x</m:t></m:r></m:e><m:e><m:r><m:t>y</m:t></m:r></m:e><m:e><m:r><m:t>z</m:t></m:r></m:e></m:mr>" +
            "</m:m>");

        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];
        var wideLeft = container.Children[0];
        var wideRepeatedLeft = container.Children[1];
        var wideRight = container.Children[2];
        var repeatedLeftCell = container.Children[4];
        var rightCell = container.Children[5];

        repeatedLeftCell.X.Should().BeApproximately(wideRepeatedLeft.X, 0.01,
            "m:mcPr/m:count should repeat the left-alignment policy into the second matrix column");
        (rightCell.X + rightCell.Metrics.Width)
            .Should().BeApproximately(wideRight.X + wideRight.Metrics.Width, 0.01,
                "the following m:mc alignment policy should still apply to the next column after the repeat");

        var glyphs = MathBoxRenderPlanner.Plan(box, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();
        glyphs.Single(g => g.Text == "y").X.Should().BeApproximately(
            10 + repeatedLeftCell.X,
            0.01,
            "WPF and Avalonia consume the repeated matrix alignment as shared draw-plan coordinates");
    }

    [Fact]
    public void Matrix_RowSpacingRuleChangesVerticalGap()
    {
        var rows = new[]
        {
            new MathNode[] { Run("a") },
            new MathNode[] { Run("b") }
        };
        var defaultMatrix = new MathNode.Matrix(rows);
        var spacedMatrix = new MathNode.Matrix(
            rows,
            rowSpacingRule: MathNode.Matrix.MatrixSpacingRule.Double);

        var defaultContainer = (MathBox.Container)MathLayoutEngine
            .Layout(defaultMatrix, "Cambria Math", FontSizePt)
            .Children[0];
        var spacedContainer = (MathBox.Container)MathLayoutEngine
            .Layout(spacedMatrix, "Cambria Math", FontSizePt)
            .Children[0];

        var defaultSecondRow = defaultContainer.Children[1];
        var spacedSecondRow = spacedContainer.Children[1];

        spacedSecondRow.Y.Should().BeGreaterThan(defaultSecondRow.Y,
            "explicit m:rSpRule metadata should increase the shared vertical matrix gap");
        spacedContainer.Metrics.Height.Should().BeGreaterThan(defaultContainer.Metrics.Height);
    }

    [Fact]
    public void Matrix_ColumnGapRuleChangesHorizontalGap()
    {
        var rows = new[]
        {
            new MathNode[] { Run("a"), Run("b") }
        };
        var defaultMatrix = new MathNode.Matrix(rows);
        var spacedMatrix = new MathNode.Matrix(
            rows,
            columnGapRule: MathNode.Matrix.MatrixSpacingRule.Exactly,
            columnGap: 24);

        var defaultContainer = (MathBox.Container)MathLayoutEngine
            .Layout(defaultMatrix, "Cambria Math", FontSizePt)
            .Children[0];
        var spacedContainer = (MathBox.Container)MathLayoutEngine
            .Layout(spacedMatrix, "Cambria Math", FontSizePt)
            .Children[0];

        var defaultSecondCell = defaultContainer.Children[1];
        var spacedSecondCell = spacedContainer.Children[1];

        spacedSecondCell.X.Should().BeGreaterThan(defaultSecondCell.X,
            "explicit m:cGpRule/m:cGp metadata should increase the shared horizontal matrix gap");
        spacedContainer.Metrics.Width.Should().BeGreaterThan(defaultContainer.Metrics.Width);
    }

    [Fact]
    public void Matrix_BaseJustificationChangesReportedAscentWithoutMovingCells()
    {
        var rows = new[]
        {
            new MathNode[] { Run("a") },
            new MathNode[] { TallFraction() }
        };
        var topMatrix = new MathNode.Matrix(
            rows,
            baseJustification: MathNode.Matrix.MatrixBaseJustification.Top);
        var centerMatrix = new MathNode.Matrix(
            rows,
            baseJustification: MathNode.Matrix.MatrixBaseJustification.Center);
        var bottomMatrix = new MathNode.Matrix(
            rows,
            baseJustification: MathNode.Matrix.MatrixBaseJustification.Bottom);

        var top = (MathBox.Container)MathLayoutEngine.Layout(topMatrix, "Cambria Math", FontSizePt).Children[0];
        var center = (MathBox.Container)MathLayoutEngine.Layout(centerMatrix, "Cambria Math", FontSizePt).Children[0];
        var bottom = (MathBox.Container)MathLayoutEngine.Layout(bottomMatrix, "Cambria Math", FontSizePt).Children[0];

        top.Children.Select(child => child.Y).Should().Equal(
            center.Children.Select(child => child.Y),
            "baseJc changes the matrix baseline/ascent contract, not the cell layout positions");
        bottom.Children.Select(child => child.Y).Should().Equal(center.Children.Select(child => child.Y));

        top.Metrics.Ascent.Should().BeLessThan(center.Metrics.Ascent);
        center.Metrics.Ascent.Should().BeLessThan(bottom.Metrics.Ascent);
        bottom.Metrics.Ascent.Should().BeLessThanOrEqualTo(bottom.Metrics.Height);
    }

    [Fact]
    public void Frac_DefaultBarType_RendersHRule_Unchanged()
    {
        var frac = new MathNode.Frac(Run("1"), Run("2")); // default FracType.Bar
        var box = MathLayoutEngine.Layout(frac, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        container.Children.Should().ContainSingle(b => b is MathBox.HRule,
            "the default bar fraction must still draw exactly one HRule (no regression)");
    }

    [Theory]
    [InlineData(MathNode.FracType.Bar, 0.85)]
    [InlineData(MathNode.FracType.NoBar, 0.85)]
    [InlineData(MathNode.FracType.Linear, 1.00)]
    [InlineData(MathNode.FracType.Skewed, 0.85)]
    public void Frac_SmallFraction_UsesScriptSizeForNumeratorAndDenominator(
        MathNode.FracType type,
        double defaultChildScale)
    {
        var fraction = new MathNode.Frac(Run("a"), Run("b"), type);
        var normal = MathLayoutEngine.Layout(fraction, "Cambria Math", FontSizePt);
        var small = MathLayoutEngine.Layout(
            new MathNode.MathRoot(
                fraction,
                new MathNode.MathProperties(SmallFraction: true)),
            "Cambria Math",
            FontSizePt);
        var explicitlyOff = MathLayoutEngine.Layout(
            new MathNode.MathRoot(
                fraction,
                new MathNode.MathProperties(SmallFraction: false)),
            "Cambria Math",
            FontSizePt);

        var normalGlyphs = AllGlyphs(normal).Cast<MathBox.Glyph>().ToDictionary(g => g.Text);
        var smallGlyphs = AllGlyphs(small).Cast<MathBox.Glyph>().ToDictionary(g => g.Text);
        var offGlyphs = AllGlyphs(explicitlyOff).Cast<MathBox.Glyph>().ToDictionary(g => g.Text);

        normalGlyphs["a"].FontSizePt.Should().BeApproximately(FontSizePt * defaultChildScale, 0.001);
        normalGlyphs["b"].FontSizePt.Should().BeApproximately(FontSizePt * defaultChildScale, 0.001);
        offGlyphs["a"].FontSizePt.Should().BeApproximately(normalGlyphs["a"].FontSizePt, 0.001);
        offGlyphs["b"].FontSizePt.Should().BeApproximately(normalGlyphs["b"].FontSizePt, 0.001);
        smallGlyphs["a"].FontSizePt.Should().BeApproximately(FontSizePt * 0.70, 0.001);
        smallGlyphs["b"].FontSizePt.Should().BeApproximately(FontSizePt * 0.70, 0.001);

        if (type == MathNode.FracType.Linear)
        {
            smallGlyphs["/"].FontSizePt.Should().BeApproximately(FontSizePt, 0.001,
                "smallFrac changes numerator/denominator script content but keeps the inline slash readable");
        }
    }

    [Fact]
    public void Frac_NoBarType_HasNoHRule_ButKeepsStackedNumDen()
    {
        var frac = new MathNode.Frac(Run("n"), Run("k"), MathNode.FracType.NoBar);
        var box = MathLayoutEngine.Layout(frac, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        container.Children.Should().NotContain(b => b is MathBox.HRule,
            "noBar (binomial style) must not draw a bar line");
        container.Children.Should().HaveCount(2, "noBar still stacks exactly numerator + denominator, no bar");
    }

    [Fact]
    public void Frac_LinearType_RendersSlashGlyph_NotStacked()
    {
        var frac = new MathNode.Frac(Run("a"), Run("b"), MathNode.FracType.Linear);
        var box = MathLayoutEngine.Layout(frac, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        container.Children.Should().NotContain(b => b is MathBox.HRule,
            "linear fractions render inline with a slash, not a bar");
        container.Children.Should().NotContain(b => b is MathBox.Line,
            "linear fractions keep the slash glyph path instead of the skewed line primitive");

        var glyphs = AllGlyphs(container).Cast<MathBox.Glyph>().ToList();
        glyphs.Should().Contain(g => g.Text == "/", "the linear form must include a slash glyph");
    }

    [Fact]
    public void Frac_SkewedType_RendersDiagonalLineWithOffsetNumeratorAndDenominator()
    {
        var frac = new MathNode.Frac(Run("a"), Run("b"), MathNode.FracType.Skewed);
        var box = MathLayoutEngine.Layout(frac, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        container.Children.Should().NotContain(b => b is MathBox.HRule,
            "skw must never render as a bar fraction");
        container.Children.Should().HaveCount(3, "skw emits numerator, diagonal line, and denominator");

        var numBox = container.Children[0];
        var line = Assert.IsType<MathBox.Line>(container.Children[1]);
        var denBox = container.Children[2];

        line.X2.Should().BeGreaterThan(0, "the shared line primitive should advance left-to-right");
        line.Y2.Should().BeLessThan(0, "the diagonal should rise from denominator side to numerator side");
        line.Thickness.Should().BeGreaterThan(0);
        denBox.X.Should().BeGreaterThan(numBox.X + numBox.Metrics.Width,
            "the denominator should be offset to the right of the numerator");
        denBox.Y.Should().BeGreaterThan(numBox.Y,
            "the denominator should be offset below the numerator");

        var glyphs = AllGlyphs(container).Cast<MathBox.Glyph>().Select(g => g.Text).ToList();
        glyphs.Should().Equal(new[] { "a", "b" },
            "skw uses a drawn diagonal line, not a literal slash glyph");

        var ops = MathBoxRenderPlanner.Plan(box, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawLine>().Should().ContainSingle(drawLine =>
            drawLine.X2 > drawLine.X1 && drawLine.Y2 < drawLine.Y1,
            "WPF and Avalonia both consume the shared diagonal as DrawLine");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Equal(new[] { "a", "b" });
    }

    [Fact]
    public void Row_WithSkewedFraction_AlignsAdjacentRunsOnCommonBaseline()
    {
        var row = new MathNode.Row(new MathNode[]
        {
            Run("x"),
            new MathNode.Frac(Run("a"), Run("b"), MathNode.FracType.Skewed),
            Run("y")
        });

        var box = MathLayoutEngine.Layout(row, "Cambria Math", FontSizePt);
        var container = (MathBox.Container)box.Children[0];

        container.Children.Should().HaveCount(3);
        foreach (var child in container.Children)
        {
            (child.Y + child.Metrics.Ascent).Should().BeApproximately(
                container.Metrics.Ascent,
                0.01,
                "row layout must preserve one shared baseline for text and skewed fractions");
        }

        var skewed = Assert.IsType<MathBox.Container>(container.Children[1]);
        skewed.Metrics.Height.Should().BeGreaterThan(0);
        skewed.Metrics.Ascent.Should().BeGreaterThan(0);
        skewed.Metrics.Descent.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Box_LayoutsAsTransparentChildWrapper()
    {
        var child = new MathNode.Frac(Run("1"), new MathNode.Rad(null, Run("x")));
        var layout = MathLayoutEngine.Layout(new MathNode.Box(child), "Cambria Math", FontSizePt);

        var wrapper = Assert.IsType<MathBox.Container>(layout.Children[0]);
        wrapper.Children.Should().ContainSingle("transparent m:box should preserve its child box without drawing a border");
        wrapper.Children.Should().NotContain(b => b is MathBox.Line);

        var childBox = wrapper.Children[0];
        wrapper.Metrics.Width.Should().BeApproximately(childBox.Metrics.Width, 0.01);
        wrapper.Metrics.Height.Should().BeApproximately(childBox.Metrics.Height, 0.01);
        wrapper.Metrics.Ascent.Should().BeApproximately(childBox.Metrics.Ascent, 0.01);
    }

    [Fact]
    public void Row_BoxOperatorEmulatorDoubleEquals_AddsRelationSpacingAdvance()
    {
        var emulatorRow = new MathNode.Row(new MathNode[]
        {
            Run("a"),
            new MathNode.Box(Run("==", isItalic: false), operatorEmulator: true),
            Run("b")
        });
        var ordinaryRow = new MathNode.Row(new MathNode[]
        {
            Run("a"),
            new MathNode.Box(Run("==", isItalic: false)),
            Run("b")
        });

        var emulatorLayout = MathLayoutEngine.Layout(emulatorRow, "Cambria Math", FontSizePt);
        var ordinaryLayout = MathLayoutEngine.Layout(ordinaryRow, "Cambria Math", FontSizePt);
        var emulatorContainer = Assert.IsType<MathBox.Container>(emulatorLayout.Children[0]);
        var ordinaryContainer = Assert.IsType<MathBox.Container>(ordinaryLayout.Children[0]);

        emulatorLayout.Metrics.Width.Should().BeGreaterThan(ordinaryLayout.Metrics.Width,
            "m:boxPr/m:opEmu should make a boxed multi-glyph operator contribute relation-class spacing");
        emulatorContainer.Children[1].X.Should().BeGreaterThan(ordinaryContainer.Children[1].X,
            "the operator-emulator box receives shared spacing before it");
        emulatorContainer.Children[2].X.Should().BeGreaterThan(ordinaryContainer.Children[2].X,
            "the following sibling advances past shared operator-emulator spacing");

        var ops = MathBoxRenderPlanner.Plan(emulatorLayout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();
        ops.Select(g => g.Text).Should().Equal(new[] { "a", "==", "b" });
    }

    [Fact]
    public void Phantom_Hidden_ReservesNaturalMetricsButEmitsNoGlyphs()
    {
        var child = new MathNode.Frac(Run("1"), new MathNode.Rad(null, Run("x")));
        var natural = MathLayoutEngine.Layout(child, "Cambria Math", FontSizePt);
        var layout = MathLayoutEngine.Layout(
            new MathNode.Phantom(child, show: false),
            "Cambria Math",
            FontSizePt);

        var phantomBox = Assert.IsType<MathBox.Container>(layout.Children[0]);
        phantomBox.Children.Should().BeEmpty("show=0 phantom reserves space without visible child boxes");
        layout.Metrics.Width.Should().BeApproximately(natural.Metrics.Width, 0.01);
        layout.Metrics.Height.Should().BeApproximately(natural.Metrics.Height, 0.01);
        layout.Metrics.Ascent.Should().BeApproximately(natural.Metrics.Ascent, 0.01);

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Should().BeEmpty("hidden phantom children must not reach the shared draw plan");
        ops.Should().BeEmpty("hidden phantom with only glyph/radical descendants should not emit any draw operations");
    }

    [Fact]
    public void Phantom_ZeroWidth_ReportsZeroWidthButKeepsVisibleChild()
    {
        var child = Run("wide");
        var natural = MathLayoutEngine.Layout(child, "Cambria Math", FontSizePt);
        var layout = MathLayoutEngine.Layout(
            new MathNode.Phantom(child, zeroWidth: true),
            "Cambria Math",
            FontSizePt);

        var phantomBox = Assert.IsType<MathBox.Container>(layout.Children[0]);
        phantomBox.Children.Should().ContainSingle("show=true phantom keeps its child boxes visible");
        layout.Metrics.Width.Should().Be(0);
        layout.Metrics.Height.Should().BeApproximately(natural.Metrics.Height, 0.01);
        layout.Metrics.Ascent.Should().BeApproximately(natural.Metrics.Ascent, 0.01);

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain("wide");
    }

    [Fact]
    public void Phantom_ZeroAscent_ReportsNoAscentAndKeepsNaturalDescent()
    {
        var child = Run("x");
        var natural = MathLayoutEngine.Layout(child, "Cambria Math", FontSizePt);
        var naturalDescent = natural.Metrics.Height - natural.Metrics.Ascent;
        var layout = MathLayoutEngine.Layout(
            new MathNode.Phantom(child, show: false, zeroAscent: true),
            "Cambria Math",
            FontSizePt);

        layout.Metrics.Width.Should().BeApproximately(natural.Metrics.Width, 0.01);
        layout.Metrics.Ascent.Should().Be(0);
        layout.Metrics.Height.Should().BeApproximately(naturalDescent, 0.01);
        layout.Metrics.Descent.Should().BeApproximately(naturalDescent, 0.01);
    }

    [Fact]
    public void Phantom_ZeroDescent_ReportsNoDescent()
    {
        var child = Run("x");
        var natural = MathLayoutEngine.Layout(child, "Cambria Math", FontSizePt);
        var layout = MathLayoutEngine.Layout(
            new MathNode.Phantom(child, show: false, zeroDescent: true),
            "Cambria Math",
            FontSizePt);

        layout.Metrics.Width.Should().BeApproximately(natural.Metrics.Width, 0.01);
        layout.Metrics.Ascent.Should().BeApproximately(natural.Metrics.Ascent, 0.01);
        layout.Metrics.Height.Should().BeApproximately(natural.Metrics.Ascent, 0.01);
        layout.Metrics.Descent.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void Row_TransparentHiddenZeroWidthPhantomBinaryOperator_AddsSpacingAdvanceWithoutGlyph()
    {
        var transparentRow = new MathNode.Row(new MathNode[]
        {
            Run("x"),
            new MathNode.Phantom(Run("+"), show: false, zeroWidth: true, transparentSpacing: true),
            Run("y")
        });
        var packedRow = new MathNode.Row(new MathNode[]
        {
            Run("x"),
            new MathNode.Phantom(Run("+"), show: false, zeroWidth: true),
            Run("y")
        });

        var transparentLayout = MathLayoutEngine.Layout(transparentRow, "Cambria Math", FontSizePt);
        var packedLayout = MathLayoutEngine.Layout(packedRow, "Cambria Math", FontSizePt);

        var transparentContainer = Assert.IsType<MathBox.Container>(transparentLayout.Children[0]);
        var packedContainer = Assert.IsType<MathBox.Container>(packedLayout.Children[0]);
        var transparentY = transparentContainer.Children[2];
        var packedY = packedContainer.Children[2];

        transparentLayout.Metrics.Width.Should().BeGreaterThan(packedLayout.Metrics.Width,
            "m:phantPr/m:transp should let a hidden zero-width operator contribute operator-class row spacing");
        transparentY.X.Should().BeGreaterThan(packedY.X,
            "the following sibling should advance past the transparent phantom operator spacing");

        var ops = MathBoxRenderPlanner.Plan(transparentLayout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "x", "y" },
                "transparent phantom spacing affects advance only and must not reintroduce hidden glyph draw ops");
    }

    [Fact]
    public void Row_TransparentZeroWidthRelationPhantom_UsesWiderRelationSpacing()
    {
        var binaryLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("a"),
                new MathNode.Phantom(Run("+"), show: false, zeroWidth: true, transparentSpacing: true),
                Run("b")
            }),
            "Cambria Math",
            FontSizePt);
        var relationLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("a"),
                new MathNode.Phantom(Run("="), show: false, zeroWidth: true, transparentSpacing: true),
                Run("b")
            }),
            "Cambria Math",
            FontSizePt);

        relationLayout.Metrics.Width.Should().BeGreaterThan(binaryLayout.Metrics.Width,
            "simple relation operators such as = should use a distinct deterministic spacing class");
    }

    [Fact]
    public void Row_TransparentZeroWidthLargeOperatorPhantom_AddsSharedSpacingAdvance()
    {
        var transparentRow = new MathNode.Row(new MathNode[]
        {
            Run("a"),
            new MathNode.Phantom(Run("\u2211"), show: false, zeroWidth: true, transparentSpacing: true),
            Run("b")
        });
        var packedRow = new MathNode.Row(new MathNode[]
        {
            Run("a"),
            new MathNode.Phantom(Run("\u2211"), show: false, zeroWidth: true),
            Run("b")
        });

        var transparentLayout = MathLayoutEngine.Layout(transparentRow, "Cambria Math", FontSizePt);
        var packedLayout = MathLayoutEngine.Layout(packedRow, "Cambria Math", FontSizePt);

        var transparentContainer = Assert.IsType<MathBox.Container>(transparentLayout.Children[0]);
        var packedContainer = Assert.IsType<MathBox.Container>(packedLayout.Children[0]);

        transparentLayout.Metrics.Width.Should().BeGreaterThan(packedLayout.Metrics.Width,
            "m:transp should preserve deterministic spacing for simple large-operator phantom runs");
        transparentContainer.Children[2].X.Should().BeGreaterThan(packedContainer.Children[2].X,
            "the following sibling should advance past the transparent large-operator spacing");

        var ops = MathBoxRenderPlanner.Plan(transparentLayout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "a", "b" },
                "transparent large-operator spacing affects advance only and must not reintroduce hidden glyph draw ops");
    }

    [Fact]
    public void Row_TransparentZeroWidthPunctuationPhantom_AddsAfterSpacingOnly()
    {
        var punctuationLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("a"),
                new MathNode.Phantom(Run(";"), show: false, zeroWidth: true, transparentSpacing: true),
                Run("b")
            }),
            "Cambria Math",
            FontSizePt);
        var packedLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("a"),
                new MathNode.Phantom(Run(";"), show: false, zeroWidth: true),
                Run("b")
            }),
            "Cambria Math",
            FontSizePt);
        var binaryLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("a"),
                new MathNode.Phantom(Run("+"), show: false, zeroWidth: true, transparentSpacing: true),
                Run("b")
            }),
            "Cambria Math",
            FontSizePt);

        punctuationLayout.Metrics.Width.Should().BeGreaterThan(packedLayout.Metrics.Width,
            "punctuation-class m:transp should still advance following content");
        punctuationLayout.Metrics.Width.Should().BeLessThan(binaryLayout.Metrics.Width,
            "the punctuation class is intentionally directional and narrower than symmetric binary spacing");
    }

    [Fact]
    public void Row_TransparentZeroWidthPhantomNonOperator_DoesNotAddOperatorSpacing()
    {
        var transparentLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("x"),
                new MathNode.Phantom(Run("hidden"), show: false, zeroWidth: true, transparentSpacing: true),
                Run("y")
            }),
            "Cambria Math",
            FontSizePt);
        var packedLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("x"),
                new MathNode.Phantom(Run("hidden"), show: false, zeroWidth: true),
                Run("y")
            }),
            "Cambria Math",
            FontSizePt);

        transparentLayout.Metrics.Width.Should().BeApproximately(packedLayout.Metrics.Width, 0.01,
            "m:transp is consumed only for simple spacing-class phantom runs in this bounded slice");
    }

    [Fact]
    public void Row_TransparentZeroWidthPhantomMultiGlyphRelation_AddsSharedSpacingAdvanceWithoutGlyph()
    {
        var transparentLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("x"),
                new MathNode.Phantom(Run("->"), show: false, zeroWidth: true, transparentSpacing: true),
                Run("y")
            }),
            "Cambria Math",
            FontSizePt);
        var packedLayout = MathLayoutEngine.Layout(
            new MathNode.Row(new MathNode[]
            {
                Run("x"),
                new MathNode.Phantom(Run("->"), show: false, zeroWidth: true),
                Run("y")
            }),
            "Cambria Math",
            FontSizePt);

        transparentLayout.Metrics.Width.Should().BeGreaterThan(packedLayout.Metrics.Width,
            "common multi-glyph relation operators should use the same shared operator-class spacing as m:boxPr/m:opEmu");

        var transparentContainer = Assert.IsType<MathBox.Container>(transparentLayout.Children[0]);
        var packedContainer = Assert.IsType<MathBox.Container>(packedLayout.Children[0]);
        transparentContainer.Children[2].X.Should().BeGreaterThan(packedContainer.Children[2].X,
            "the following sibling should advance past the transparent multi-glyph relation spacing");

        var ops = MathBoxRenderPlanner.Plan(transparentLayout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "x", "y" },
                "transparent phantom spacing affects advance only and must not reintroduce hidden relation glyphs");
    }

    [Fact]
    public void BorderBox_EmitsVisibleSideLinesAndPadsNestedChild()
    {
        var child = new MathNode.Frac(Run("1"), new MathNode.Rad(null, Run("x")));
        var node = new MathNode.BorderBox(
            child,
            showTop: true,
            showBottom: false,
            showLeft: true,
            showRight: false);
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = Assert.IsType<MathBox.Container>(layout.Children[0]);
        var childBox = container.Children[0];
        var borderLines = container.Children.OfType<MathBox.Line>().ToList();

        borderLines.Should().HaveCount(2, "only the visible top and left border sides should produce line primitives");
        childBox.X.Should().BeGreaterThan(0, "borderBox must pad the nested child from the border");
        childBox.Y.Should().BeGreaterThan(0, "borderBox must pad the nested child from the border");
        container.Metrics.Width.Should().BeGreaterThan(childBox.Metrics.Width);
        container.Metrics.Height.Should().BeGreaterThan(childBox.Metrics.Height);
        container.Metrics.Ascent.Should().BeApproximately(childBox.Y + childBox.Metrics.Ascent, 0.01,
            "the borderBox baseline should remain the nested child's baseline after padding");

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawLine>().Should().HaveCount(2);
        ops.OfType<MathDrawOp.DrawLine>().Should().ContainSingle(line =>
                Math.Abs(line.Y1 - line.Y2) < 0.01 && line.X2 > line.X1,
            "the visible top side should produce exactly one horizontal line");
        ops.OfType<MathDrawOp.DrawLine>().Should().ContainSingle(line =>
                Math.Abs(line.X1 - line.X2) < 0.01 && line.Y2 > line.Y1,
            "the visible left side should produce exactly one vertical line");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "1", "x" });
    }

    [Fact]
    public void BorderBox_EmitsStrikeAndDiagonalLinesThroughBoxCenter()
    {
        var node = new MathNode.BorderBox(
            Run("x"),
            showTop: false,
            showBottom: false,
            showLeft: false,
            showRight: false,
            strikeHorizontal: true,
            strikeVertical: true,
            strikeBottomLeftToTopRight: true,
            strikeTopLeftToBottomRight: true);
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = Assert.IsType<MathBox.Container>(layout.Children[0]);
        var strikeLines = container.Children.OfType<MathBox.Line>().ToList();
        double centerX = container.Metrics.Width / 2.0;
        double centerY = container.Metrics.Height / 2.0;

        strikeLines.Should().HaveCount(4, "each borderBox strike flag should produce one renderer-neutral line");
        strikeLines.Should().ContainSingle(line =>
                Math.Abs(line.Y - centerY) < 0.01 && Math.Abs(line.Y2) < 0.01 && line.X2 > 0,
            "m:strikeH spans left-right through the box center");
        strikeLines.Should().ContainSingle(line =>
                Math.Abs(line.X - centerX) < 0.01 && Math.Abs(line.X2) < 0.01 && line.Y2 > 0,
            "m:strikeV spans top-bottom through the box center");
        strikeLines.Should().ContainSingle(line =>
                line.X2 > 0 && line.Y2 < 0,
            "m:strikeBLTR runs from bottom-left to top-right");
        strikeLines.Should().ContainSingle(line =>
                line.X2 > 0 && line.Y2 > 0,
            "m:strikeTLBR runs from top-left to bottom-right");

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawLine>().Should().HaveCount(4);
    }

    [Fact]
    public void BorderBox_HiddenHorizontalEdgesAndDiagonalStrike_EmitExactSharedEndpoints()
    {
        var node = ParseOmml(
            "<m:borderBox>" +
            "<m:borderBoxPr><m:hideTop/><m:hideBot/><m:strikeTLBR/></m:borderBoxPr>" +
            "<m:e><m:f><m:num><m:r><m:t>1</m:t></m:r></m:num><m:den><m:r><m:t>x</m:t></m:r></m:den></m:f></m:e>" +
            "</m:borderBox>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = Assert.IsType<MathBox.Container>(layout.Children[0]);
        var childBox = container.Children[0];
        var lines = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawLine>()
            .ToList();

        lines.Should().HaveCount(3,
            "hidden top/bottom edges leave only the left/right border edges plus the requested TLBR strike");
        lines.Should().NotContain(line => Math.Abs(line.Y1 - line.Y2) < 0.01,
            "m:hideTop and m:hideBot must suppress horizontal border-edge draw ops");

        var left = lines.Single(line => Math.Abs(line.X1 - line.X2) < 0.01 && line.X1 < 10 + container.Metrics.Width / 2.0);
        var right = lines.Single(line => Math.Abs(line.X1 - line.X2) < 0.01 && line.X1 > 10 + container.Metrics.Width / 2.0);
        var diagonal = lines.Single(line => Math.Abs(line.X1 - line.X2) > 0.01 && Math.Abs(line.Y1 - line.Y2) > 0.01);

        left.Y1.Should().BeApproximately(diagonal.Y1, 0.01);
        right.Y1.Should().BeApproximately(diagonal.Y1, 0.01);
        left.Y2.Should().BeApproximately(diagonal.Y2, 0.01);
        right.Y2.Should().BeApproximately(diagonal.Y2, 0.01);
        diagonal.X1.Should().BeApproximately(left.X1, 0.01);
        diagonal.X2.Should().BeApproximately(right.X1, 0.01);
        diagonal.Y2.Should().BeGreaterThan(diagonal.Y1,
            "m:strikeTLBR runs from the top-left border endpoint to the bottom-right endpoint");
        childBox.X.Should().BeGreaterThan(0);
        childBox.Y.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GroupChr_Above_PlacesBraceAboveBaseAndGrowsAscent()
    {
        var node = new MathNode.GroupChr("\u23DE", Run("x"), isAbove: true);
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = Assert.IsType<MathBox.Container>(layout.Children[0]);
        var groupGlyph = Assert.IsType<MathBox.Glyph>(container.Children[0]);
        var baseGlyph = Assert.IsType<MathBox.Glyph>(container.Children[1]);

        groupGlyph.Text.Should().Be("\u23DE");
        groupGlyph.Y.Should().BeLessThan(baseGlyph.Y,
            "top group characters must sit above the grouped expression");
        layout.Metrics.Ascent.Should().BeGreaterThan(baseGlyph.Metrics.Ascent,
            "the reported baseline must include the raised group character");

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).Should().Contain(new[] { "\u23DE", "x" });
    }

    [Fact]
    public void GroupChr_Below_PlacesBraceBelowBaseAndKeepsBaseBaseline()
    {
        var node = new MathNode.GroupChr("\u23DF", Run("x"), isAbove: false);
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = Assert.IsType<MathBox.Container>(layout.Children[0]);
        var groupGlyph = Assert.IsType<MathBox.Glyph>(container.Children[0]);
        var baseGlyph = Assert.IsType<MathBox.Glyph>(container.Children[1]);

        groupGlyph.Text.Should().Be("\u23DF");
        groupGlyph.Y.Should().BeGreaterThan(baseGlyph.Y,
            "bottom group characters must sit below the grouped expression");
        layout.Metrics.Ascent.Should().BeApproximately(baseGlyph.Metrics.Ascent, 0.01,
            "bottom group characters grow descent while preserving the base baseline");
    }

    [Fact]
    public void GroupChr_WithWideBase_GrowsBraceGlyphTowardBaseWidth()
    {
        var narrowLayout = MathLayoutEngine.Layout(
            new MathNode.GroupChr("\u23DE", Run("x"), isAbove: true),
            "Cambria Math",
            FontSizePt);
        var wideLayout = MathLayoutEngine.Layout(
            new MathNode.GroupChr(
                "\u23DE",
                new MathNode.Row(new MathNode[] { Run("x"), Run("+"), Run("y") }),
                isAbove: true),
            "Cambria Math",
            FontSizePt);

        var narrowContainer = Assert.IsType<MathBox.Container>(narrowLayout.Children[0]);
        var wideContainer = Assert.IsType<MathBox.Container>(wideLayout.Children[0]);
        var narrowGlyph = Assert.IsType<MathBox.Glyph>(narrowContainer.Children[0]);
        var wideGlyph = Assert.IsType<MathBox.Glyph>(wideContainer.Children[0]);
        var wideBase = wideContainer.Children[1];

        wideGlyph.Text.Should().Be("\u23DE");
        wideGlyph.FontSizePt.Should().BeGreaterThan(narrowGlyph.FontSizePt,
            "group-character glyph growth must be resolved in the shared layout, not separately in WPF/Avalonia");
        wideGlyph.Metrics.Width.Should().BeGreaterThan(narrowGlyph.Metrics.Width);
        wideGlyph.Metrics.Width.Should().BeGreaterThan(wideBase.Metrics.Width * 0.75,
            "the brace should grow toward the grouped expression width instead of staying at ordinary run size");

        var op = MathBoxRenderPlanner.Plan(wideLayout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single(g => g.Text == "\u23DE");
        op.FontSizePt.Should().Be(wideGlyph.FontSizePt,
            "the renderer-neutral draw plan must carry the grown brace size to both hosts");
    }

    [Fact]
    public void OmmlGroupChrVertJcTop_AlignsObjectTopToSharedBaseline()
    {
        var layout = MathLayoutEngine.Layout(
            ParseOmml("<m:groupChr><m:groupChrPr><m:vertJc m:val=\"top\"/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>"),
            "Cambria Math",
            FontSizePt);

        layout.Metrics.Ascent.Should().BeApproximately(0, 0.01,
            "m:groupChrPr/m:vertJc=top aligns the group-character object's top to the shared baseline");

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "\u23DE", "x" },
                "vertical justification changes shared baseline metrics without adding renderer-local draw ops");
    }

    [Fact]
    public void OmmlGroupChrVertJcBottom_AlignsObjectBottomToSharedBaseline()
    {
        var layout = MathLayoutEngine.Layout(
            ParseOmml("<m:groupChr><m:groupChrPr><m:pos m:val=\"bot\"/><m:vertJc/></m:groupChrPr><m:e><m:r><m:t>x</m:t></m:r></m:e></m:groupChr>"),
            "Cambria Math",
            FontSizePt);

        layout.Metrics.Ascent.Should().BeApproximately(layout.Metrics.Height, 0.01,
            "a present m:vertJc without m:val defaults to bot and aligns the object bottom to the shared baseline");

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text)
            .Should().Equal(new[] { "\u23DF", "x" },
                "both WPF and Avalonia consume the same underbrace glyph plan and bottom-aligned metrics");
    }

    [Fact]
    public void LimitLow_CentersLimitBelowBase_AndGrowsDescent()
    {
        var node = new MathNode.Limit(Run("lim"), Run("0"), isUpper: false);
        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var baseBox = container.Children[0];
        var limitBox = container.Children[1];

        limitBox.Y.Should().BeGreaterThan(baseBox.Y + baseBox.Metrics.Height,
            "m:limLow places the limit below the base expression");
        box.Metrics.Ascent.Should().BeApproximately(baseBox.Metrics.Ascent, 0.01,
            "a lower limit keeps the base baseline fixed and grows the descent");
        (limitBox.X + limitBox.Metrics.Width / 2.0)
            .Should().BeApproximately(baseBox.X + baseBox.Metrics.Width / 2.0, 0.01,
                "PowerPoint centers lower limits under their base expression");
    }

    [Fact]
    public void LimitUpp_CentersLimitAboveBase_AndGrowsAscent()
    {
        var node = new MathNode.Limit(Run("max"), Run("S"), isUpper: true);
        var box = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        var limitBox = container.Children[0];
        var baseBox = container.Children[1];

        baseBox.Y.Should().BeGreaterThan(limitBox.Y + limitBox.Metrics.Height,
            "m:limUpp places the limit above the base expression");
        box.Metrics.Ascent.Should().BeGreaterThan(baseBox.Metrics.Ascent,
            "an upper limit must grow ascent so the raised limit is not clipped");
        (baseBox.Y + baseBox.Metrics.Ascent).Should().BeApproximately(box.Metrics.Ascent, 0.01,
            "the base expression baseline must remain the reported container baseline");
        (limitBox.X + limitBox.Metrics.Width / 2.0)
            .Should().BeApproximately(baseBox.X + baseBox.Metrics.Width / 2.0, 0.01,
                "PowerPoint centers upper limits over their base expression");
    }

    [Fact]
    public void LimitLow_RenderPlanner_EmitsBaseAndLimitGlyphs()
    {
        var node = new MathNode.Limit(Run("lim"), Run("x->0"), isUpper: false);
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math");
        var glyphTexts = ops.OfType<MathDrawOp.DrawGlyph>().Select(g => g.Text).ToList();

        glyphTexts.Should().Contain("lim");
        glyphTexts.Should().Contain("x->0");
    }

    [Fact]
    public void OmmlLimitUpperAndLower_RenderPlanCarriesCenteredReducedLimitGlyphs()
    {
        var lowerLayout = MathLayoutEngine.Layout(
            ParseOmml("<m:limLow><m:e><m:r><m:t>lim</m:t></m:r></m:e><m:lim><m:r><m:t>x->0</m:t></m:r></m:lim></m:limLow>"),
            "Cambria Math",
            FontSizePt);
        var upperLayout = MathLayoutEngine.Layout(
            ParseOmml("<m:limUpp><m:e><m:r><m:t>max</m:t></m:r></m:e><m:lim><m:r><m:t>S</m:t></m:r></m:lim></m:limUpp>"),
            "Cambria Math",
            FontSizePt);

        var lowerGlyphs = MathBoxRenderPlanner.Plan(lowerLayout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();
        var upperGlyphs = MathBoxRenderPlanner.Plan(upperLayout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        lowerGlyphs.Select(g => g.Text).Should().Equal(new[] { "lim", "x->0" });
        lowerGlyphs[1].Y.Should().BeGreaterThan(lowerGlyphs[0].Y,
            "m:limLow draws the reduced limit below the base in the shared plan");
        lowerGlyphs[1].FontSizePt.Should().BeLessThan(lowerGlyphs[0].FontSizePt,
            "limit text is reduced before WPF or Avalonia consumes the plan");

        upperGlyphs.Select(g => g.Text).Should().Equal(new[] { "S", "max" });
        upperGlyphs[0].Y.Should().BeLessThan(upperGlyphs[1].Y,
            "m:limUpp draws the reduced limit above the base in the shared plan");
        upperGlyphs[0].FontSizePt.Should().BeLessThan(upperGlyphs[1].FontSizePt,
            "upper limit text is reduced before renderer-specific drawing");
    }

    [Fact]
    public void OmmlParagraphJustification_RightAlignsContentInsideSharedParagraphWidth()
    {
        var node = ParseOmmlParagraph(
            "<m:oMathParaPr><m:jc m:val=\"right\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");
        var natural = MathLayoutEngine.Layout(((MathNode.MathParagraph)node).Content, "Cambria Math", FontSizePt);
        var aligned = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt, paragraphWidthDip: 180);

        aligned.Metrics.Width.Should().BeApproximately(180, 0.01);

        var glyph = MathBoxRenderPlanner.Plan(aligned, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        glyph.Text.Should().Be("x");
        glyph.X.Should().BeApproximately(10 + 180 - natural.Metrics.Width, 0.01,
            "m:oMathParaPr/m:jc=right should shift glyph coordinates in the shared plan before any host draws");
    }

    [Fact]
    public void OmmlParagraphJustification_CenterGroupUsesCenteredSharedParagraphPlan()
    {
        var node = ParseOmmlParagraph(
            "<m:oMathParaPr><m:jc m:val=\"centerGroup\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>xy</m:t></m:r></m:oMath>");
        var natural = MathLayoutEngine.Layout(((MathNode.MathParagraph)node).Content, "Cambria Math", FontSizePt);
        var aligned = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt, paragraphWidthDip: 200);

        var glyph = MathBoxRenderPlanner.Plan(aligned, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        glyph.Text.Should().Be("xy");
        glyph.X.Should().BeApproximately(10 + (200 - natural.Metrics.Width) / 2.0, 0.01,
            "bounded centerGroup support uses the same centered renderer-neutral coordinates as centered equation paragraphs");
    }

    [Fact]
    public void OmmlParagraphDefJc_RightAlignsContentWhenLocalJcIsAbsent()
    {
        var node = ParseOmmlParagraph(
            "<m:mathPr><m:dispDef/><m:defJc m:val=\"right\"/></m:mathPr>" +
            "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath>");
        var natural = MathLayoutEngine.Layout(((MathNode.MathParagraph)node).Content, "Cambria Math", FontSizePt);
        var aligned = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt, paragraphWidthDip: 180);

        var glyph = MathBoxRenderPlanner.Plan(aligned, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        Assert.Equal(MathNode.MathParagraphJustification.Right,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
        glyph.X.Should().BeApproximately(10 + 180 - natural.Metrics.Width, 0.01);
    }

    [Fact]
    public void OmmlParagraphBareDefJc_UsesCenterGroupPlanWithoutChangingCenteredGeometry()
    {
        var node = ParseOmmlParagraph(
            "<m:mathPr><m:dispDef/><m:defJc/></m:mathPr>" +
            "<m:oMath><m:r><m:t>xy</m:t></m:r></m:oMath>");

        var natural = MathLayoutEngine.Layout(((MathNode.MathParagraph)node).Content, "Cambria Math", FontSizePt);
        var aligned = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt, paragraphWidthDip: 200);
        var glyph = MathBoxRenderPlanner.Plan(aligned, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .Single();

        Assert.Equal(MathNode.MathParagraphJustification.CenterGroup,
            Assert.IsType<MathNode.MathParagraph>(node).Justification);
        glyph.X.Should().BeApproximately(10 + (200 - natural.Metrics.Width) / 2.0, 0.01);
    }

    [Fact]
    public void OmmlParagraphMathFont_UsesEquationWideFontInSharedGlyphPlan()
    {
        var node = ParseOmmlParagraph(
            "<m:mathPr><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
            "<m:oMath><m:f><m:num><m:r><m:t>x</m:t></m:r></m:num>" +
            "<m:den><m:r><m:t>y</m:t></m:r></m:den></m:f></m:oMath>");

        var glyphs = MathBoxRenderPlanner.Plan(
                MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt),
                10,
                20,
                SrgbColor.Black,
                "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Should().NotBeEmpty();
        glyphs.Should().OnlyContain(glyph => glyph.FontFamily == "Arial",
            "m:mathPr/m:mathFont must be resolved before the shared layout creates glyph boxes");
    }

    [Fact]
    public void OmmlDocumentMathProperties_InheritAndOverrideBeforeSharedLayout()
    {
        var xml = $"<a:graphicData xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                  $"xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\" xmlns:m=\"{M}\">" +
                  "<m:mathPr><m:mathFont m:val=\"Arial\"/></m:mathPr>" +
                  "<a14:m><m:oMathPara><m:mathPr><m:brkBin m:val=\"repeat\"/></m:mathPr>" +
                  "<m:oMath><m:r><m:t>x</m:t></m:r></m:oMath></m:oMathPara></a14:m></a:graphicData>";
        var node = OmmlParser.Parse(xml, fallbackText: "FALLBACK");

        var glyphs = MathBoxRenderPlanner.Plan(
                MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt),
                10,
                20,
                SrgbColor.Black,
                "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Should().ContainSingle();
        glyphs[0].FontFamily.Should().Be("Arial",
            "document mathPr defaults must flow through the shared parser/model/layout path");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void OmmlParagraphBinaryBreak_WrapsAtOperatorThroughSharedRenderPlan(
        string breakValue)
    {
        var node = ParseOmmlParagraph(
            $"<m:oMathParaPr><m:brkBin m:val=\"{breakValue}\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>a</m:t></m:r><m:r><m:t>+</m:t></m:r><m:r><m:t>b</m:t></m:r></m:oMath>");

        var glyphs = MathBoxRenderPlanner.Plan(
                MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt, paragraphWidthDip: 30),
                10,
                20,
                SrgbColor.Black,
                "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal("a", "+", "b");
        if (breakValue == "before")
        {
            glyphs[1].Y.Should().BeGreaterThan(glyphs[0].Y,
                "before places the binary operator at the start of the wrapped line");
            glyphs[2].Y.Should().Be(glyphs[1].Y);
        }
        else
        {
            glyphs[1].Y.Should().Be(glyphs[0].Y,
                "after keeps the binary operator at the end of the first line");
            glyphs[2].Y.Should().BeGreaterThan(glyphs[1].Y);
        }
    }

    [Fact]
    public void OmmlParagraphBinaryBreak_RepeatUsesConfiguredSubtractionSigns()
    {
        var node = ParseOmmlParagraph(
            "<m:oMathParaPr><m:brkBin m:val=\"repeat\"/><m:brkBinSub m:val=\"+-\"/></m:oMathParaPr>" +
            "<m:oMath><m:r><m:t>a</m:t></m:r><m:r><m:t>-</m:t></m:r><m:r><m:t>b</m:t></m:r></m:oMath>");

        var glyphs = MathBoxRenderPlanner.Plan(
                MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt, paragraphWidthDip: 30),
                10,
                20,
                SrgbColor.Black,
                "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        glyphs.Select(g => g.Text).Should().Equal("a", "+", "-", "b");
        glyphs[1].Y.Should().Be(glyphs[0].Y,
            "repeat keeps the configured plus sign at the end of the first line");
        glyphs[2].Y.Should().BeGreaterThan(glyphs[1].Y);
    }

    [Fact]
    public void Func_FunctionName_RenderPlanIsUprightAndArgumentStaysItalic()
    {
        var node = ParseOmml(
            "<m:func>" +
            "<m:fName><m:r><m:t>sin</m:t></m:r></m:fName>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:func>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        ops.Should().HaveCount(2);
        ops[0].Text.Should().Be("sin");
        ops[0].IsItalic.Should().BeFalse("m:func/m:fName names are function operators, not math variables");
        ops[1].Text.Should().Be("x");
        ops[1].IsItalic.Should().BeTrue("the function argument keeps ordinary math-run styling");
        ops[1].X.Should().BeGreaterThan(ops[0].X + ops[0].Text.Length,
            "the existing shared function layout keeps a visible advance between name and argument");
    }

    [Fact]
    public void OmmlScriptedFunctionName_RenderPlanKeepsBaseNameUpright()
    {
        var node = ParseOmml(
            "<m:func>" +
            "<m:fName><m:sSup><m:e><m:r><m:t>sin</m:t></m:r></m:e><m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup></m:fName>" +
            "<m:e><m:r><m:t>x</m:t></m:r></m:e>" +
            "</m:func>");
        var layout = MathLayoutEngine.Layout(node, "Cambria Math", FontSizePt);

        var ops = MathBoxRenderPlanner.Plan(layout, 10, 20, SrgbColor.Black, "Cambria Math")
            .OfType<MathDrawOp.DrawGlyph>()
            .ToList();

        ops.Select(g => g.Text).Should().Equal(new[] { "sin", "2", "x" });
        ops[0].IsItalic.Should().BeFalse(
            "m:func/m:fName can contain scripted function names such as sin^2, and the base name must become an upright operator in the shared draw plan");
        ops[2].IsItalic.Should().BeTrue("the function argument keeps ordinary math-run styling");
        ops[1].Y.Should().BeLessThan(ops[0].Y,
            "the existing shared superscript layout should keep the exponent raised inside the function-name object");
        ops[2].X.Should().BeGreaterThan(ops[1].X,
            "the applied argument must remain to the right of the complete scripted function-name object");
    }
}
