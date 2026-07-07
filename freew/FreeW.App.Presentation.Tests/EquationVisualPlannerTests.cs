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
    public void EquationVisualPlanner_Fraction_BuildsStructuredElementAndDisplaySegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.Fraction("a + b", "c")]));

        plan.LinearText.Should().Be("a + b/c");
        plan.Elements.Should().ContainSingle();
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.Fraction);
        plan.Elements[0].LinearText.Should().Be("a + b/c");
        plan.Elements[0].Numerator.Should().Be("a + b");
        plan.Elements[0].Denominator.Should().Be("c");
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "a + b",
            EquationVisualPlanner.FractionBarText,
            "c");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.FractionNumerator,
            EquationVisualSegmentRole.FractionBar,
            EquationVisualSegmentRole.FractionDenominator);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_Radical_BuildsStructuredElementAndDisplaySegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.Radical("x + 1", "3")]));

        plan.LinearText.Should().Be("3√(x + 1)");
        plan.Elements.Should().ContainSingle();
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.Radical);
        plan.Elements[0].LinearText.Should().Be("3√(x + 1)");
        plan.Elements[0].Radicand.Should().Be("x + 1");
        plan.Elements[0].Degree.Should().Be("3");
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "3",
            EquationVisualPlanner.RadicalSignText,
            "x + 1");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.RadicalDegree,
            EquationVisualSegmentRole.RadicalSign,
            EquationVisualSegmentRole.RadicalRadicand);
        plan.Segments[0].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_SquareRadical_OmitsDegreeSegment()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.Radical("x")]));

        plan.LinearText.Should().Be("√(x)");
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.Radical);
        plan.Elements[0].Degree.Should().BeEmpty();
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            EquationVisualPlanner.RadicalSignText,
            "x");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.RadicalSign,
            EquationVisualSegmentRole.RadicalRadicand);
    }

    [Fact]
    public void EquationVisualPlanner_UnsupportedStructuredKinds_RetainLinearFallbackSegments()
    {
        var runs = new[]
        {
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
            plan.Elements.Should().ContainSingle();
            plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.Segments);
        }
    }
}
