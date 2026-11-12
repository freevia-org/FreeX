using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>One entry of the Equation tab's Symbols gallery: the character it inserts at the editor caret.</summary>
/// <param name="Id">Stable lower-case id, unique across the whole catalog.</param>
/// <param name="Text">The character inserted.</param>
/// <param name="Name">The linear-format name that types the same character (<c>\alpha</c> without the backslash).</param>
/// <param name="Label">The English gallery label.</param>
public sealed record EquationSymbolDefinition(string Id, string Text, string Name, string Label)
{
    public string CommandId => EquationSymbolCatalog.CommandPrefix + Id;
}

/// <summary>A named set of symbols shown as one gallery on the Equation tab.</summary>
public sealed record EquationSymbolSet(string Id, string Label, IReadOnlyList<EquationSymbolDefinition> Symbols)
{
    public string CommandId => EquationSymbolCatalog.SetCommandPrefix + Id;
}

/// <summary>
/// The Equation tab's Symbols gallery, shared by both desktop renderers: Word's Basic Math, Greek Letters,
/// Operators and Arrows sets. Every symbol is one the linear format knows by name, so the gallery and
/// typing <c>\name</c> can never disagree.
/// </summary>
public static class EquationSymbolCatalog
{
    public const string CommandPrefix = "freew.equation-symbol-";
    public const string SetCommandPrefix = "freew.equation-symbols-";

    public static IReadOnlyList<EquationSymbolSet> Sets { get; } =
    [
        new("basic", "Basic Math",
        [
            Symbol("pm", "Plus Minus"),
            Symbol("infty", "Infinity"),
            Symbol("neq", "Not Equal To"),
            Symbol("times", "Multiplication Sign"),
            Symbol("div", "Division Sign"),
            Symbol("propto", "Proportional To"),
            Symbol("leq", "Less Than or Equal To"),
            Symbol("geq", "Greater Than or Equal To"),
            Symbol("approx", "Almost Equal To"),
            Symbol("equiv", "Identical To"),
            Symbol("cong", "Approximately Equal To"),
            Symbol("forall", "For All"),
            Symbol("exists", "There Exists"),
            Symbol("partial", "Partial Differential"),
            Symbol("nabla", "Nabla"),
            Symbol("emptyset", "Empty Set"),
            Symbol("degree", "Degrees"),
            Symbol("in", "Element Of"),
            Symbol("notin", "Not an Element Of"),
            Symbol("therefore", "Therefore"),
        ]),
        new("greek", "Greek Letters",
        [
            Symbol("alpha", "Alpha"),
            Symbol("beta", "Beta"),
            Symbol("gamma", "Gamma"),
            Symbol("delta", "Delta"),
            Symbol("epsilon", "Epsilon"),
            Symbol("zeta", "Zeta"),
            Symbol("eta", "Eta"),
            Symbol("theta", "Theta"),
            Symbol("iota", "Iota"),
            Symbol("kappa", "Kappa"),
            Symbol("lambda", "Lambda"),
            Symbol("mu", "Mu"),
            Symbol("nu", "Nu"),
            Symbol("xi", "Xi"),
            Symbol("omicron", "Omicron"),
            Symbol("pi", "Pi"),
            Symbol("rho", "Rho"),
            Symbol("sigma", "Sigma"),
            Symbol("tau", "Tau"),
            Symbol("upsilon", "Upsilon"),
            Symbol("phi", "Phi"),
            Symbol("chi", "Chi"),
            Symbol("psi", "Psi"),
            Symbol("omega", "Omega"),
            Capital("Gamma"),
            Capital("Delta"),
            Capital("Theta"),
            Capital("Lambda"),
            Capital("Xi"),
            Capital("Pi"),
            Capital("Sigma"),
            Capital("Upsilon"),
            Capital("Phi"),
            Capital("Psi"),
            Capital("Omega"),
        ]),
        new("operators", "Operators",
        [
            Symbol("cdot", "Dot Operator"),
            Symbol("ast", "Asterisk Operator"),
            Symbol("circ", "Ring Operator"),
            Symbol("bullet", "Bullet Operator"),
            Symbol("mp", "Minus Plus"),
            Symbol("oplus", "Circled Plus"),
            Symbol("otimes", "Circled Times"),
            Symbol("odot", "Circled Dot"),
            Symbol("cup", "Union"),
            Symbol("cap", "Intersection"),
            Symbol("vee", "Logical Or"),
            Symbol("wedge", "Logical And"),
            Symbol("neg", "Not Sign"),
            Symbol("setminus", "Set Minus"),
            Symbol("subset", "Subset Of"),
            Symbol("superset", "Superset Of"),
            Symbol("subseteq", "Subset Of or Equal To"),
            Symbol("superseteq", "Superset Of or Equal To"),
        ]),
        new("arrows", "Arrows",
        [
            Symbol("rightarrow", "Right Arrow"),
            Symbol("leftarrow", "Left Arrow"),
            Symbol("leftrightarrow", "Left Right Arrow"),
            Symbol("uparrow", "Up Arrow"),
            Symbol("downarrow", "Down Arrow"),
            Symbol("Rightarrow", "Right Double Arrow", "double-rightarrow"),
            Symbol("Leftarrow", "Left Double Arrow", "double-leftarrow"),
            Symbol("Leftrightarrow", "Left Right Double Arrow", "double-leftrightarrow"),
            Symbol("mapsto", "Maps To"),
            Symbol("longrightarrow", "Long Right Arrow"),
        ]),
    ];

    /// <summary>Every symbol of every set, in gallery order.</summary>
    public static IReadOnlyList<EquationSymbolDefinition> Symbols { get; } =
        Sets.SelectMany(set => set.Symbols).ToArray();

    private static EquationSymbolDefinition Symbol(string name, string label, string? id = null) =>
        new(id ?? name, EquationLinearSymbols.Names[name], name, label);

    // Command ids are compared case-insensitively (and name icon files), so a capital letter cannot
    // share its lower-case letter's id.
    private static EquationSymbolDefinition Capital(string name) =>
        Symbol(name, "Capital " + name, "capital-" + name.ToLowerInvariant());
}
