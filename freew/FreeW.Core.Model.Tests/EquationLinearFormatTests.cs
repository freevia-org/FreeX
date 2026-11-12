namespace FreeW.Core.Model.Tests;

public class EquationLinearFormatTests
{
    private static Equation Eq(params MathRun[] runs) => new(runs);

    private static Equation Text(string text) => Equation.FromText(text);

    private static void ShouldParseTo(string linear, params MathRun[] expected)
    {
        var parsed = EquationLinearFormat.Parse(linear);
        EquationStructure.AreEquivalent(parsed, Eq(expected)).Should().BeTrue(
            "'{0}' parsed to '{1}' ({2})",
            linear,
            EquationLinearFormat.ToLinear(parsed).Text,
            string.Join(", ", parsed.Runs.Select(run => run.Kind)));
    }

    private static string ShouldRoundTrip(Equation equation)
    {
        var linear = EquationLinearFormat.ToLinear(equation);
        var parsed = EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts);
        EquationStructure.AreEquivalent(parsed, equation).Should().BeTrue("'{0}' must parse back to the same equation", linear.Text);
        return linear.Text;
    }

    // ── Parsing ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_PlainText_StaysOneTextRun()
    {
        ShouldParseTo("x + 1 = y", MathRun.PlainText("x + 1 = y"));
        EquationLinearFormat.Parse(null).Runs.Should().BeEmpty();
        EquationLinearFormat.Parse(string.Empty).Runs.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SimpleFraction()
    {
        ShouldParseTo("a/b", MathRun.Fraction("a", "b"));
        ShouldParseTo("12/345", MathRun.Fraction("12", "345"));
        ShouldParseTo("1.5/2", MathRun.Fraction("1.5", "2"));
    }

    [Fact]
    public void Parse_Fraction_StripsParenthesesAroundOperands()
    {
        ShouldParseTo("(a+b)/(c+d)", MathRun.Fraction("a+b", "c+d"));
        ShouldParseTo("((a+b))/c", MathRun.Fraction(Eq(MathRun.Delimiter("a+b")), Text("c")));
    }

    [Fact]
    public void Parse_Fraction_TakesOnlyTheAdjacentOperands()
    {
        ShouldParseTo("a+b/c+d", MathRun.PlainText("a+"), MathRun.Fraction("b", "c"), MathRun.PlainText("+d"));
        ShouldParseTo("xy/zw", MathRun.PlainText("x"), MathRun.Fraction("y", "z"), MathRun.PlainText("w"));
    }

    [Fact]
    public void Parse_Fraction_ChainsLeftToRight_AndBindsLooserThanScripts()
    {
        ShouldParseTo("a/b/c", MathRun.Fraction(Eq(MathRun.Fraction("a", "b")), Text("c")));
        ShouldParseTo(
            "x^2/y_1",
            MathRun.Fraction(Eq(MathRun.Superscript("x", "2")), Eq(MathRun.Subscript("y", "1"))));
    }

    [Fact]
    public void Parse_SlashWithoutBothOperands_IsText()
    {
        ShouldParseTo("/x", MathRun.PlainText("/x"));
        ShouldParseTo("a/", MathRun.PlainText("a/"));
        ShouldParseTo("a/+b", MathRun.PlainText("a/+b"));
    }

    [Fact]
    public void Parse_FractionVariants()
    {
        ShouldParseTo("n¦k", MathRun.Fraction("n", "k") with { FractionType = MathFractionType.NoBar });
        ShouldParseTo(@"n\atop k", MathRun.Fraction("n", "k") with { FractionType = MathFractionType.NoBar });
        ShouldParseTo(@"a\sdiv b", MathRun.Fraction("a", "b") with { FractionType = MathFractionType.Skewed });
        ShouldParseTo(@"a\ldiv b", MathRun.Fraction("a", "b") with { FractionType = MathFractionType.Linear });
    }

    [Fact]
    public void Parse_Scripts()
    {
        ShouldParseTo("x^2", MathRun.Superscript("x", "2"));
        ShouldParseTo("x_i", MathRun.Subscript("x", "i"));
        ShouldParseTo("x_i^2", MathRun.SubSuperscript("x", "i", "2"));
        ShouldParseTo("x^2_i", MathRun.SubSuperscript("x", "i", "2"));
        ShouldParseTo("x^(n+1)", MathRun.Superscript("x", "n+1"));
        ShouldParseTo("x^-1", MathRun.Superscript("x", "-1"));
        ShouldParseTo("E=mc^2", MathRun.PlainText("E=m"), MathRun.Superscript("c", "2"));
        ShouldParseTo("x^23", MathRun.Superscript("x", "23"));
        ShouldParseTo("x^ab", MathRun.Superscript("x", "a"), MathRun.PlainText("b"));
    }

    [Fact]
    public void Parse_ScriptBase_KeepsParenthesesAsDelimiter()
    {
        ShouldParseTo("(a+b)^2", MathRun.Superscript(Eq(MathRun.Delimiter("a+b")), Text("2")));
        ShouldParseTo("〖ab〗^2", MathRun.Superscript("ab", "2"));
        ShouldParseTo(@"\begin ab\end ^2", MathRun.Superscript("ab", "2"));
    }

    [Fact]
    public void Parse_RepeatedScripts_NestOnTheBase()
    {
        ShouldParseTo("x^y^z", MathRun.Superscript(Eq(MathRun.Superscript("x", "y")), Text("z")));
    }

    [Fact]
    public void Parse_ScriptOperatorsWithoutOperands_AreText()
    {
        ShouldParseTo("^2", MathRun.PlainText("^2"));
        ShouldParseTo("x^", MathRun.PlainText("x^"));
        ShouldParseTo("a _ b", MathRun.PlainText("a _ b"));
    }

    [Fact]
    public void Parse_Radicals()
    {
        ShouldParseTo(@"\sqrt(x)", MathRun.Radical("x"));
        ShouldParseTo(@"\sqrt x", MathRun.Radical("x"));
        ShouldParseTo("√(a+b)", MathRun.Radical("a+b"));
        ShouldParseTo(@"\sqrt(n&x)", MathRun.Radical("x", "n"));
        ShouldParseTo(@"\cbrt x", MathRun.Radical("x", "3"));
        ShouldParseTo(@"\qdrt(x+1)", MathRun.Radical("x+1", "4"));
        ShouldParseTo(
            @"\sqrt(b^2-4ac)",
            MathRun.Radical(Eq(MathRun.Superscript("b", "2"), MathRun.PlainText("-4ac"))));
        ShouldParseTo("√", MathRun.PlainText("√"));
    }

    [Fact]
    public void Parse_Delimiters()
    {
        ShouldParseTo("(a+b)", MathRun.Delimiter("a+b"));
        ShouldParseTo("[a+b]", MathRun.Delimiter("a+b", "[", "]"));
        ShouldParseTo("{a+b}", MathRun.Delimiter("a+b", "{", "}"));
        ShouldParseTo("|x|", MathRun.Delimiter("x", "|", "|"));
        ShouldParseTo(@"\langle x\rangle ", MathRun.Delimiter("x", "⟨", "⟩"));
        ShouldParseTo(@"\lfloor x\rfloor ", MathRun.Delimiter("x", "⌊", "⌋"));
        ShouldParseTo("(a,b]", MathRun.Delimiter("a,b", "(", "]"));
        ShouldParseTo("f(x)", MathRun.PlainText("f"), MathRun.Delimiter("x"));
        ShouldParseTo("(a/b)", MathRun.Delimiter(Eq(MathRun.Fraction("a", "b"))));
    }

    [Fact]
    public void Parse_Delimiters_ExplicitGlyphsAndInvisibleSides()
    {
        ShouldParseTo(@"\open|x\close|", MathRun.Delimiter("x", "|", "|"));
        ShouldParseTo("├]a┤[", MathRun.Delimiter("a", "]", "["));
        ShouldParseTo("{a〗", MathRun.Delimiter("a", "{", string.Empty));
        ShouldParseTo("〖a)", MathRun.Delimiter("a", string.Empty, ")"));
    }

    [Fact]
    public void Parse_Delimiters_MultipleArguments()
    {
        ShouldParseTo("(a│b)", MathRun.Delimiter(["a", "b"], "(", ")", "|"));
        ShouldParseTo(@"\langle a\vbar b\vbar c\rangle ", MathRun.Delimiter(["a", "b", "c"], "⟨", "⟩", "|"));
    }

    [Fact]
    public void Parse_UnbalancedBrackets_AreText()
    {
        ShouldParseTo("(a+b", MathRun.PlainText("(a+b"));
        ShouldParseTo("a+b)", MathRun.PlainText("a+b)"));
        ShouldParseTo("a|b", MathRun.PlainText("a|b"));
        ShouldParseTo("P(A|B)", MathRun.PlainText("P"), MathRun.Delimiter("A|B"));
    }

    [Fact]
    public void Parse_NAryOperators()
    {
        ShouldParseTo(@"\sum_(i=1)^n i", MathRun.NAry("∑", "i=1", "n", "i"));
        ShouldParseTo(@"\int_a^b f", MathRun.NAry("∫", "a", "b", "f"));
        ShouldParseTo(@"\prod_(k=1)^n\of a_k", new MathRun
        {
            Kind = MathRunKind.NAry,
            Operator = "∏",
            Sub = "k=1",
            Sup = "n",
            Base = "a_k",
            NAryOperandEquation = Eq(MathRun.Subscript("a", "k"))
        });
        ShouldParseTo("∑_(i=1)^n▒〖a_i b_i〗", new MathRun
        {
            Kind = MathRunKind.NAry,
            Operator = "∑",
            Sub = "i=1",
            Sup = "n",
            NAryOperandEquation = Eq(MathRun.Subscript("a", "i"), MathRun.PlainText(" "), MathRun.Subscript("b", "i"))
        });
        ShouldParseTo(@"\int\of x", MathRun.NAry("∫", string.Empty, string.Empty, "x"));
        ShouldParseTo(@"\sum_i", MathRun.NAry("∑", "i", string.Empty, string.Empty));
    }

    [Fact]
    public void Parse_BareNAryGlyph_IsText()
    {
        ShouldParseTo("∑ x", MathRun.PlainText("∑ x"));
    }

    [Fact]
    public void Parse_Functions()
    {
        ShouldParseTo("sin x", MathRun.FunctionApply("sin", "x"));
        ShouldParseTo("cos⁡x", MathRun.FunctionApply("cos", "x"));
        ShouldParseTo("sin(x)", MathRun.FunctionApply("sin", Eq(MathRun.Delimiter("x"))));
        ShouldParseTo("sin θ", MathRun.FunctionApply("sin", "θ"));
        ShouldParseTo(@"cos \theta", MathRun.FunctionApply("cos", "θ"));
        ShouldParseTo("f⁡x", MathRun.FunctionApply("f", "x"));
        ShouldParseTo("〖sgn〗⁡x", MathRun.FunctionApply("sgn", "x"));
        ShouldParseTo("sin cos x", MathRun.FunctionApply("sin", Eq(MathRun.FunctionApply("cos", "x"))));
        ShouldParseTo("sin x/2", MathRun.Fraction(Eq(MathRun.FunctionApply("sin", "x")), Text("2")));
    }

    [Fact]
    public void Parse_FunctionNames_OnlyAsWholeWords()
    {
        ShouldParseTo("sinx", MathRun.PlainText("sinx"));
        ShouldParseTo("asin x", MathRun.PlainText("asin x"));
        ShouldParseTo("sin", MathRun.PlainText("sin"));
        ShouldParseTo("sin + x", MathRun.PlainText("sin + x"));
    }

    [Fact]
    public void Parse_FunctionWithScriptedName()
    {
        ShouldParseTo("sin^2 x", new MathRun
        {
            Kind = MathRunKind.FunctionApply,
            FuncName = "sin^2",
            FunctionNameEquation = Eq(MathRun.Superscript("sin", "2")),
            Base = "x"
        });
    }

    [Fact]
    public void Parse_Limits()
    {
        var limit = new MathRun
        {
            Kind = MathRunKind.FunctionApply,
            FunctionNameEquation = Eq(MathRun.LowerLimit("lim", "x→0")),
            Base = "f"
        };
        ShouldParseTo("lim_(x->0) f", limit);
        ShouldParseTo(@"lim_(x\rightarrow 0) f", limit);
        ShouldParseTo("lim┬(x→0)⁡f", limit);
        ShouldParseTo(@"a\below b", MathRun.LowerLimit("a", "b"));
        ShouldParseTo(@"a\above b", MathRun.UpperLimit("a", "b"));
        ShouldParseTo("max_i", MathRun.LowerLimit("max", "i"));
        ShouldParseTo("〖lim〗_n", MathRun.Subscript("lim", "n"));
    }

    [Fact]
    public void Parse_Accents()
    {
        ShouldParseTo(@"x\hat", MathRun.AccentOf("x", "̂"));
        ShouldParseTo(@"x\bar", MathRun.AccentOf("x", "̅"));
        ShouldParseTo(@"x\vec", MathRun.AccentOf("x", "⃗"));
        ShouldParseTo(@"(a+b)\tilde", MathRun.AccentOf("a+b", "̃"));
        ShouldParseTo(@"\hat(x)", MathRun.AccentOf("x", "̂"));
        ShouldParseTo(@"\vec(AB)", MathRun.AccentOf("AB", "⃗"));
        ShouldParseTo(@"x\dot\dot", MathRun.AccentOf(Eq(MathRun.AccentOf("x", "̇")), "̇"));
        ShouldParseTo(@"x\hat^2", MathRun.Superscript(Eq(MathRun.AccentOf("x", "̂")), Text("2")));
    }

    [Fact]
    public void Parse_BarsAndGroupCharacters()
    {
        ShouldParseTo(@"\overbar(x+y)", MathRun.BarOf("x+y"));
        ShouldParseTo(@"\underbar(x)", MathRun.BarOf("x", top: false));
        ShouldParseTo(@"\overbrace(x+y)", MathRun.GroupCharOf("x+y", "⏞", "top"));
        ShouldParseTo(@"\underbrace(x+y)", MathRun.GroupCharOf("x+y", "⏟", "bot"));
    }

    [Fact]
    public void Parse_Matrices()
    {
        ShouldParseTo(@"\matrix(a&b@c&d)", MathRun.MatrixOf(new MathMatrix([["a", "b"], ["c", "d"]])));
        ShouldParseTo("■(1&0@0&1)", MathRun.MatrixOf(MathMatrix.Identity2x2()));
        ShouldParseTo(
            "(■(a&b@c&d))",
            MathRun.Delimiter(Eq(MathRun.MatrixOf(new MathMatrix([["a", "b"], ["c", "d"]])))));
        ShouldParseTo(
            "■(a/b&c)",
            MathRun.MatrixOf(MathMatrix.FromCellEquations([[Eq(MathRun.Fraction("a", "b")), Text("c")]])));
        ShouldParseTo(@"\eqarray(x=1@y=2)", MathRun.EquationArrayOf(new MathMatrix([["x=1"], ["y=2"]])));
        ShouldParseTo("■x", MathRun.PlainText("■x"));
    }

    [Fact]
    public void Parse_GreekAndSymbolNames()
    {
        ShouldParseTo(@"\alpha+\beta=\gamma", MathRun.PlainText("α+β=γ"));
        ShouldParseTo(@"\omega\Omega", MathRun.PlainText("ωΩ"));
        ShouldParseTo(
            @"\infty\pm\times\leq\geq\neq\approx\rightarrow\partial\nabla",
            MathRun.PlainText("∞±×≤≥≠≈→∂∇"));
    }

    [Fact]
    public void Parse_NameSwallowsOneFollowingSpace_AndUnknownNamesStayLiteral()
    {
        ShouldParseTo(@"\alpha x", MathRun.PlainText("αx"));
        ShouldParseTo(@"\alpha  x", MathRun.PlainText("α x"));
        ShouldParseTo(@"\foo x", MathRun.PlainText(@"\foo x"));
        ShouldParseTo(@"x\", MathRun.PlainText(@"x\"));
    }

    [Fact]
    public void Parse_EscapesAndQuotes_AreLiteral()
    {
        ShouldParseTo(@"a\/b", MathRun.PlainText("a/b"));
        ShouldParseTo(@"x\^2", MathRun.PlainText("x^2"));
        ShouldParseTo(@"f\(x\)", MathRun.PlainText("f(x)"));
        ShouldParseTo("\"sin x\"", MathRun.PlainText("sin x"));
        ShouldParseTo("\"a/b\" c", MathRun.PlainText("a/b c"));
        ShouldParseTo("\"say \\\"hi\\\"\"", MathRun.PlainText("say \"hi\""));
        ShouldParseTo("a\"b", MathRun.PlainText("a\"b"));
        ShouldParseTo(@"\\alpha", MathRun.PlainText(@"\alpha"));
    }

    [Fact]
    public void Parse_AutocorrectDigraphs()
    {
        ShouldParseTo("a->b", MathRun.PlainText("a→b"));
        ShouldParseTo("a<=b>=c", MathRun.PlainText("a≤b≥c"));
        ShouldParseTo("a+-b", MathRun.PlainText("a±b"));
    }

    [Fact]
    public void Parse_QuadraticFormula()
    {
        ShouldParseTo(
            @"x=(-b\pm\sqrt(b^2-4ac))/(2a)",
            MathRun.PlainText("x="),
            MathRun.Fraction(
                Eq(
                    MathRun.PlainText("-b±"),
                    MathRun.Radical(Eq(MathRun.Superscript("b", "2"), MathRun.PlainText("-4ac")))),
                Text("2a")));
    }

    public static TheoryData<int> HostileInputIndexes()
    {
        var data = new TheoryData<int>();
        for (var index = 0; index < HostileInputs.Length; index++)
            data.Add(index);
        return data;
    }

    private static readonly string[] HostileInputs =
    [
        new string('(', 5000),
        new string('(', 300) + "x" + new string(')', 300),
        string.Concat(Enumerable.Repeat("a/", 5000)),
        string.Concat(Enumerable.Repeat("x^", 5000)) + "x",
        string.Concat(Enumerable.Repeat("√", 5000)) + "x",
        string.Concat(Enumerable.Repeat("f⁡", 5000)) + "x",
        "x" + string.Concat(Enumerable.Repeat("̂", 5000)),
        string.Concat(Enumerable.Repeat("∑_", 2000)) + "x",
        string.Concat(Enumerable.Repeat("sin ", 2000)) + "x",
        string.Concat(Enumerable.Repeat("■(", 200)) + "x" + new string(')', 200),
        "├", "┤", "├(", "┤)", "\\", "\"", "⟦0⟧", "⟦", "⟦999999999999⟧",
        "■(", "■()", "&@&@", "a" + (char)0xD800, (char)0xDC00 + "x", "|||||", "││", "▒", "⁡", "_^_^",
    ];

    [Theory]
    [MemberData(nameof(HostileInputIndexes))]
    public void Parse_NeverThrows_OnHostileInput(int index)
    {
        var input = HostileInputs[index];
        var parse = () => EquationLinearFormat.Parse(input);
        var equation = parse.Should().NotThrow().Subject;

        var linear = EquationLinearFormat.ToLinear(equation);
        EquationStructure.AreEquivalent(EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts), equation).Should().BeTrue();
    }

    [Fact]
    public void Parse_OpaqueToken_ResolvesAgainstTheSuppliedParts()
    {
        var preserved = MathRun.PreservedOmml("<m:box xmlns:m=\"m\"/>", "x");
        var parsed = EquationLinearFormat.Parse("a+⟦0⟧/2", [preserved]);

        parsed.Runs.Should().HaveCount(2);
        parsed.Runs[0].Should().Be(MathRun.PlainText("a+"));
        parsed.Runs[1].Kind.Should().Be(MathRunKind.Fraction);
        parsed.Runs[1].NumeratorEquation!.Runs.Should().ContainSingle().Which.Should().BeSameAs(preserved);

        ShouldParseTo("⟦0⟧", MathRun.PlainText("⟦0⟧"));
        EquationLinearFormat.Parse("⟦1⟧", [preserved]).LinearText.Should().Be("⟦1⟧");
    }

    // ── Serializing ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a/b")]
    [InlineData("(a+b)/(c+d)")]
    [InlineData("x^2")]
    [InlineData("x_i")]
    [InlineData("x_i^2")]
    [InlineData("x^(n+1)")]
    [InlineData("(a+b)^2")]
    [InlineData("√(x)")]
    [InlineData("√(n&x)")]
    [InlineData("[a+b]")]
    [InlineData("{a+b}")]
    [InlineData("|x|")]
    [InlineData("∑_(i=1)^n▒i")]
    [InlineData("∫_a^b▒x")]
    [InlineData("sin⁡x")]
    [InlineData("lim┬(x→0)⁡f")]
    [InlineData("x̂")]
    [InlineData("(a+b)⃗")]
    [InlineData("¯(x+y)")]
    [InlineData("▁(x)")]
    [InlineData("⏞(x+y)")]
    [InlineData("■(a&b@c&d)")]
    [InlineData("█(x=1@y=2)")]
    [InlineData("n¦k")]
    [InlineData("x=(-b±√(b^2-4ac))/(2a)")]
    [InlineData("α+β≤∞")]
    [InlineData("(a│b)")]
    [InlineData("〖ab〗^2")]
    [InlineData("a^2+b^2=c^2")]
    public void ToLinear_IsStableForCanonicalLinearText(string linear)
    {
        var equation = EquationLinearFormat.Parse(linear);
        var written = EquationLinearFormat.ToLinear(equation);

        written.Text.Should().Be(linear);
        written.HasOpaqueParts.Should().BeFalse();
    }

    [Fact]
    public void ToLinear_WritesTheModelFactoriesReadably()
    {
        EquationLinearFormat.ToLinear(Eq(MathRun.Fraction("a", "b"))).Text.Should().Be("a/b");
        EquationLinearFormat.ToLinear(Eq(MathRun.SubSuperscript("x", "n", "2"))).Text.Should().Be("x_n^2");
        EquationLinearFormat.ToLinear(Eq(MathRun.Radical("x", "n"))).Text.Should().Be("√(n&x)");
        EquationLinearFormat.ToLinear(Eq(MathRun.NAry("∑", "i=1", "n", "i"))).Text.Should().Be("∑_(i=1)^n▒i");
        EquationLinearFormat.ToLinear(Eq(MathRun.MatrixOf(MathMatrix.Identity2x2()))).Text.Should().Be("■(1&0@0&1)");
        EquationLinearFormat.ToLinear(Eq(MathRun.PlainText("E = m"), MathRun.Superscript("c", "2"))).Text.Should().Be("E = mc^2");
    }

    [Fact]
    public void ToLinear_EscapesTextThatWouldOtherwiseBuildUp()
    {
        ShouldRoundTrip(Text("a/b")).Should().Be(@"a\/b");
        ShouldRoundTrip(Text("f(x) dx")).Should().Be(@"f\(x\) dx");
        ShouldRoundTrip(Text("x^2")).Should().Be(@"x\^2");
        ShouldRoundTrip(Text("sin x")).Should().Be("\"sin x\"");
        ShouldRoundTrip(Text("a->b")).Should().Be("\"a->b\"");
        ShouldRoundTrip(Text(@"\alpha")).Should().Be(@"\\alpha");
        ShouldRoundTrip(Text("a&b@c")).Should().Be(@"a\&b\@c");
        ShouldRoundTrip(Text("say \"hi\"")).Should().Be("say \\\"hi\\\"");
    }

    [Fact]
    public void ToLinear_IsolatesNeighboursThatWouldMerge()
    {
        // "1" followed by 2/3 must not read back as 12/3.
        ShouldRoundTrip(Eq(MathRun.PlainText("1"), MathRun.Fraction("2", "3")));
        ShouldRoundTrip(Eq(MathRun.Fraction("a", "1"), MathRun.PlainText("2")));
        ShouldRoundTrip(Eq(MathRun.Superscript("x", "2"), MathRun.PlainText("3")));
        ShouldRoundTrip(Eq(MathRun.PlainText("1"), MathRun.Superscript("2", "3")));
        ShouldRoundTrip(Eq(MathRun.PlainText("a"), MathRun.FunctionApply("sin", "x")));
        ShouldRoundTrip(Eq(MathRun.PlainText("sin"), MathRun.Delimiter("x")));
        ShouldRoundTrip(Eq(MathRun.PlainText("|"), MathRun.Fraction("a", "b"), MathRun.PlainText("|")));
        ShouldRoundTrip(Eq(MathRun.Fraction("a", "b"), MathRun.PlainText("^2")));
        ShouldRoundTrip(Eq(MathRun.Fraction("a", "b"), MathRun.Fraction("c", "d")));
        ShouldRoundTrip(Eq(MathRun.Superscript("x", "l"), MathRun.PlainText("n 2")));
        ShouldRoundTrip(Eq(MathRun.NAry("∑", "i", "n", "x"), MathRun.PlainText("y")));
        ShouldRoundTrip(Eq(MathRun.PlainText("x"), MathRun.AccentOf("y"), MathRun.PlainText("z")));
    }

    [Fact]
    public void ToLinear_TreatsSplitTextAndTextSlotsAsTheirMergedForm()
    {
        var split = Eq(MathRun.PlainText("a"), MathRun.PlainText(string.Empty), MathRun.PlainText("+b"));
        EquationLinearFormat.ToLinear(split).Text.Should().Be("a+b");

        var structuredSlots = MathRun.Fraction(Eq(MathRun.PlainText("a"), MathRun.PlainText("b")), Text("c"));
        EquationStructure.AreEquivalent(Eq(structuredSlots), Eq(MathRun.Fraction("ab", "c"))).Should().BeTrue();
        EquationLinearFormat.ToLinear(Eq(structuredSlots)).Text.Should().Be("(ab)/c");
    }

    [Fact]
    public void ToLinear_KeepsWhatItCannotExpressAsOpaqueTokens()
    {
        var preserved = MathRun.PreservedOmml("<m:box xmlns:m=\"m\"><m:e/></m:box>", "boxed");
        var oddAccent = MathRun.AccentOf("x", "!!");
        var oddOperator = MathRun.NAry("⨌", "a", "b", "f");
        var commaSeparated = MathRun.Delimiter(["a", "b"], "(", ")", ",");
        var misplacedBrace = MathRun.GroupCharOf("x", "⏞", "bot");
        var emptyMatrix = MathRun.MatrixOf(new MathMatrix());
        var wideArrayRow = MathRun.EquationArrayOf(new MathMatrix([["a", "b"]]));
        var longDelimiterGlyph = MathRun.Delimiter("x", "((", "))");

        foreach (var run in new[] { preserved, oddAccent, oddOperator, commaSeparated, misplacedBrace, emptyMatrix, wideArrayRow, longDelimiterGlyph })
        {
            var equation = Eq(MathRun.PlainText("y="), run, MathRun.PlainText("+1"));
            var linear = EquationLinearFormat.ToLinear(equation);

            linear.HasOpaqueParts.Should().BeTrue("{0} cannot be written as linear text", run.Kind);
            linear.Text.Should().StartWith("y=").And.EndWith("+1");
            var parsed = EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts);
            parsed.Runs.Should().HaveCount(3);
            parsed.Runs[1].Should().BeSameAs(run, "the opaque part must come back untouched");
        }
    }

    [Fact]
    public void ToLinear_OpaquePartNestedInAStructure_SurvivesEditingAroundIt()
    {
        var preserved = MathRun.PreservedOmml("<m:phant xmlns:m=\"m\"/>", "p");
        var equation = Eq(MathRun.Fraction(Eq(MathRun.PlainText("a+"), preserved), Text("b")));
        var linear = EquationLinearFormat.ToLinear(equation);

        linear.Text.Should().Be("(a+⟦0⟧)/b");
        var edited = EquationLinearFormat.Parse(linear.Text.Replace("/b", "/(b+c)^2"), linear.OpaqueParts);
        var fraction = edited.Runs.Should().ContainSingle().Subject;
        fraction.NumeratorEquation!.Runs[1].Should().BeSameAs(preserved);
        fraction.DenominatorEquation!.Runs.Should().ContainSingle().Which.Kind.Should().Be(MathRunKind.Superscript);
    }

    // ── Round trip ───────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, Equation> RoundTripCorpus()
    {
        var data = new TheoryData<string, Equation>();
        void Add(string name, params MathRun[] runs) => data.Add(name, Eq(runs));

        Add("empty");
        Add("text", MathRun.PlainText("x + 1"));
        Add("text with every structural character", MathRun.PlainText("\\ \" / ^ _ ( ) [ ] { } | & @ √ ■ █ ▒ ⁡ │ ├ ┤ 〖 〗 ⟦ 0 ⟧ ∑ ̂ ⏞ ¯ ▁ ┬ ┴ ¦"));
        Add("text function words", MathRun.PlainText("sin x cos(y) lim_n max"));
        Add("text digraphs", MathRun.PlainText("a -> b <= c >= d +- e"));
        Add("text unknown name", MathRun.PlainText(@"\foo \alpha"));
        Add("text surrogate pair", MathRun.PlainText("𝑥+𝑦"));
        Add("text lone surrogate", MathRun.PlainText("a" + (char)0xD800 + "b"));
        Add("superscript", MathRun.Superscript("x", "2"));
        Add("superscript empty slots", MathRun.Superscript(string.Empty, string.Empty));
        Add("superscript long base", MathRun.Superscript("abc", "def"));
        Add("superscript function base", MathRun.Superscript("sin", "2"));
        Add("subscript", MathRun.Subscript("x", "i"));
        Add("subscript of lim is not a limit", MathRun.Subscript("lim", "n"));
        Add("subsuperscript", MathRun.SubSuperscript("x", "i", "2"));
        Add("nested scripts", MathRun.Superscript(Eq(MathRun.Subscript("x", "1")), Eq(MathRun.Superscript("y", "z"))));
        Add("script of delimiter", MathRun.Superscript(Eq(MathRun.Delimiter("a+b")), Text("2")));
        Add("fraction", MathRun.Fraction("a", "b"));
        Add("fraction empty", MathRun.Fraction(string.Empty, string.Empty));
        Add("fraction numbers", MathRun.Fraction("12", "3.5"));
        Add("fraction compound", MathRun.Fraction("a+b", "c+d"));
        Add("fraction of delimiter", MathRun.Fraction(Eq(MathRun.Delimiter("a")), Eq(MathRun.Delimiter("b", "[", "]"))));
        Add("fraction nested", MathRun.Fraction(Eq(MathRun.Fraction("a", "b")), Eq(MathRun.Fraction("c", "d"))));
        Add("fraction no bar", MathRun.Fraction("n", "k") with { FractionType = MathFractionType.NoBar });
        Add("fraction skewed", MathRun.Fraction("a", "b") with { FractionType = MathFractionType.Skewed });
        Add("fraction linear", MathRun.Fraction("a", "b") with { FractionType = MathFractionType.Linear });
        Add("radical", MathRun.Radical("x"));
        Add("radical empty", MathRun.Radical(string.Empty));
        Add("radical nth", MathRun.Radical("x", "n"));
        Add("radical ampersand", MathRun.Radical("a&b", "c&d"));
        Add("radical structured", MathRun.Radical(Eq(MathRun.Fraction("a", "b")), Eq(MathRun.Superscript("n", "2"))));
        Add("nary sum", MathRun.NAry("∑", "i=1", "n", "i"));
        Add("nary integral", MathRun.NAry("∫", "a", "b", "f(x) dx"));
        Add("nary no limits", MathRun.NAry("∫", string.Empty, string.Empty, "x"));
        Add("nary all empty", MathRun.NAry("∏", string.Empty, string.Empty, string.Empty));
        Add("nary structured", MathRun.NAry("∑", Eq(MathRun.PlainText("k="), MathRun.Fraction("1", "2")), null, Eq(MathRun.Subscript("a", "k"))));
        Add("accent", MathRun.AccentOf("x"));
        Add("accent vector", MathRun.AccentOf("AB", "⃗"));
        Add("accent structured", MathRun.AccentOf(Eq(MathRun.Fraction("a", "b")), "̃"));
        Add("accent stacked", MathRun.AccentOf(Eq(MathRun.AccentOf("x", "̇")), "̂"));
        Add("bar over", MathRun.BarOf("x+y"));
        Add("bar under", MathRun.BarOf("x", top: false));
        Add("group char over", MathRun.GroupCharOf("x+y"));
        Add("group char under", MathRun.GroupCharOf("x+y", "⏟", "bot"));
        Add("delimiter", MathRun.Delimiter("a, b"));
        Add("delimiter empty", MathRun.Delimiter(string.Empty));
        Add("delimiter square", MathRun.Delimiter("a", "[", "]"));
        Add("delimiter mixed", MathRun.Delimiter("a", "[", ")"));
        Add("delimiter bars", MathRun.Delimiter("x", "|", "|"));
        Add("delimiter double bars", MathRun.Delimiter("x", "‖", "‖"));
        Add("delimiter reversed brackets", MathRun.Delimiter("a", "]", "["));
        Add("delimiter open only", MathRun.Delimiter("a", "{", string.Empty));
        Add("delimiter close only", MathRun.Delimiter("a", string.Empty, ")"));
        Add("delimiter odd glyphs", MathRun.Delimiter("a", "↑", "\\"));
        Add("delimiter arguments", MathRun.Delimiter(["a", "b", "c"], "(", ")", "|"));
        Add("delimiter nested", MathRun.Delimiter(Eq(MathRun.Delimiter(Eq(MathRun.Delimiter("x", "|", "|")), "[", "]"))));
        Add("matrix", MathRun.MatrixOf(MathMatrix.Identity2x2()));
        Add("matrix ragged", MathRun.MatrixOf(new MathMatrix([["a", "b", "c"], ["d"], []])));
        Add("matrix structured", MathRun.MatrixOf(MathMatrix.FromCellEquations([[Eq(MathRun.Fraction("a", "b")), null], [Text("c&d"), Eq(MathRun.Radical("x"))]])));
        Add("equation array", MathRun.EquationArrayOf(new MathMatrix([["x=1"], ["y&=2@"]])));
        Add("function", MathRun.FunctionApply("sin", "x"));
        Add("function unknown name", MathRun.FunctionApply("sgn", "x"));
        Add("function empty", MathRun.FunctionApply(string.Empty, string.Empty));
        Add("function structured argument", MathRun.FunctionApply("cos", Eq(MathRun.Fraction("x", "2"))));
        Add("function delimiter argument", MathRun.FunctionApply("f", Eq(MathRun.Delimiter("x"))));
        Add("function limit name", new MathRun
        {
            Kind = MathRunKind.FunctionApply,
            FunctionNameEquation = Eq(MathRun.LowerLimit("lim", "x→0")),
            FunctionArgumentEquation = Eq(MathRun.Fraction(Eq(MathRun.FunctionApply("sin", "x")), Text("x")))
        });
        Add("lower limit", MathRun.LowerLimit("lim", "n→∞"));
        Add("lower limit odd base", MathRun.LowerLimit("a+b", string.Empty));
        Add("upper limit", MathRun.UpperLimit("x", "def"));
        Add("default equation", MathRun.PlainText("E = m"), MathRun.Superscript("c", "2"));
        Add(
            "quadratic formula",
            MathRun.PlainText("x="),
            MathRun.Fraction(
                Eq(MathRun.PlainText("-b±"), MathRun.Radical(Eq(MathRun.Superscript("b", "2"), MathRun.PlainText("-4ac")))),
                Text("2a")));
        Add("adjacent structures", MathRun.Fraction("1", "2"), MathRun.Fraction("3", "4"), MathRun.Superscript("5", "6"), MathRun.PlainText("7"), MathRun.Radical("8"), MathRun.PlainText("9"));
        Add("text between bars", MathRun.PlainText("|"), MathRun.Superscript("x", "2"), MathRun.PlainText("|+("), MathRun.Fraction("a", "b"), MathRun.PlainText(")"));
        Add("preserved", MathRun.PlainText("a"), MathRun.PreservedOmml("<m:box xmlns:m=\"m\"/>", "b"), MathRun.PlainText("/c"));
        return data;
    }

    [Theory]
    [MemberData(nameof(RoundTripCorpus))]
    public void RoundTrip_ModelToLinearToModel_IsIdentity(string name, Equation equation)
    {
        name.Should().NotBeNull();
        ShouldRoundTrip(equation);
    }

    [Fact]
    public void RoundTrip_HoldsForRandomEquations()
    {
        var random = new Random(20261112);
        var opaque = 0;
        const int iterations = 600;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var equation = RandomEquation(random, depth: 0, maxRuns: 4);
            var linear = EquationLinearFormat.ToLinear(equation);
            var parsed = EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts);

            EquationStructure.AreEquivalent(parsed, equation).Should().BeTrue(
                "iteration {0}: '{1}' must parse back to the same equation",
                iteration,
                linear.Text);
            if (linear.HasOpaqueParts)
                opaque++;
        }

        // The generator only builds structures the linear format can express, so the opaque fallback
        // must stay the exception (it covers e.g. a zero-column matrix row), never the rule.
        opaque.Should().BeLessThan(iterations / 20);
    }

    [Fact]
    public void RoundTrip_LinearToModelToLinear_IsAFixedPointAfterOnePass()
    {
        var random = new Random(7);
        const string alphabet = "ab12 +-/^_()[]{}|&@\\\"√∑■▒⁡̂〖〗.,=sincolmx";
        for (var iteration = 0; iteration < 600; iteration++)
        {
            var typed = new string(Enumerable.Range(0, random.Next(1, 24))
                .Select(_ => alphabet[random.Next(alphabet.Length)])
                .ToArray());

            var first = EquationLinearFormat.Parse(typed);
            var written = EquationLinearFormat.ToLinear(first);
            var second = EquationLinearFormat.Parse(written.Text, written.OpaqueParts);

            written.HasOpaqueParts.Should().BeFalse("everything that can be typed ('{0}') can be written back", typed);
            EquationStructure.AreEquivalent(first, second).Should().BeTrue("typed '{0}' was written as '{1}'", typed, written.Text);
            EquationLinearFormat.ToLinear(second).Text.Should().Be(written.Text, "writing '{0}' must be stable", typed);
        }
    }

    [Fact]
    public void RoundTrip_DeeplyNestedEquation_DoesNotBlowUp()
    {
        var equation = Text("x");
        for (var level = 0; level < 40; level++)
            equation = Eq(MathRun.Subscript(Text("lim"), equation));

        var linear = EquationLinearFormat.ToLinear(equation);
        var parsed = EquationLinearFormat.Parse(linear.Text, linear.OpaqueParts);

        EquationStructure.AreEquivalent(parsed, equation).Should().BeTrue();
    }

    private static readonly string[] TextSamples =
    [
        "x", "y", "ab", "12", "3.5", "a+b", " ", "= ", "sin", "lim", "f(x)", "a/b", "x^2", "|", "(", ")", "&", "@",
        "α", "∞", "-", "n→∞", "\"q\"", "\\", "1", "2", "dx", "max ", "∑", "̂", ",",
    ];

    private static Equation RandomEquation(Random random, int depth, int maxRuns)
    {
        var equation = new Equation();
        var count = random.Next(0, maxRuns + 1);
        for (var index = 0; index < count; index++)
            equation.Runs.Add(RandomRun(random, depth));
        return equation;
    }

    private static string RandomText(Random random) => TextSamples[random.Next(TextSamples.Length)];

    private static (string Text, Equation? Equation) RandomSlot(Random random, int depth)
    {
        if (depth >= 3 || random.Next(3) > 0)
            return (random.Next(5) == 0 ? string.Empty : RandomText(random), null);

        var equation = RandomEquation(random, depth + 1, maxRuns: 3);
        return (equation.LinearText, equation);
    }

    private static MathRun RandomRun(Random random, int depth)
    {
        var (aText, aEquation) = RandomSlot(random, depth);
        var (bText, bEquation) = RandomSlot(random, depth);
        var (cText, cEquation) = RandomSlot(random, depth);
        switch (random.Next(16))
        {
            case 0:
                return new MathRun { Kind = MathRunKind.Superscript, Base = aText, ScriptBaseEquation = aEquation, Sup = bText, ScriptSupEquation = bEquation };
            case 1:
                return new MathRun { Kind = MathRunKind.Subscript, Base = aText, ScriptBaseEquation = aEquation, Sub = bText, ScriptSubEquation = bEquation };
            case 2:
                return new MathRun { Kind = MathRunKind.SubSuperscript, Base = aText, ScriptBaseEquation = aEquation, Sub = bText, ScriptSubEquation = bEquation, Sup = cText, ScriptSupEquation = cEquation };
            case 3:
                return new MathRun { Kind = MathRunKind.Fraction, FractionType = (MathFractionType)random.Next(4), Numerator = aText, NumeratorEquation = aEquation, Denominator = bText, DenominatorEquation = bEquation };
            case 4:
                return new MathRun { Kind = MathRunKind.Radical, Base = aText, RadicandEquation = aEquation, Degree = bText, DegreeEquation = bEquation };
            case 5:
                return new MathRun { Kind = MathRunKind.NAry, Operator = random.Next(2) == 0 ? "∑" : "∫", Sub = aText, NAryLowerLimitEquation = aEquation, Sup = bText, NAryUpperLimitEquation = bEquation, Base = cText, NAryOperandEquation = cEquation };
            case 6:
                return new MathRun { Kind = MathRunKind.Accent, Accent = random.Next(2) == 0 ? "̂" : "⃗", Base = aText, DecoratorBaseEquation = aEquation };
            case 7:
                return new MathRun { Kind = MathRunKind.Bar, BarTop = random.Next(2) == 0, Base = aText, DecoratorBaseEquation = aEquation };
            case 8:
                return new MathRun { Kind = MathRunKind.GroupChar, GroupChr = "⏟", GroupChrPos = "bot", Base = aText, DecoratorBaseEquation = aEquation };
            case 9:
            {
                string[] opens = ["(", "[", "{", "|", string.Empty, "⟨", "<"];
                string[] closes = [")", "]", "}", "|", string.Empty, "⟩", ">"];
                var open = opens[random.Next(opens.Length)];
                var close = closes[random.Next(closes.Length)];
                if (open.Length == 0 && close.Length == 0)
                    close = ")";
                var delimiter = new MathRun { Kind = MathRunKind.Delimiter, OpenChar = open, CloseChar = close, Base = aText, DelimiterContentEquation = aEquation };
                return random.Next(4) == 0
                    ? delimiter with
                    {
                        AdditionalDelimiterArguments = [bText],
                        AdditionalDelimiterContentEquations = [bEquation],
                        DelimiterSeparator = "|"
                    }
                    : delimiter;
            }
            case 10:
            {
                var matrix = MathMatrix.FromCellEquations(
                [
                    [aEquation ?? Equation.FromText(aText), bEquation ?? Equation.FromText(bText)],
                    [cEquation ?? Equation.FromText(cText)],
                ]);
                return MathRun.MatrixOf(matrix);
            }
            case 11:
                return MathRun.EquationArrayOf(MathMatrix.FromCellEquations(
                [
                    [aEquation ?? Equation.FromText(aText)],
                    [bEquation ?? Equation.FromText(bText)],
                ]));
            case 12:
                return new MathRun { Kind = MathRunKind.FunctionApply, FuncName = aText, FunctionNameEquation = aEquation, Base = bText, FunctionArgumentEquation = bEquation };
            case 13:
                return new MathRun { Kind = MathRunKind.LowerLimit, Base = aText, ScriptBaseEquation = aEquation, Sub = bText, ScriptSubEquation = bEquation };
            case 14:
                return new MathRun { Kind = MathRunKind.UpperLimit, Base = aText, ScriptBaseEquation = aEquation, Sup = bText, ScriptSupEquation = bEquation };
            default:
                return MathRun.PlainText(RandomText(random));
        }
    }

    // ── Names and structure helpers ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ExpandNames_ReplacesKnownNamesOnly()
    {
        EquationLinearFormat.ExpandNames(@"\alpha x+\sqrt(y)\foo\/z\").Should().Be("αx+√(y)\\foo\\/z\\");
        EquationLinearFormat.ExpandNames(null).Should().BeEmpty();
    }

    [Fact]
    public void Symbols_CoverTheGreekAlphabetAndCommonOperators()
    {
        foreach (var name in new[]
                 {
                     "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa", "lambda", "mu",
                     "nu", "xi", "omicron", "pi", "rho", "sigma", "tau", "upsilon", "phi", "chi", "psi", "omega",
                     "infty", "pm", "times", "leq", "geq", "neq", "approx", "rightarrow", "partial", "nabla",
                     "sqrt", "sum", "int", "prod", "bar", "hat", "vec", "matrix",
                 })
        {
            EquationLinearSymbols.Names.Should().ContainKey(name);
        }

        EquationLinearSymbols.Names.Values.Should().OnlyContain(value => value.Length > 0);
    }

    [Fact]
    public void AreEquivalent_ComparesOnlyWhatAKindUses()
    {
        var left = MathRun.Fraction("a", "b") with { Accent = "!", OpenChar = "<", Text = "ignored" };
        EquationStructure.AreEquivalent(Eq(left), Eq(MathRun.Fraction("a", "b"))).Should().BeTrue();
        EquationStructure.AreEquivalent(Eq(MathRun.Fraction("a", "b")), Eq(MathRun.Fraction("a", "c"))).Should().BeFalse();
        EquationStructure.AreEquivalent(Eq(MathRun.Superscript("a", "b")), Eq(MathRun.Subscript("a", "b"))).Should().BeFalse();
        EquationStructure.AreEquivalent((Equation?)null, (Equation?)null).Should().BeTrue();
        EquationStructure.AreEquivalent(Eq(), null).Should().BeFalse();
    }

    [Fact]
    public void AreEquivalent_ComparesLayoutOnlyOnRequest()
    {
        var inline = Eq(MathRun.PlainText("x"));
        var display = Eq(MathRun.PlainText("x"));
        display.IsDisplayMath = true;
        var displayLeft = Eq(MathRun.PlainText("x"));
        displayLeft.IsDisplayMath = true;
        displayLeft.Justification = MathJustification.Left;

        EquationStructure.AreEquivalent(inline, display).Should().BeTrue();
        EquationStructure.AreEquivalent(inline, display, compareLayout: true).Should().BeFalse();
        EquationStructure.AreEquivalent(display, displayLeft, compareLayout: true).Should().BeFalse();
        EquationStructure.AreEquivalent(display, display.Clone(), compareLayout: true).Should().BeTrue();
    }

    [Fact]
    public void Clone_CopiesTheNewSlotsAndLayout()
    {
        var name = Eq(MathRun.LowerLimit("lim", "x"));
        var equation = Eq(new MathRun
        {
            Kind = MathRunKind.FunctionApply,
            FunctionNameEquation = name,
            Base = "f"
        });
        equation.IsDisplayMath = true;
        equation.Justification = MathJustification.Right;

        var clone = equation.Clone();

        clone.Justification.Should().Be(MathJustification.Right);
        clone.Runs[0].FunctionNameEquation.Should().NotBeNull().And.NotBeSameAs(name);
        EquationStructure.AreEquivalent(clone, equation, compareLayout: true).Should().BeTrue();
    }
}
