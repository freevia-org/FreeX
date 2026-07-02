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
}
