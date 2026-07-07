namespace FreeW.Core.Model.Tests;

public class EquationsTests
{
    [Fact]
    public void MathRun_LinearText_RendersEachKind()
    {
        MathRun.PlainText("x + 1").LinearText.Should().Be("x + 1");
        MathRun.Superscript("c", "2").LinearText.Should().Be("c^2");
        MathRun.Fraction("a", "b").LinearText.Should().Be("a/b");
    }

    [Fact]
    public void MathRun_Fraction_CanCarryNestedEquations()
    {
        var numerator = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);
        var denominator = new Equation([
            MathRun.PlainText("b+"),
            MathRun.Subscript("y", "1")
        ]);

        var fraction = MathRun.Fraction(numerator, denominator);

        fraction.Kind.Should().Be(MathRunKind.Fraction);
        fraction.Numerator.Should().Be("a+x^2");
        fraction.Denominator.Should().Be("b+y_1");
        fraction.NumeratorEquation.Should().BeSameAs(numerator);
        fraction.DenominatorEquation.Should().BeSameAs(denominator);
        fraction.LinearText.Should().Be("a+x^2/b+y_1");
    }

    [Fact]
    public void MathRun_Fraction_LinearText_IsDepthBoundedForCyclicNestedSlots()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Fraction,
            NumeratorEquation = equation,
            Denominator = "b"
        });

        var linearText = equation.LinearText;

        linearText.Should().NotBeEmpty();
        linearText.Length.Should().BeLessThan(100);
        linearText.Should().EndWith("/b");
    }

    [Fact]
    public void MathRun_Radical_CanCarryNestedRadicandEquation()
    {
        var radicand = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);

        var radical = MathRun.Radical(radicand, "3");

        radical.Kind.Should().Be(MathRunKind.Radical);
        radical.Base.Should().Be("a+x^2");
        radical.Degree.Should().Be("3");
        radical.RadicandEquation.Should().BeSameAs(radicand);
        radical.LinearText.Should().Be("3\u221a(a+x^2)");
    }

    [Fact]
    public void MathRun_Radical_LinearText_IsDepthBoundedForCyclicNestedRadicand()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Radical,
            Base = "x",
            RadicandEquation = equation
        });

        var linearText = equation.LinearText;

        linearText.Should().NotBeEmpty();
        linearText.Length.Should().BeLessThan(100);
        linearText.Should().Contain("\u221a(x)");
    }

    [Fact]
    public void MathRun_Delimiter_CanCarryNestedContentEquation()
    {
        var content = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);

        var delimiter = MathRun.Delimiter(content, "[", "]");

        delimiter.Kind.Should().Be(MathRunKind.Delimiter);
        delimiter.Base.Should().Be("a+x^2");
        delimiter.OpenChar.Should().Be("[");
        delimiter.CloseChar.Should().Be("]");
        delimiter.DelimiterContentEquation.Should().BeSameAs(content);
        delimiter.LinearText.Should().Be("[a+x^2]");
    }

    [Fact]
    public void MathRun_Delimiter_LinearText_IsDepthBoundedForCyclicNestedContent()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.Delimiter,
            Base = "x",
            DelimiterContentEquation = equation
        });

        var linearText = equation.LinearText;

        linearText.Should().NotBeEmpty();
        linearText.Length.Should().BeLessThan(100);
        linearText.Should().Contain("(x)");
    }

    [Fact]
    public void MathRun_LinearText_RendersNewStructures()
    {
        MathRun.Subscript("x", "i").LinearText.Should().Be("x_i");
        MathRun.SubSuperscript("x", "i", "2").LinearText.Should().Be("x_i^2");
        MathRun.Radical("x").LinearText.Should().Be("√(x)");
        MathRun.Radical("x", "3").LinearText.Should().Be("3√(x)");
        MathRun.NAry("∑", "i=1", "n", "i").LinearText.Should().Be("∑(i=1..n) i");
        MathRun.Delimiter("a, b").LinearText.Should().Be("(a, b)");
        MathRun.Delimiter("a", "[", "]").LinearText.Should().Be("[a]");
        MathRun.MatrixOf(MathMatrix.Identity2x2()).LinearText.Should().Be("[1, 0; 0, 1]");
        MathRun.FunctionApply("sin", "x").LinearText.Should().Be("sin(x)");
        MathRun.GroupCharOf("x+y").LinearText.Should().Be("\u23DEx+y");
        MathRun.GroupCharOf("x+y", "\u23DF", "bot").LinearText.Should().Be("x+y\u23DF");
    }

    [Fact]
    public void MathRun_Accent_DefaultsToHatAndCarriesBase()
    {
        var hat = MathRun.AccentOf("x");
        hat.Kind.Should().Be(MathRunKind.Accent);
        hat.Base.Should().Be("x");
        hat.Accent.Should().Be("̂");
        hat.LinearText.Should().Be("x̂");

        // An explicit accent glyph (e.g. a vector arrow) is preserved; an empty glyph falls back to the hat.
        MathRun.AccentOf("v", "→").Accent.Should().Be("→");
        MathRun.AccentOf("v", "").Accent.Should().Be("̂");
    }

    [Fact]
    public void MathRun_Bar_DefaultsToOverbarAndHonoursPosition()
    {
        var over = MathRun.BarOf("AB");
        over.Kind.Should().Be(MathRunKind.Bar);
        over.Base.Should().Be("AB");
        over.BarTop.Should().BeTrue();
        over.LinearText.Should().Be("‾AB‾");

        var under = MathRun.BarOf("AB", top: false);
        under.BarTop.Should().BeFalse();
        under.LinearText.Should().Be("_AB_");
    }

    [Fact]
    public void MathMatrix_ReportsDimensions()
    {
        var matrix = new MathMatrix([["a", "b", "c"], ["d", "e", "f"]]);
        matrix.RowCount.Should().Be(2);
        matrix.ColumnCount.Should().Be(3);
        matrix.LinearText.Should().Be("[a, b, c; d, e, f]");

        new MathMatrix().ColumnCount.Should().Be(0);
    }

    [Fact]
    public void Equation_LinearText_ConcatenatesFragments()
    {
        var equation = new Equation([
            MathRun.PlainText("E = m"),
            MathRun.Superscript("c", "2")
        ]);

        equation.LinearText.Should().Be("E = mc^2");
    }

    [Fact]
    public void FromEquation_MirrorsLinearTextAsRunFallback()
    {
        var run = Run.FromEquation(Equation.FromText("a/b"));

        run.Equation.Should().NotBeNull();
        run.Text.Should().Be("a/b");
    }

    [Fact]
    public void MathRun_FunctionApply_SetsKindAndSlots()
    {
        var func = MathRun.FunctionApply("sin", "x");

        func.Kind.Should().Be(MathRunKind.FunctionApply);
        func.FuncName.Should().Be("sin");
        func.Base.Should().Be("x");
        func.LinearText.Should().Be("sin(x)");
    }

    [Fact]
    public void MathRun_FunctionApply_CanCarryNestedArgumentEquation()
    {
        var argument = new Equation([
            MathRun.PlainText("a+"),
            MathRun.Superscript("x", "2")
        ]);

        var func = MathRun.FunctionApply("sin", argument);

        func.Kind.Should().Be(MathRunKind.FunctionApply);
        func.FuncName.Should().Be("sin");
        func.Base.Should().Be("a+x^2");
        func.FunctionArgumentEquation.Should().BeSameAs(argument);
        func.LinearText.Should().Be("sin(a+x^2)");
    }

    [Fact]
    public void MathRun_FunctionApply_LinearText_IsDepthBoundedForCyclicNestedArgument()
    {
        var equation = new Equation();
        equation.Runs.Add(new MathRun
        {
            Kind = MathRunKind.FunctionApply,
            FuncName = "sin",
            Base = "x",
            FunctionArgumentEquation = equation
        });

        var linearText = equation.LinearText;

        linearText.Should().NotBeEmpty();
        linearText.Length.Should().BeLessThan(100);
        linearText.Should().Contain("sin(x)");
    }

    [Fact]
    public void MathRun_FunctionApply_RendersLimWithArgument()
    {
        var lim = MathRun.FunctionApply("lim", "f(x)");
        lim.LinearText.Should().Be("lim(f(x))");
    }

    [Fact]
    public void MathRun_GroupChar_DefaultsToOverbrace()
    {
        var gc = MathRun.GroupCharOf("x+y");

        gc.Kind.Should().Be(MathRunKind.GroupChar);
        gc.Base.Should().Be("x+y");
        gc.GroupChr.Should().Be("⏞");
        gc.GroupChrPos.Should().Be("top");
        // Linear: glyph above → glyph before base
        gc.LinearText.Should().Be("⏞x+y");
    }

    [Fact]
    public void MathRun_GroupChar_UnderbraceSetsBotPos()
    {
        var under = MathRun.GroupCharOf("a+b", "⏟", "bot");

        under.GroupChr.Should().Be("⏟");
        under.GroupChrPos.Should().Be("bot");
        // Linear: glyph below → glyph after base
        under.LinearText.Should().Be("a+b⏟");
    }
}
