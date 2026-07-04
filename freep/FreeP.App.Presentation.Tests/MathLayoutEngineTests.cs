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

    private static MathNode Run(string text, bool isItalic = true) => new MathNode.Run(text, isItalic);

    private static MathNode TallFraction() =>
        new MathNode.Frac(Run("1"), Run("x"));

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

    // ── HA6: m:f fPr/type — fraction bar style ──────────────────────────────

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
    public void Frac_DefaultBarType_RendersHRule_Unchanged()
    {
        var frac = new MathNode.Frac(Run("1"), Run("2")); // default FracType.Bar
        var box = MathLayoutEngine.Layout(frac, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        container.Children.Should().ContainSingle(b => b is MathBox.HRule,
            "the default bar fraction must still draw exactly one HRule (no regression)");
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

        var glyphs = AllGlyphs(container).Cast<MathBox.Glyph>().ToList();
        glyphs.Should().Contain(g => g.Text == "/", "the linear form must include a slash glyph");
    }

    [Fact]
    public void Frac_SkewedType_DoesNotRenderAsBarFraction()
    {
        // HA6: full skew layout isn't implemented; at minimum it must not fall back
        // to the bar-fraction rendering (approximated as the linear a/b form instead).
        var frac = new MathNode.Frac(Run("a"), Run("b"), MathNode.FracType.Skewed);
        var box = MathLayoutEngine.Layout(frac, "Cambria Math", FontSizePt);

        var container = (MathBox.Container)box.Children[0];
        container.Children.Should().NotContain(b => b is MathBox.HRule,
            "skw must never render as a bar fraction");
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
}
