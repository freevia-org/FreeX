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
    public void EquationVisualPlanner_NAry_BuildsStructuredLargeOperatorElement()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.NAry("\u2211", "i=1", "n", "i")]));

        plan.LinearText.Should().Be("\u2211(i=1..n) i");
        plan.Elements.Should().ContainSingle();
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.NAry);
        plan.Elements[0].LinearText.Should().Be("\u2211(i=1..n) i");
        plan.Elements[0].Operator.Should().Be("\u2211");
        plan.Elements[0].LowerLimit.Should().Be("i=1");
        plan.Elements[0].UpperLimit.Should().Be("n");
        plan.Elements[0].Operand.Should().Be("i");
        plan.Segments.Select(segment => segment.Text).Should().Equal("\u2211", "i=1", "n", "i");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.NAryOperator,
            EquationVisualSegmentRole.NAryLowerLimit,
            EquationVisualSegmentRole.NAryUpperLimit,
            EquationVisualSegmentRole.NAryOperand);
        plan.Segments[0].Style.FontSizeScale.Should().Be(EquationVisualPlanner.LargeOperatorFontSizeScale);
        plan.Segments[0].Style.Italic.Should().BeFalse();
        plan.Segments[1].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Subscript);
        plan.Segments[2].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
        plan.Segments[3].Style.FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_Matrix_BuildsStructuredRowsCellsAndDisplaySegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())]));

        plan.LinearText.Should().Be("[1, 0; 0, 1]");
        plan.Elements.Should().ContainSingle();
        var element = plan.Elements[0];
        element.Kind.Should().Be(EquationVisualElementKind.Matrix);
        element.LinearText.Should().Be("[1, 0; 0, 1]");
        element.MatrixRowCount.Should().Be(2);
        element.MatrixColumnCount.Should().Be(2);
        element.MatrixRows.Select(row => row.RowIndex).Should().Equal(0, 1);
        element.MatrixRows[0].Cells.Select(cell => (cell.RowIndex, cell.ColumnIndex, cell.Text))
            .Should().Equal((0, 0, "1"), (0, 1, "0"));
        element.MatrixRows[1].Cells.Select(cell => (cell.RowIndex, cell.ColumnIndex, cell.Text))
            .Should().Equal((1, 0, "0"), (1, 1, "1"));
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            EquationVisualPlanner.MatrixOpenDelimiterText,
            "1",
            EquationVisualPlanner.MatrixColumnSeparatorText,
            "0",
            EquationVisualPlanner.MatrixRowSeparatorText,
            "0",
            EquationVisualPlanner.MatrixColumnSeparatorText,
            "1",
            EquationVisualPlanner.MatrixCloseDelimiterText);
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.MatrixOpenDelimiter,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixColumnSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixRowSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixColumnSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixCloseDelimiter);
        plan.Segments.Where(segment => segment.Role == EquationVisualSegmentRole.MatrixCell)
            .Should().OnlyContain(segment => segment.Style.FontSizeScale == EquationVisualPlanner.StructureFontSizeScale);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_UnsupportedStructuredKinds_RetainLinearFallbackSegments()
    {
        var runs = new[]
        {
            MathRun.AccentOf("x", "hat"),
            MathRun.BarOf("x"),
            MathRun.Delimiter("x + y", "[", "]"),
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
