/// <summary>
/// Word's built-in equation gallery typed in linear format must build up into the structures Word itself
/// produces: build-up spaces consumed, postfix factorials kept with their operand, and an n-ary operand that
/// spans the whole following product.
/// </summary>
public class EquationLinearGalleryTests
{
    public static TheoryData<string, string> Gallery() => new()
    {
        { "area of circle", @"A=\pi r^2" },
        { "binomial theorem", @"(x+a)^n=\sum_(k=0)^n\of (n\atop k)x^k a^(n-k)" },
        { "expansion of a sum", @"(1+x)^n=1+(nx)/1!+(n(n-1)x^2)/2!+\dots" },
        { "fourier series", @"f(x)=a_0+\sum_(n=1)^\infty\of (a_n cos\funcapply((n\pi x)/L)+b_n sin\funcapply((n\pi x)/L))" },
        { "pythagorean theorem", "a^2+b^2=c^2" },
        { "quadratic formula", @"x=(-b\pm\sqrt(b^2-4ac))/(2a)" },
        { "taylor expansion", @"e^x=1+x/1!+x^2/2!+x^3/3!+\dots, -\infty<x<\infty" },
        { "trig identity 1", @"sin \alpha\pm sin \beta=2 sin\funcapply(1/2(\alpha\pm\beta)) cos\funcapply(1/2(\alpha\mp\beta))" },
        { "trig identity 2", @"cos \alpha+cos \beta=2 cos\funcapply(1/2(\alpha+\beta)) cos\funcapply(1/2(\alpha-\beta))" },
    };

    private static IEnumerable<MathRun> AllRuns(Equation equation)
    {
        foreach (var run in equation.Runs)
        {
            yield return run;
            foreach (var slot in new[]
                     {
                         run.ScriptBaseEquation, run.ScriptSubEquation, run.ScriptSupEquation, run.NumeratorEquation,
                         run.DenominatorEquation, run.RadicandEquation, run.DegreeEquation, run.DelimiterContentEquation,
                         run.FunctionArgumentEquation, run.FunctionNameEquation, run.NAryLowerLimitEquation,
                         run.NAryUpperLimitEquation, run.NAryOperandEquation, run.DecoratorBaseEquation
                     })
            {
                if (slot is null)
                    continue;
                foreach (var nested in AllRuns(slot))
                    yield return nested;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Gallery))]
    public void GalleryEquation_BuildsUpWithoutLiteralSpaces_AndRoundTrips(string name, string linear)
    {
        var equation = EquationLinearFormat.Parse(linear);

        AllRuns(equation).Where(run => run.Kind == MathRunKind.Text).Should().OnlyContain(
            run => !run.Text.Replace(", ", ",").Contains(' '),
            "{0}: spaces typed in linear format only trigger build-up", name);
        AllRuns(equation).Select(run => run.Base + run.Numerator + run.Denominator + run.Sub + run.Sup)
            .Should().OnlyContain(text => !text.Contains(' '));

        var reparsed = EquationLinearFormat.Parse(EquationLinearFormat.ToLinear(equation).Text);
        EquationStructure.AreEquivalent(reparsed, equation, compareLayout: true).Should().BeTrue(name);
    }

    [Fact]
    public void Factorials_StayWithTheirOperand()
    {
        var expansion = EquationLinearFormat.Parse(@"(1+x)^n=1+(nx)/1!+(n(n-1)x^2)/2!+\dots");
        expansion.Runs.Where(run => run.Kind == MathRunKind.Fraction).Select(run => run.Denominator)
            .Should().Equal("1!", "2!");

        var taylor = EquationLinearFormat.Parse(@"e^x=1+x/1!+x^2/2!+x^3/3!");
        taylor.Runs.Where(run => run.Kind == MathRunKind.Fraction).Select(run => run.Denominator)
            .Should().Equal("1!", "2!", "3!");

        var both = EquationLinearFormat.Parse("n!/k!").Runs.Should().ContainSingle().Subject;
        both.Numerator.Should().Be("n!");
        both.Denominator.Should().Be("k!");

        EquationLinearFormat.Parse("f′(x)").LinearText.Should().Be("f′(x)");
    }

    [Fact]
    public void NAryOperand_SpansTheFollowingProduct()
    {
        var binomial = EquationLinearFormat.Parse(@"(x+a)^n=\sum_(k=0)^n\of (n\atop k)x^k a^(n-k)");
        var sum = binomial.Runs.Single(run => run.Kind == MathRunKind.NAry);
        sum.NAryOperandEquation!.Runs.Select(run => run.Kind).Should().Equal(
            MathRunKind.Delimiter, MathRunKind.Superscript, MathRunKind.Superscript);

        var integral = EquationLinearFormat.Parse(@"\int_0^1 f(x) dx+1").Runs;
        integral[0].Kind.Should().Be(MathRunKind.NAry);
        integral[0].NAryOperandEquation!.LinearText.Should().Be("f(x)dx");
        integral[1].Text.Should().Be("+1", "the operand stops at the next operator");
    }

    [Fact]
    public void EscapedSpaces_StayLiteral_AndTextWithSpacesRoundTrips()
    {
        EquationLinearFormat.Parse(@"a\ b").LinearText.Should().Be("a b");
        EquationLinearFormat.Parse("a + b").LinearText.Should().Be("a+b");

        var withSpaces = Equation.FromText("E = m");
        var linear = EquationLinearFormat.ToLinear(withSpaces);
        EquationStructure.AreEquivalent(EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts), withSpaces).Should().BeTrue();
    }
}
