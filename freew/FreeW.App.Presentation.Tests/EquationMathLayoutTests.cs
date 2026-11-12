using FreeW.App.Presentation.DocumentView.MathLayout;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// The shared equation box-layout engine, exercised with the metric-only Cambria Math stand-in so the
/// geometry is deterministic on every platform (no font files needed).
/// </summary>
public sealed class EquationMathLayoutTests
{
    private const double Size = 20.0;
    private static readonly MathFontConstants K = MathFontConstants.CambriaMath;

    private static MathLayoutResult Layout(Equation equation, bool display = false) =>
        EquationMathLayout.Layout(equation, EstimatedMathLayoutFont.Instance, Size, display);

    private static MathLayoutResult Layout(string linear, bool display = false) =>
        Layout(EquationLinearFormat.Parse(linear), display);

    private static List<MathTextDrawItem> Texts(MathLayoutResult layout) =>
        layout.Items.OfType<MathTextDrawItem>().ToList();

    private static MathTextDrawItem Text(MathLayoutResult layout, string text) =>
        Texts(layout).Single(item => item.Text == text);

    [Fact]
    public void Letters_AreMathItalic_DigitsAndOperatorsUpright_HyphenIsMinus()
    {
        var layout = Layout(new Equation([MathRun.PlainText("x-1")]));

        Texts(layout).Select(item => item.Text).Should().Equal("\U0001D465", "−", "1");
    }

    [Fact]
    public void BinaryOperators_GetMediumSpace_RelationsThickSpace_NotInScripts()
    {
        var layout = Layout(new Equation([MathRun.PlainText("a+b=c")]));
        var items = Texts(layout);
        var a = items[0];
        var plus = items[1];
        var b = items[2];
        var equals = items[3];

        var mediumSpace = 4.0 / 18 * Size;
        var thickSpace = 5.0 / 18 * Size;
        plus.X.Should().BeApproximately(a.X + (0.5 * Size) + mediumSpace, 0.01);
        equals.X.Should().BeApproximately(b.X + (0.5 * Size) + thickSpace, 0.01);

        var script = Layout(new Equation([MathRun.Superscript(Equation.FromText("x"), Equation.FromText("a+b"))]));
        var scriptItems = Texts(script);
        var scriptA = scriptItems.Single(item => item.Text == "\U0001D44E");
        var scriptPlus = scriptItems.Single(item => item.Text == "+");
        scriptPlus.X.Should().BeApproximately(scriptA.X + (0.5 * scriptA.FontSize), 0.01,
            "medium/thick spaces are suppressed in script styles");
    }

    [Fact]
    public void LeadingMinus_IsUnary_WithNoBinarySpace()
    {
        var items = Texts(Layout(new Equation([MathRun.PlainText("-b")])));

        items[1].X.Should().BeApproximately(
            items[0].X + (EstimatedMathLayoutFont.Instance.MeasureText("−").Width * Size), 0.01);
    }

    [Fact]
    public void Superscript_IsRaisedAndScaledToScriptSize()
    {
        var layout = Layout("x^2");
        var exponent = Text(layout, "2");

        exponent.FontSize.Should().BeApproximately(Size * K.ScriptPercentScaleDown / 100, 0.001);
        exponent.BaselineY.Should().BeLessThanOrEqualTo(-K.SuperscriptShiftUp * Size + 0.001);
    }

    [Fact]
    public void SubSuperscript_KeepsTheMinimumGapBetweenScripts()
    {
        var layout = Layout(new Equation([MathRun.SubSuperscript("T", "n", "2")]));
        var sup = Text(layout, "2");
        var sub = Text(layout, "\U0001D45B");

        sub.BaselineY.Should().BeGreaterThan(0);
        sup.BaselineY.Should().BeLessThan(0);
        sup.X.Should().BeApproximately(sub.X + (EstimatedMathLayoutFont.Instance.MeasureText("\U0001D447").ItalicCorrection * Size), 0.01,
            "the superscript is moved right by the base's italic correction");
    }

    [Fact]
    public void BarFraction_IsStackedOnTheMathAxis_InDisplayAtFullSize()
    {
        var layout = Layout("a/b", display: true);
        var rule = layout.Items.OfType<MathRuleDrawItem>().Should().ContainSingle().Subject;
        var numerator = Text(layout, "\U0001D44E");
        var denominator = Text(layout, "\U0001D44F");

        (rule.Y + (rule.Height / 2)).Should().BeApproximately(-K.AxisHeight * Size, 0.001);
        numerator.FontSize.Should().Be(Size, "display fractions set their parts in text style");
        numerator.BaselineY.Should().BeLessThanOrEqualTo(-K.FractionNumeratorDisplayStyleShiftUp * Size + 0.001);
        denominator.BaselineY.Should().BeGreaterThanOrEqualTo(K.FractionDenominatorDisplayStyleShiftDown * Size - 0.001);
        Math.Abs((numerator.X + (0.25 * Size)) - (rule.X + (rule.Width / 2))).Should().BeLessThan(0.01, "the numerator is centred");
    }

    [Fact]
    public void InlineFraction_SetsItsPartsInScriptSize()
    {
        var layout = Layout("a/b");

        Text(layout, "\U0001D44E").FontSize.Should().BeApproximately(Size * 0.73, 0.001);
    }

    [Fact]
    public void NestedFractions_StepDownThroughScriptScriptSize()
    {
        var layout = Layout("1/(1+1/x)", display: true);
        var sizes = Texts(layout).Select(item => Math.Round(item.FontSize, 3)).Distinct().OrderByDescending(size => size).ToList();

        sizes.Should().Equal(Size, Math.Round(Size * 0.73, 3));
        Layout("1/(1+1/(1+1/x))", display: true).Items.OfType<MathTextDrawItem>()
            .Should().Contain(item => Math.Abs(item.FontSize - (Size * 0.6)) < 0.001);
    }

    [Fact]
    public void NoBarFraction_HasNoRule()
    {
        var layout = Layout(@"(n\atop k)", display: true);

        layout.Items.OfType<MathRuleDrawItem>().Should().BeEmpty();
        Text(layout, "\U0001D45B").BaselineY.Should().BeLessThan(0);
        Text(layout, "\U0001D458").BaselineY.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SkewedFraction_PlacesNumeratorUpLeftAndDenominatorDownRight()
    {
        var equation = new Equation([MathRun.Fraction("a", "b") with { FractionType = MathFractionType.Skewed }]);
        var layout = Layout(equation, display: true);
        var numerator = Text(layout, "\U0001D44E");
        var denominator = Text(layout, "\U0001D44F");

        layout.Items.OfType<MathRuleDrawItem>().Should().BeEmpty();
        numerator.BaselineY.Should().BeLessThan(denominator.BaselineY);
        denominator.X.Should().BeGreaterThan(numerator.X + (0.5 * numerator.FontSize));
        Texts(layout).Should().Contain(item => item.Text == "/");
    }

    [Fact]
    public void LinearFraction_StaysOnOneLineAtFullSize()
    {
        var equation = new Equation([MathRun.Fraction("a", "b") with { FractionType = MathFractionType.Linear }]);
        var items = Texts(Layout(equation, display: true));

        items.Select(item => item.Text).Should().Equal("\U0001D44E", "/", "\U0001D44F");
        items.Should().OnlyContain(item => item.BaselineY == 0 && item.FontSize == Size);
    }

    [Fact]
    public void DisplaySum_CentresItsLimitsAboveAndBelow()
    {
        var layout = Layout(new Equation([MathRun.NAry("∑", "i", "n", "x")]), display: true);
        var op = Texts(layout).First(item => item.Text == "∑");
        var lower = Text(layout, "\U0001D456");
        var upper = Text(layout, "\U0001D45B");
        var operand = Text(layout, "\U0001D465");

        op.FontSize.Should().BeGreaterThan(Size, "a display operator is drawn larger");
        lower.BaselineY.Should().BeGreaterThan(0);
        upper.BaselineY.Should().BeLessThan(-Size * 0.5);
        lower.X.Should().BeApproximately(upper.X, 0.01, "both limits are centred on the operator");
        operand.X.Should().BeGreaterThan(Math.Max(lower.X, upper.X));
    }

    [Theory]
    [InlineData("∑", false)]
    [InlineData("∫", true)]
    public void SideLimits_ForInlineOperatorsAndDisplayIntegrals(string op, bool display)
    {
        var layout = Layout(new Equation([MathRun.NAry(op, "0", "1", "f")]), display);
        var opItem = Texts(layout).First(item => item.Text == op);
        var lower = Text(layout, "0");
        var upper = Text(layout, "1");

        lower.X.Should().BeGreaterThan(opItem.X, "limits sit to the side of the operator");
        upper.X.Should().BeGreaterThan(opItem.X);
        upper.BaselineY.Should().BeLessThan(0);
        lower.BaselineY.Should().BeGreaterThan(0);
        lower.FontSize.Should().BeApproximately(Size * 0.73, 0.001);
    }

    [Fact]
    public void LowerLimit_IsCentredBelowItsBase()
    {
        var equation = new Equation([MathRun.FunctionApply("lim", "f") with
        {
            FunctionNameEquation = new Equation([MathRun.LowerLimit(Equation.FromText("lim"), Equation.FromText("n"))])
        }]);
        var layout = Layout(equation);
        var name = Text(layout, "lim");
        var limit = Text(layout, "\U0001D45B");

        name.BaselineY.Should().Be(0);
        limit.BaselineY.Should().BeGreaterThan(0);
        (limit.X + (0.25 * limit.FontSize)).Should().BeApproximately(name.X + (0.75 * Size), 0.01, "the limit is centred under lim");
    }

    [Fact]
    public void Radical_HasOverbarAndScriptScriptDegree()
    {
        var layout = Layout(new Equation([MathRun.Radical("x", "3")]));
        var degree = Text(layout, "3");
        var radicand = Text(layout, "\U0001D465");
        var bar = layout.Items.OfType<MathRuleDrawItem>().Should().ContainSingle().Subject;

        degree.FontSize.Should().BeApproximately(Size * K.ScriptScriptPercentScaleDown / 100, 0.001);
        degree.BaselineY.Should().BeLessThan(0);
        degree.X.Should().BeLessThan(radicand.X);
        bar.X.Should().BeApproximately(radicand.X, 0.01);
        bar.Y.Should().BeLessThan(-0.47 * Size, "the overbar clears the radicand");
        layout.Ascent.Should().BeGreaterThan(-bar.Y);
    }

    [Fact]
    public void NestedStructuresInsideSlots_AreLaidOutStructurally()
    {
        var layout = Layout(@"e^(\sqrt(a/b))");

        layout.Items.OfType<MathRuleDrawItem>().Should().HaveCount(2, "the exponent holds a radical with a fraction inside");
        Texts(layout).Should().NotContain(item => item.Text.Contains('/'), "the fraction is built up, not linear");
        Text(layout, "\U0001D44E").FontSize.Should().BeApproximately(Size * 0.6, 0.001);
    }

    [Fact]
    public void Delimiters_GrowWithTallContent()
    {
        var small = Layout("(a)", display: true);
        var tall = Layout("(a/b)", display: true);

        Text(small, "(").FontSize.Should().Be(Size);
        Text(tall, "(").FontSize.Should().BeGreaterThan(Size, "m:grow stretches the fence around a fraction");
        Text(tall, ")").FontSize.Should().Be(Text(tall, "(").FontSize);
    }

    [Fact]
    public void Matrix_LaysCellsOnAGridCentredOnTheAxis_WithoutBrackets()
    {
        var layout = Layout(new Equation([MathRun.MatrixOf(new MathMatrix([["1", "22"], ["333", "4"]]))]));
        var items = Texts(layout);

        items.Select(item => item.Text).Should().Equal("1", "22", "333", "4");
        items[0].BaselineY.Should().Be(items[1].BaselineY);
        items[2].BaselineY.Should().BeGreaterThan(items[0].BaselineY);
        (items[0].X + 0.25 * Size).Should().BeApproximately(items[2].X + 0.75 * Size, 0.01, "columns are centred");
        ((layout.Ascent - layout.Descent) / 2).Should().BeApproximately(K.AxisHeight * Size, 0.01);
    }

    [Fact]
    public void Accent_SitsAboveItsBase_AndBarsAddRules()
    {
        var accent = Layout(new Equation([MathRun.AccentOf("x", "̂")]));
        var mark = Texts(accent).Single(item => item.Text == "̂");
        mark.BaselineY.Should().BeLessThan(-0.4 * Size);

        var over = Layout(new Equation([MathRun.BarOf("x")]));
        over.Items.OfType<MathRuleDrawItem>().Single().Y.Should().BeLessThan(-0.47 * Size);
        var under = Layout(new Equation([MathRun.BarOf("x", top: false)]));
        under.Items.OfType<MathRuleDrawItem>().Single().Y.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FunctionName_IsUpright_WithAThinSpaceBeforeTheArgument()
    {
        var items = Texts(Layout(new Equation([MathRun.FunctionApply("sin", "x")])));

        items.Select(item => item.Text).Should().Equal("sin", "\U0001D465");
        items[1].X.Should().BeApproximately(items[0].X + (1.5 * Size) + (3.0 / 18 * Size), 0.01);
    }

    [Fact]
    public void PlainStyleText_IsDrawnUpright()
    {
        var items = Texts(Layout(new Equation([MathRun.PlainText("dx") with { IsUpright = true }, MathRun.PlainText("y")])));

        items.Select(item => item.Text).Should().Equal("dx", "\U0001D466");
    }

    [Fact]
    public void DeeplyNestedEquation_DoesNotThrow()
    {
        var equation = Equation.FromText("x");
        for (var i = 0; i < 40; i++)
            equation = new Equation([MathRun.Superscript(equation, Equation.FromText("2"))]);

        var layout = Layout(equation);

        layout.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DisplayStyle_FollowsTheEquationsDisplayFlag()
    {
        var equation = EquationLinearFormat.Parse("a/b");
        equation.IsDisplayMath = true;

        EquationMathLayout.Layout(equation, EstimatedMathLayoutFont.Instance, Size).Style.Should().Be(MathLayoutStyle.Display);
        EquationMathLayout.Layout(EquationLinearFormat.Parse("a/b"), EstimatedMathLayoutFont.Instance, Size).Style.Should().Be(MathLayoutStyle.Text);
    }
}
