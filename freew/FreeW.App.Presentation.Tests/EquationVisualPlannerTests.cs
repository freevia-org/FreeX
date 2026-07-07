using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class EquationVisualPlannerTests
{
    [Fact]
    public void EquationVisualPlanner_TextRun_BuildsSingleMathTextSegment()
    {
        var plan = EquationVisualPlanner.Build(Equation.FromText("x + y"));

        plan.LinearText.Should().Be("x + y");
        plan.MathFontFamily.Should().Contain("Cambria Math");
        plan.Italic.Should().BeTrue();
        plan.Segments.Should().ContainSingle();
        plan.Segments[0].Text.Should().Be("x + y");
        plan.Segments[0].Role.Should().Be(EquationVisualSegmentRole.Text);
        plan.Segments[0].Style.FontSizeScale.Should().Be(1.0);
        plan.Segments[0].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Normal);
    }

    [Fact]
    public void EquationVisualPlanner_Superscript_SplitsBaseAndRaisedScriptWithoutCaretGlyph()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.Superscript("c", "2")]));

        plan.LinearText.Should().Be("c^2");
        plan.Segments.Select(s => s.Text).Should().Equal("c", "2");
        plan.Segments.Select(s => s.Role).Should().Equal(
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Superscript);
        plan.Segments[1].Style.FontSizeScale.Should().Be(EquationVisualPlanner.ScriptFontSizeScale);
        plan.Segments[1].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
        plan.Segments[1].Style.BaselineOffsetEm.Should().BePositive();
    }

    [Fact]
    public void EquationVisualPlanner_Subscript_SplitsBaseAndLoweredScriptWithoutUnderscoreGlyph()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.Subscript("x", "i")]));

        plan.LinearText.Should().Be("x_i");
        plan.Segments.Select(s => s.Text).Should().Equal("x", "i");
        plan.Segments.Select(s => s.Role).Should().Equal(
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Subscript);
        plan.Segments[1].Style.FontSizeScale.Should().Be(EquationVisualPlanner.ScriptFontSizeScale);
        plan.Segments[1].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Subscript);
        plan.Segments[1].Style.BaselineOffsetEm.Should().BeNegative();
    }

    [Fact]
    public void EquationVisualPlanner_SubSuperscript_SplitsBaseSubAndSupWithDistinctRoles()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.SubSuperscript("x", "i", "n")]));

        plan.LinearText.Should().Be("x_i^n");
        plan.Segments.Select(s => s.Text).Should().Equal("x", "i", "n");
        plan.Segments.Select(s => s.Role).Should().Equal(
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Subscript,
            EquationVisualSegmentRole.Superscript);
        plan.Segments[1].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Subscript);
        plan.Segments[2].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
    }

    [Fact]
    public void EquationVisualPlanner_NonScriptKinds_RetainLinearFallbackSegments()
    {
        var runs = new[]
        {
            MathRun.Fraction("a", "b"),
            MathRun.Radical("x", "3"),
            MathRun.NAry("SUM", "i=1", "n", "i"),
            MathRun.AccentOf("x", "hat"),
            MathRun.BarOf("x"),
            MathRun.Delimiter("x + y", "[", "]"),
            MathRun.MatrixOf(MathMatrix.Identity2x2()),
            MathRun.FunctionApply("sin", "x"),
            MathRun.GroupCharOf("x", "over", "top")
        };

        foreach (var run in runs)
        {
            var plan = EquationVisualPlanner.Build(new Equation([run]));

            plan.LinearText.Should().Be(run.LinearText);
            plan.Segments.Should().ContainSingle();
            plan.Segments[0].Text.Should().Be(run.LinearText);
            plan.Segments[0].Role.Should().Be(EquationVisualSegmentRole.LinearFallback);
            plan.Segments[0].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Normal);
        }
    }
}
