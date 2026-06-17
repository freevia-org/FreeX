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
