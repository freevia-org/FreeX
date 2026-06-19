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
}
