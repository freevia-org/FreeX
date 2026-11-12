using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum EquationPresetKind
{
    Fraction,
    Script,
    Radical,
    NthRoot,
    Integral,
    Summation,
    Product,
    Accent,
    Bar,
    Bracket,
    Matrix,
    Function,
    GroupCharacter,
    AreaOfCircle,
    BinomialTheorem,
    ExpansionOfSum,
    FourierSeries,
    PythagoreanTheorem,
    QuadraticFormula,
    TaylorExpansion,
    TrigIdentity1,
    TrigIdentity2,
}

public sealed record EquationPresetDefinition(
    EquationPresetKind Kind,
    string CommandId,
    string LegacyCommandId,
    Func<Equation> Factory)
{
    /// <summary>
    /// The gallery name of one of Word's built-in equations ("Quadratic Formula"); null for the bare
    /// structure presets.
    /// </summary>
    public string? BuiltInName { get; init; }

    /// <summary>The linear source of a built-in equation; null for the bare structure presets.</summary>
    public string? BuiltInLinearText { get; init; }

    public Equation CreateEquation() => Factory();
}

/// <summary>Canonical equation gallery identities and fresh model factories for both desktop renderers.</summary>
public static class EquationPresetCatalog
{
    public const string DefaultCommandId = "freew.equation-default";
    public const string LegacyDefaultCommandId = "freew.equation.default";

    public static IReadOnlyList<EquationPresetDefinition> Presets { get; } =
    [
        Preset(EquationPresetKind.Fraction, "fraction", () =>
            new Equation([MathRun.Fraction("a", "b")])),
        Preset(EquationPresetKind.Script, "script", () =>
            new Equation([MathRun.SubSuperscript("x", "n", "2")])),
        Preset(EquationPresetKind.Radical, "radical", () =>
            new Equation([MathRun.Radical("x")])),
        Preset(EquationPresetKind.NthRoot, "nthroot", () =>
            new Equation([MathRun.Radical("x", "n")])),
        Preset(EquationPresetKind.Integral, "integral", () =>
            new Equation([MathRun.NAry("\u222B", "a", "b", "f(x) dx")])),
        Preset(EquationPresetKind.Summation, "summation", () =>
            new Equation([MathRun.NAry("\u2211", "i=1", "n", "i")])),
        Preset(EquationPresetKind.Product, "product", () =>
            new Equation([MathRun.NAry("\u220F", "i=1", "n", "i")])),
        Preset(EquationPresetKind.Accent, "accent", () =>
            new Equation([MathRun.AccentOf("x")])),
        Preset(EquationPresetKind.Bar, "bar", () =>
            new Equation([MathRun.BarOf("x")])),
        Preset(EquationPresetKind.Bracket, "bracket", () =>
            new Equation([MathRun.Delimiter("a, b")])),
        Preset(EquationPresetKind.Matrix, "matrix", () =>
            new Equation([MathRun.MatrixOf(MathMatrix.Identity2x2())])),
        Preset(EquationPresetKind.Function, "func", () =>
            new Equation([MathRun.FunctionApply("sin", "x")])),
        Preset(EquationPresetKind.GroupCharacter, "groupchr", () =>
            new Equation([MathRun.GroupCharOf("x+y")])),

        // Word's built-in equation gallery. Each is authored in the linear format, so it is exactly what
        // typing that text into the equation editor produces.
        BuiltIn(EquationPresetKind.AreaOfCircle, "area-of-circle", "Area of Circle",
            "A=πr^2"),
        BuiltIn(EquationPresetKind.BinomialTheorem, "binomial-theorem", "Binomial Theorem",
            "(x+a)^n=∑_(k=0)^n▒〖(n¦k)x^k a^(n-k)〗"),
        BuiltIn(EquationPresetKind.ExpansionOfSum, "expansion-of-sum", "Expansion of a Sum",
            "(1+x)^n=1+(nx)/(1!)+(n(n-1)x^2)/(2!)+…"),
        BuiltIn(EquationPresetKind.FourierSeries, "fourier-series", "Fourier Series",
            "f(x)=a_0+∑_(n=1)^∞▒(a_n cos\u2061〖(nπx)/L〗+b_n sin\u2061〖(nπx)/L〗)"),
        BuiltIn(EquationPresetKind.PythagoreanTheorem, "pythagorean-theorem", "Pythagorean Theorem",
            "a^2+b^2=c^2"),
        BuiltIn(EquationPresetKind.QuadraticFormula, "quadratic-formula", "Quadratic Formula",
            "x=(-b±√(b^2-4ac))/(2a)"),
        BuiltIn(EquationPresetKind.TaylorExpansion, "taylor-expansion", "Taylor Expansion",
            "e^x=1+x/(1!)+x^2/(2!)+x^3/(3!)+…, -∞<x<∞"),
        BuiltIn(EquationPresetKind.TrigIdentity1, "trig-identity-1", "Trig Identity 1",
            "sin\u2061α±sin\u2061β=2 sin\u2061〖1/2 (α±β)〗 cos\u2061〖1/2 (α∓β)〗"),
        BuiltIn(EquationPresetKind.TrigIdentity2, "trig-identity-2", "Trig Identity 2",
            "cos\u2061α+cos\u2061β=2 cos\u2061〖1/2 (α+β)〗 cos\u2061〖1/2 (α-β)〗"),
    ];

    /// <summary>Word's built-in gallery equations, in gallery (alphabetical) order.</summary>
    public static IReadOnlyList<EquationPresetDefinition> BuiltInEquations { get; } =
        Presets.Where(preset => preset.BuiltInName is not null).ToArray();

    public static EquationPresetDefinition Get(EquationPresetKind kind) =>
        Presets.First(preset => preset.Kind == kind);

    public static Equation CreateDefaultEquation()
    {
        var equation = new Equation();
        equation.Runs.Add(MathRun.PlainText("E = m"));
        equation.Runs.Add(MathRun.Superscript("c", "2"));
        return equation;
    }

    private static EquationPresetDefinition Preset(
        EquationPresetKind kind,
        string commandSuffix,
        Func<Equation> factory) =>
        new(
            kind,
            $"freew.equation-{commandSuffix}",
            $"freew.equation.{commandSuffix}",
            factory);

    private static EquationPresetDefinition BuiltIn(
        EquationPresetKind kind,
        string commandSuffix,
        string name,
        string linearText) =>
        Preset(kind, commandSuffix, () => EquationLinearFormat.Parse(linearText)) with
        {
            BuiltInName = name,
            BuiltInLinearText = linearText
        };
}
