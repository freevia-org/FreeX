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
}

public sealed record EquationPresetDefinition(
    EquationPresetKind Kind,
    string CommandId,
    string LegacyCommandId,
    Func<Equation> Factory)
{
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
    ];

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
}
