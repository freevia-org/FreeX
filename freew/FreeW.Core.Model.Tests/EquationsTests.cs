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
