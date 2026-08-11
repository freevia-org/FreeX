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
    public void EquationVisualPlanner_NestedScriptSlots_SurfaceSharedSlotPlansAndKeepFlattenedSegments()
    {
        var baseEquation = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Subscript("x", "1")
        ]);
        var subEquation = new Equation([
            MathRun.PlainText("i+"),
            MathRun.Superscript("j", "2")
        ]);
        var supEquation = new Equation([
            MathRun.PlainText("n+"),
            MathRun.Subscript("k", "0")
        ]);

        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.Superscript(baseEquation, supEquation),
            MathRun.Subscript(baseEquation, subEquation),
            MathRun.SubSuperscript(baseEquation, subEquation, supEquation)
        ]));

        plan.LinearText.Should().Be("a+x_1^n+k_0a+x_1_i+j^2a+x_1_i+j^2^n+k_0");
        plan.Elements.Select(element => element.Kind).Should().Equal(
            EquationVisualElementKind.Segments,
            EquationVisualElementKind.Segments,
            EquationVisualElementKind.Segments);

        var superscript = plan.Elements[0];
        superscript.BaseText.Should().Be("a+x_1");
        superscript.ScriptSuperscriptText.Should().Be("n+k_0");
        superscript.ScriptBasePlan.Should().NotBeNull();
        superscript.ScriptBasePlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Subscript);
        superscript.ScriptSubscriptPlan.Should().BeNull();
        superscript.ScriptSuperscriptPlan.Should().NotBeNull();
        superscript.ScriptSuperscriptPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Subscript);

        var subscript = plan.Elements[1];
        subscript.BaseText.Should().Be("a+x_1");
        subscript.ScriptSubscriptText.Should().Be("i+j^2");
        subscript.ScriptBasePlan.Should().NotBeNull();
        subscript.ScriptSubscriptPlan.Should().NotBeNull();
        subscript.ScriptSubscriptPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);
        subscript.ScriptSuperscriptPlan.Should().BeNull();

        var subSuperscript = plan.Elements[2];
        subSuperscript.BaseText.Should().Be("a+x_1");
        subSuperscript.ScriptSubscriptText.Should().Be("i+j^2");
        subSuperscript.ScriptSuperscriptText.Should().Be("n+k_0");
        subSuperscript.ScriptBasePlan.Should().NotBeNull();
        subSuperscript.ScriptSubscriptPlan.Should().NotBeNull();
        subSuperscript.ScriptSuperscriptPlan.Should().NotBeNull();

        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "a+x_1",
            "n+k_0",
            "a+x_1",
            "i+j^2",
            "a+x_1",
            "i+j^2",
            "n+k_0");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Superscript,
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Subscript,
            EquationVisualSegmentRole.Base,
            EquationVisualSegmentRole.Subscript,
            EquationVisualSegmentRole.Superscript);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
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
    public void EquationVisualPlanner_NestedFractionSlots_SurfaceSharedSlotPlansAndKeepFlattenedSegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.Fraction(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                new Equation([
                    MathRun.PlainText("b+"),
                    MathRun.Subscript("y", "1")
                ]))
        ]));

        plan.LinearText.Should().Be("a+x^2/b+y_1");
        plan.Elements.Should().ContainSingle();
        var fraction = plan.Elements[0];
        fraction.Kind.Should().Be(EquationVisualElementKind.Fraction);
        fraction.Numerator.Should().Be("a+x^2");
        fraction.Denominator.Should().Be("b+y_1");
        fraction.NumeratorPlan.Should().NotBeNull();
        fraction.NumeratorPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);
        fraction.DenominatorPlan.Should().NotBeNull();
        fraction.DenominatorPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Subscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "a+x^2",
            EquationVisualPlanner.FractionBarText,
            "b+y_1");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.FractionNumerator,
            EquationVisualSegmentRole.FractionBar,
            EquationVisualSegmentRole.FractionDenominator);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedFractionSlots_AreDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Fraction,
            NumeratorEquation = equation,
            Denominator = "b"
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().EndWith("/b");
        plan.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public void EquationVisualPlanner_NestedRadicalRadicand_SurfacesSharedSlotPlanAndKeepsFlattenedSegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.Radical(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                "3")
        ]));

        plan.LinearText.Should().Be("3\u221a(a+x^2)");
        plan.Elements.Should().ContainSingle();
        var radical = plan.Elements[0];
        radical.Kind.Should().Be(EquationVisualElementKind.Radical);
        radical.Radicand.Should().Be("a+x^2");
        radical.Degree.Should().Be("3");
        radical.RadicandPlan.Should().NotBeNull();
        radical.RadicandPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "3",
            EquationVisualPlanner.RadicalSignText,
            "a+x^2");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.RadicalDegree,
            EquationVisualSegmentRole.RadicalSign,
            EquationVisualSegmentRole.RadicalRadicand);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedRadicalDegree_SurfacesSharedSlotPlanAndKeepsFlattenedSegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.Radical(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                new Equation([
                    MathRun.PlainText("n+"),
                    MathRun.Subscript("k", "1")
                ]))
        ]));

        plan.LinearText.Should().Be("n+k_1\u221a(a+x^2)");
        plan.Elements.Should().ContainSingle();
        var radical = plan.Elements[0];
        radical.Kind.Should().Be(EquationVisualElementKind.Radical);
        radical.Radicand.Should().Be("a+x^2");
        radical.Degree.Should().Be("n+k_1");
        radical.RadicandPlan.Should().NotBeNull();
        radical.DegreePlan.Should().NotBeNull();
        radical.DegreePlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Subscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "n+k_1",
            EquationVisualPlanner.RadicalSignText,
            "a+x^2");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.RadicalDegree,
            EquationVisualSegmentRole.RadicalSign,
            EquationVisualSegmentRole.RadicalRadicand);
        plan.Segments[0].Style.BaselineRole.Should().Be(EquationVisualBaselineRole.Superscript);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedRadicalRadicand_IsDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Radical,
            Base = "x",
            RadicandEquation = equation
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().Contain("\u221a(x)");
        plan.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public void EquationVisualPlanner_NestedRadicalDegree_IsDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Radical,
            Base = "x",
            Degree = "n",
            DegreeEquation = equation
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().Contain("n\u221a(x)");
        plan.Elements.Should().NotBeEmpty();
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
    public void EquationVisualPlanner_NestedNArySlots_SurfaceSharedSlotPlansAndKeepFlattenedSegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.NAry(
                "\u2211",
                new Equation([
                    MathRun.PlainText("i="),
                    MathRun.Subscript("j", "1")
                ]),
                new Equation([MathRun.Superscript("n", "2")]),
                new Equation([MathRun.Fraction("1", "i")]))
        ]));

        plan.LinearText.Should().Be("\u2211(i=j_1..n^2) 1/i");
        plan.Elements.Should().ContainSingle();
        var nary = plan.Elements[0];
        nary.Kind.Should().Be(EquationVisualElementKind.NAry);
        nary.Operator.Should().Be("\u2211");
        nary.LowerLimit.Should().Be("i=j_1");
        nary.UpperLimit.Should().Be("n^2");
        nary.Operand.Should().Be("1/i");
        nary.NAryLowerLimitPlan.Should().NotBeNull();
        nary.NAryLowerLimitPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Subscript);
        nary.NAryUpperLimitPlan.Should().NotBeNull();
        nary.NAryUpperLimitPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);
        nary.NAryOperandPlan.Should().NotBeNull();
        nary.NAryOperandPlan!.Elements.Should().ContainSingle(element => element.Kind == EquationVisualElementKind.Fraction);

        plan.Segments.Select(segment => segment.Text).Should().Equal("\u2211", "i=j_1", "n^2", "1/i");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.NAryOperator,
            EquationVisualSegmentRole.NAryLowerLimit,
            EquationVisualSegmentRole.NAryUpperLimit,
            EquationVisualSegmentRole.NAryOperand);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedNArySlots_AreDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.NAry,
            Operator = "\u2211",
            Sub = "i=1",
            Sup = "n",
            Base = "x",
            NAryLowerLimitEquation = equation
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().Contain("\u2211(i=1..n) x");
        plan.LinearText.Length.Should().BeLessThan(500);
        plan.Elements.Should().NotBeEmpty();
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
    public void EquationVisualPlanner_NestedMatrixCells_SurfaceSharedCellPlansAndKeepFlattenedSegments()
    {
        var structuredCell = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var matrix = new MathMatrix([["a+x2", "plain"]]);
        matrix.CellEquations.Add([structuredCell, null]);

        var plan = EquationVisualPlanner.Build(new Equation([MathRun.MatrixOf(matrix)]));

        plan.LinearText.Should().Be("[a+x^2, plain]");
        plan.Elements.Should().ContainSingle();
        var element = plan.Elements[0];
        element.Kind.Should().Be(EquationVisualElementKind.Matrix);
        element.MatrixRows.Should().ContainSingle();
        var cells = element.MatrixRows[0].Cells;
        cells.Select(cell => cell.Text).Should().Equal("a+x^2", "plain");
        cells[0].CellPlan.Should().NotBeNull();
        cells[0].CellPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);
        cells[1].CellPlan.Should().BeNull();
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            EquationVisualPlanner.MatrixOpenDelimiterText,
            "a+x^2",
            EquationVisualPlanner.MatrixColumnSeparatorText,
            "plain",
            EquationVisualPlanner.MatrixCloseDelimiterText);
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.MatrixOpenDelimiter,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixColumnSeparator,
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixCloseDelimiter);
    }

    [Fact]
    public void EquationVisualPlanner_NestedMatrixCells_AreDepthBounded()
    {
        var equation = new Equation();
        var matrix = new MathMatrix();
        matrix.Rows.Add(["fallback"]);
        matrix.CellEquations.Add([equation]);
        equation.Runs.Add(MathRun.MatrixOf(matrix));

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Length.Should().BeLessThan(500);
        plan.LinearText.Should().Contain("fallback");
        plan.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public void EquationVisualPlanner_NestedEquationArrayCells_SurfaceSharedCellPlans()
    {
        var structuredCell = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var array = MathMatrix.FromCellEquations([[structuredCell], [Equation.FromText("z")]]);

        var plan = EquationVisualPlanner.Build(new Equation([MathRun.EquationArrayOf(array)]));

        plan.LinearText.Should().Be("a+x^2; z");
        plan.Elements.Should().ContainSingle();
        var element = plan.Elements[0];
        element.Kind.Should().Be(EquationVisualElementKind.EquationArray);
        element.MatrixRowCount.Should().Be(2);
        element.MatrixColumnCount.Should().Be(1);
        element.MatrixRows[0].Cells[0].Text.Should().Be("a+x^2");
        element.MatrixRows[0].Cells[0].CellPlan.Should().NotBeNull();
        element.MatrixRows[1].Cells[0].Text.Should().Be("z");
        element.MatrixRows[1].Cells[0].CellPlan.Should().NotBeNull();
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "a+x^2",
            EquationVisualPlanner.MatrixRowSeparatorText,
            "z");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.MatrixCell,
            EquationVisualSegmentRole.MatrixRowSeparator,
            EquationVisualSegmentRole.MatrixCell);
    }

    [Fact]
    public void EquationVisualPlanner_Accent_BuildsStructuredMarkOverBaseElement()
    {
        var run = MathRun.AccentOf("x", "hat");
        var plan = EquationVisualPlanner.Build(new Equation([run]));

        plan.LinearText.Should().Be(run.LinearText);
        plan.Elements.Should().ContainSingle();
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.Accent);
        plan.Elements[0].BaseText.Should().Be("x");
        plan.Elements[0].Accent.Should().Be("hat");
        plan.Segments.Select(segment => segment.Text).Should().Equal("hat", "x");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.AccentMark,
            EquationVisualSegmentRole.AccentBase);
        plan.Segments[0].Style.FontSizeScale.Should().Be(EquationVisualPlanner.DecoratorFontSizeScale);
        plan.Segments[0].Style.Italic.Should().BeFalse();
        plan.Segments[1].Style.FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_Bar_BuildsStructuredTopAndBottomBarElements()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.BarOf("x"),
            MathRun.BarOf("y", top: false)
        ]));

        plan.Elements.Select(element => element.Kind).Should().Equal(
            EquationVisualElementKind.Bar,
            EquationVisualElementKind.Bar);
        plan.Elements[0].BaseText.Should().Be("x");
        plan.Elements[0].BarTop.Should().BeTrue();
        plan.Elements[1].BaseText.Should().Be("y");
        plan.Elements[1].BarTop.Should().BeFalse();
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            EquationVisualPlanner.OverbarCueText,
            "x",
            "y",
            EquationVisualPlanner.UnderbarCueText);
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.BarMark,
            EquationVisualSegmentRole.BarBase,
            EquationVisualSegmentRole.BarBase,
            EquationVisualSegmentRole.BarMark);
        plan.Segments.Where(segment => segment.Role == EquationVisualSegmentRole.BarMark)
            .Should().OnlyContain(segment => segment.Style.Italic == false);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_Delimiter_BuildsStructuredWrappedContentElement()
    {
        var run = MathRun.Delimiter("x + y", "[", "]");
        var plan = EquationVisualPlanner.Build(new Equation([run]));

        plan.LinearText.Should().Be(run.LinearText);
        plan.Elements.Should().ContainSingle();
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.Delimiter);
        plan.Elements[0].BaseText.Should().Be("x + y");
        plan.Elements[0].OpenDelimiter.Should().Be("[");
        plan.Elements[0].CloseDelimiter.Should().Be("]");
        plan.Segments.Select(segment => segment.Text).Should().Equal("[", "x + y", "]");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.DelimiterOpen,
            EquationVisualSegmentRole.DelimiterContent,
            EquationVisualSegmentRole.DelimiterClose);
        plan.Segments[0].Style.FontSizeScale.Should().Be(EquationVisualPlanner.DelimiterFontSizeScale);
        plan.Segments[2].Style.FontSizeScale.Should().Be(EquationVisualPlanner.DelimiterFontSizeScale);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedDelimiterContent_SurfacesSharedSlotPlanAndKeepsFlattenedSegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.Delimiter(
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]),
                "[",
                "]")
        ]));

        plan.LinearText.Should().Be("[a+x^2]");
        plan.Elements.Should().ContainSingle();
        var delimiter = plan.Elements[0];
        delimiter.Kind.Should().Be(EquationVisualElementKind.Delimiter);
        delimiter.BaseText.Should().Be("a+x^2");
        delimiter.OpenDelimiter.Should().Be("[");
        delimiter.CloseDelimiter.Should().Be("]");
        delimiter.DelimiterContentPlan.Should().NotBeNull();
        delimiter.DelimiterContentPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal("[", "a+x^2", "]");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.DelimiterOpen,
            EquationVisualSegmentRole.DelimiterContent,
            EquationVisualSegmentRole.DelimiterClose);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedDelimiterContent_IsDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Delimiter,
            Base = "x",
            DelimiterContentEquation = equation
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().Contain("(x)");
        plan.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public void EquationVisualPlanner_MultiArgumentDelimiter_PlansEveryArgumentAndSeparator_NotJustTheFirst()
    {
        // Regression for the visual-truncation gap: the model has always round-tripped every m:e under a
        // multi-argument m:d (AdditionalDelimiterArguments), but AddDelimiterElement only ever read the
        // first argument, so a 3-argument binomial/case delimiter displayed as "(n)" no matter how many
        // arguments the file actually held. Assert the planned element (and flattened segments) carry all
        // three arguments plus both separators, not just the first.
        var run = MathRun.Delimiter(["n", "k", "m"], "(", ")", ",");
        var plan = EquationVisualPlanner.Build(new Equation([run]));

        plan.LinearText.Should().Be("(n,k,m)");
        plan.Elements.Should().ContainSingle();
        var delimiter = plan.Elements[0];
        delimiter.Kind.Should().Be(EquationVisualElementKind.Delimiter);
        delimiter.OpenDelimiter.Should().Be("(");
        delimiter.CloseDelimiter.Should().Be(")");
        delimiter.BaseText.Should().Be("n");
        delimiter.DelimiterSeparatorText.Should().Be(",");
        delimiter.AdditionalDelimiterArgumentTexts.Should().Equal("k", "m");

        // The flattened segment list (what both shells' equation renderers ultimately draw/build from) must
        // contain every argument and every separator in document order, not just open/first-arg/close.
        plan.Segments.Select(segment => segment.Text).Should().Equal("(", "n", ",", "k", ",", "m", ")");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.DelimiterOpen,
            EquationVisualSegmentRole.DelimiterContent,
            EquationVisualSegmentRole.DelimiterSeparator,
            EquationVisualSegmentRole.DelimiterContent,
            EquationVisualSegmentRole.DelimiterSeparator,
            EquationVisualSegmentRole.DelimiterContent,
            EquationVisualSegmentRole.DelimiterClose);

        // Same element's own Segments must match (it's what the WPF shell iterates directly to build TextBlocks).
        delimiter.Segments.Select(segment => segment.Text).Should().Equal("(", "n", ",", "k", ",", "m", ")");
    }

    [Fact]
    public void EquationVisualPlanner_MultiArgumentDelimiter_WithStructuredArguments_SurfacesEveryArgumentPlan()
    {
        // Same gap, but with nested OMML structure in the additional arguments (not just plain text) — the
        // Avalonia shell draws each argument via its own EquationVisualPlan (for correct nested geometry),
        // so AdditionalDelimiterArgumentPlans must be populated in parallel with AdditionalDelimiterArgumentTexts.
        var run = new MathRun
        {
            Kind = MathRunKind.Delimiter,
            Base = "n",
            OpenChar = "{",
            CloseChar = "}",
            DelimiterSeparator = ";",
            AdditionalDelimiterArguments = ["k", "x^2"],
            AdditionalDelimiterContentEquations =
            [
                null,
                new Equation([MathRun.Superscript("x", "2")])
            ]
        };

        var plan = EquationVisualPlanner.Build(new Equation([run]));
        var delimiter = plan.Elements.Single();

        delimiter.AdditionalDelimiterArgumentTexts.Should().Equal("k", "x^2");
        delimiter.AdditionalDelimiterArgumentPlans.Should().HaveCount(2);
        delimiter.AdditionalDelimiterArgumentPlans[0].Should().BeNull();
        delimiter.AdditionalDelimiterArgumentPlans[1].Should().NotBeNull();
        delimiter.AdditionalDelimiterArgumentPlans[1]!.Segments.Select(segment => segment.Role)
            .Should().Equal(EquationVisualSegmentRole.Base, EquationVisualSegmentRole.Superscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal("{", "n", ";", "k", ";", "x^2", "}");
    }

    [Fact]
    public void EquationVisualPlanner_GroupChar_BuildsStructuredTopAndBottomGroupElements()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.GroupCharOf("x", "\u23DE", "top"),
            MathRun.GroupCharOf("y", "\u23DF", "bot")
        ]));

        plan.Elements.Select(element => element.Kind).Should().Equal(
            EquationVisualElementKind.GroupChar,
            EquationVisualElementKind.GroupChar);
        plan.Elements[0].BaseText.Should().Be("x");
        plan.Elements[0].GroupCharacter.Should().Be("\u23DE");
        plan.Elements[0].GroupCharacterPosition.Should().Be("top");
        plan.Elements[0].GroupCharacterTop.Should().BeTrue();
        plan.Elements[1].BaseText.Should().Be("y");
        plan.Elements[1].GroupCharacter.Should().Be("\u23DF");
        plan.Elements[1].GroupCharacterPosition.Should().Be("bot");
        plan.Elements[1].GroupCharacterTop.Should().BeFalse();
        plan.Segments.Select(segment => segment.Text).Should().Equal("\u23DE", "x", "y", "\u23DF");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.GroupCharMark,
            EquationVisualSegmentRole.GroupCharBase,
            EquationVisualSegmentRole.GroupCharBase,
            EquationVisualSegmentRole.GroupCharMark);
        plan.Segments.Where(segment => segment.Role == EquationVisualSegmentRole.GroupCharMark)
            .Should().OnlyContain(segment => segment.Style.FontSizeScale == EquationVisualPlanner.DecoratorFontSizeScale);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedDecoratorBaseSlots_SurfaceSharedSlotPlansAndKeepFlattenedSegments()
    {
        var nestedBase = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);

        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.AccentOf(nestedBase, "hat"),
            MathRun.BarOf(nestedBase, top: false),
            MathRun.GroupCharOf(nestedBase, "\u23DF", "bot")
        ]));

        plan.LinearText.Should().Be("a+x^2hat_a+x^2_a+x^2\u23DF");
        plan.Elements.Select(element => element.Kind).Should().Equal(
            EquationVisualElementKind.Accent,
            EquationVisualElementKind.Bar,
            EquationVisualElementKind.GroupChar);

        var accent = plan.Elements[0];
        accent.BaseText.Should().Be("a+x^2");
        accent.AccentBasePlan.Should().NotBeNull();
        accent.AccentBasePlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);

        var bar = plan.Elements[1];
        bar.BaseText.Should().Be("a+x^2");
        bar.BarBasePlan.Should().NotBeNull();
        bar.BarBasePlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);

        var groupChar = plan.Elements[2];
        groupChar.BaseText.Should().Be("a+x^2");
        groupChar.GroupCharBasePlan.Should().NotBeNull();
        groupChar.GroupCharBasePlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "hat",
            "a+x^2",
            "a+x^2",
            EquationVisualPlanner.UnderbarCueText,
            "a+x^2",
            "\u23DF");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.AccentMark,
            EquationVisualSegmentRole.AccentBase,
            EquationVisualSegmentRole.BarBase,
            EquationVisualSegmentRole.BarMark,
            EquationVisualSegmentRole.GroupCharBase,
            EquationVisualSegmentRole.GroupCharMark);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedDecoratorBaseSlots_AreDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Accent,
            Base = "x",
            Accent = "hat",
            DecoratorBaseEquation = equation
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().Contain("xhat");
        plan.LinearText.Length.Should().BeLessThan(100);
        plan.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public void EquationVisualPlanner_FunctionApply_BuildsStructuredFunctionApplicationElement()
    {
        var run = MathRun.FunctionApply("sin", "x + y");
        var plan = EquationVisualPlanner.Build(new Equation([run]));

        plan.LinearText.Should().Be(run.LinearText);
        plan.Elements.Should().ContainSingle();
        plan.Elements[0].Kind.Should().Be(EquationVisualElementKind.FunctionApply);
        plan.Elements[0].FunctionName.Should().Be("sin");
        plan.Elements[0].FunctionArgument.Should().Be("x + y");
        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "sin",
            "x + y");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.FunctionName,
            EquationVisualSegmentRole.FunctionArgument);
        plan.Segments[0].Style.Italic.Should().BeFalse();
        plan.Segments[1].Style.Italic.Should().BeTrue();
        plan.Segments[0].Style.FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        plan.Segments[1].Style.FontSizeScale.Should().Be(EquationVisualPlanner.StructureFontSizeScale);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedFunctionArgument_SurfacesSharedSlotPlanAndKeepsFlattenedSegments()
    {
        var plan = EquationVisualPlanner.Build(new Equation([
            MathRun.FunctionApply(
                "sin",
                new Equation([
                    MathRun.PlainText("a+"),
                    MathRun.Superscript("x", "2")
                ]))
        ]));

        plan.LinearText.Should().Be("sin(a+x^2)");
        plan.Elements.Should().ContainSingle();
        var function = plan.Elements[0];
        function.Kind.Should().Be(EquationVisualElementKind.FunctionApply);
        function.FunctionName.Should().Be("sin");
        function.FunctionArgument.Should().Be("a+x^2");
        function.FunctionArgumentPlan.Should().NotBeNull();
        function.FunctionArgumentPlan!.Segments.Select(segment => segment.Role)
            .Should().Equal(
                EquationVisualSegmentRole.Text,
                EquationVisualSegmentRole.Base,
                EquationVisualSegmentRole.Superscript);

        plan.Segments.Select(segment => segment.Text).Should().Equal(
            "sin",
            "a+x^2");
        plan.Segments.Select(segment => segment.Role).Should().Equal(
            EquationVisualSegmentRole.FunctionName,
            EquationVisualSegmentRole.FunctionArgument);
        plan.Segments.Should().NotContain(segment => segment.Role == EquationVisualSegmentRole.LinearFallback);
    }

    [Fact]
    public void EquationVisualPlanner_NestedFunctionArgument_IsDepthBounded()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.FunctionApply,
            FuncName = "sin",
            Base = "x",
            FunctionArgumentEquation = equation
        });

        var plan = EquationVisualPlanner.Build(equation);

        plan.LinearText.Should().Contain("sin(x)");
        plan.LinearText.Length.Should().BeLessThan(100);
        plan.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public void EquationVisualPlanner_BuildEvidence_EmitsStableGeometryAndNestedSlotSignatures()
    {
        var nestedNumerator = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var nestedDenominator = new Equation([
            MathRun.Radical("b", "3")
        ]);
        var equation = new Equation([
            MathRun.Fraction(nestedNumerator, nestedDenominator),
            MathRun.NAry(
                "\u2211",
                new Equation([MathRun.Subscript("i", "0")]),
                new Equation([MathRun.PlainText("n")]),
                new Equation([MathRun.FunctionApply("sin", "x")]))
        ]);

        var evidence = EquationVisualPlanner.BuildEvidence([equation]);

        evidence.EquationCount.Should().Be(1);
        evidence.NestedSlotCount.Should().Be(5);
        evidence.MaxNestedSlotDepth.Should().Be(1);
        evidence.ElementKindCounts.Should().Contain([
            "Fraction=1",
            "FunctionApply=1",
            "NAry=1",
            "Radical=1",
            "Segments=4"]);
        evidence.SegmentRoleCounts.Should().Contain([
            "FractionBar=1",
            "FractionDenominator=1",
            "FractionNumerator=1",
            "FunctionArgument=1",
            "FunctionName=1",
            "NAryOperand=1",
            "NAryOperator=1",
            "RadicalDegree=1"]);
        evidence.BaselineRoleCounts.Should().Contain([
            "Normal=13",
            "Subscript=2",
            "Superscript=3"]);
        evidence.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=fraction", StringComparison.Ordinal)
            && signature.Contains("numerator=a+x^2", StringComparison.Ordinal)
            && signature.Contains("denominator=3\u221a(b)", StringComparison.Ordinal));
        evidence.ElementGeometrySignatures.Should().Contain(signature =>
            signature.Contains("geometry=nary", StringComparison.Ordinal)
            && signature.Contains("operator=\u2211", StringComparison.Ordinal)
            && signature.Contains("operand=sin(x)", StringComparison.Ordinal));
        evidence.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=fraction", StringComparison.Ordinal)
            && signature.Contains("layout=vertical-stack", StringComparison.Ordinal)
            && signature.Contains("barThicknessEm=0.05", StringComparison.Ordinal)
            && signature.Contains("numeratorSegments=3", StringComparison.Ordinal)
            && signature.Contains("denominatorSegments=3", StringComparison.Ordinal));
        evidence.SpacingGeometrySignatures.Should().Contain(signature =>
            signature.Contains("spacing=nary", StringComparison.Ordinal)
            && signature.Contains("limitPlacement=above-below", StringComparison.Ordinal)
            && signature.Contains("operandGapEm=0.16", StringComparison.Ordinal)
            && signature.Contains("operatorScale=1.32", StringComparison.Ordinal));
        evidence.SlotGeometrySignatures.Should().Contain(signature =>
            signature.Contains("slot=fraction-numerator", StringComparison.Ordinal)
            && signature.Contains("roles=Text,Base,Superscript", StringComparison.Ordinal));
        evidence.SlotGeometrySignatures.Should().Contain(signature =>
            signature.Contains("slot=nary-operand", StringComparison.Ordinal)
            && signature.Contains("roles=FunctionName,FunctionArgument", StringComparison.Ordinal));
    }
}
