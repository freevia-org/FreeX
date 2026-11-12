namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// One entry of an Equation tab Structures gallery: a linear-format template inserted at the editor
/// caret. Empty slots are the placeholder character ⬚, which the editor selects so typing replaces it.
/// </summary>
/// <param name="Id">Stable lower-case id, unique within its group.</param>
/// <param name="Label">The English gallery label.</param>
/// <param name="LinearText">The linear-format text inserted.</param>
public sealed record EquationStructureDefinition(string Id, string Label, string LinearText)
{
    /// <summary>The id of the gallery this structure belongs to.</summary>
    public string GroupId { get; init; } = string.Empty;

    public string CommandId => EquationStructureCatalog.CommandPrefix + GroupId + "-" + Id;
}

/// <summary>One Structures gallery (Fraction, Script, Radical, …) and its templates.</summary>
public sealed record EquationStructureGroup(string Id, string Label, IReadOnlyList<EquationStructureDefinition> Structures)
{
    public string CommandId => EquationStructureCatalog.CommandPrefix + Id;
}

/// <summary>
/// The Equation tab's Structures galleries, shared by both desktop renderers, in Word's order: Fraction,
/// Script, Radical, Integral, Large Operator, Bracket, Function, Accent, Limit and Log, Operator, Matrix.
/// </summary>
public static class EquationStructureCatalog
{
    public const string CommandPrefix = "freew.equation-structure-";

    /// <summary>The placeholder character templates use for an empty slot (Word's dotted box).</summary>
    public const string Placeholder = "⬚";

    private const string P = Placeholder;

    public static IReadOnlyList<EquationStructureGroup> Groups { get; } =
    [
        Group("fraction", "Fraction",
            new("stacked", "Stacked Fraction", P + "/" + P),
            new("skewed", "Skewed Fraction", P + "⁄" + P),
            new("linear", "Linear Fraction", P + "∕" + P),
            new("dy-dx", "dy over dx", "dy/dx"),
            new("pi-over-2", "Pi Over 2", "π/2")),
        Group("script", "Script",
            new("superscript", "Superscript", P + "^" + P),
            new("subscript", "Subscript", P + "_" + P),
            new("subscript-superscript", "Subscript-Superscript", P + "_" + P + "^" + P),
            new("x-squared", "x Squared", "x^2"),
            new("e-to-the-power", "e to the Power", "e^(-iωt)")),
        Group("radical", "Radical",
            new("square-root", "Square Root", "√(" + P + ")"),
            new("root-with-degree", "Radical with Degree", "√(" + P + "&" + P + ")"),
            new("cube-root", "Cubic Root", "√(3&" + P + ")"),
            new("hypotenuse", "Square Root of a Sum of Squares", "√(a^2+b^2)")),
        Group("integral", "Integral",
            new("integral", "Integral", "∫▒" + P),
            new("integral-limits", "Integral with Limits", "∫_" + P + "^" + P + "▒" + P),
            new("double-integral", "Double Integral", "∬▒" + P),
            new("triple-integral", "Triple Integral", "∭▒" + P),
            new("contour-integral", "Contour Integral", "∮▒" + P)),
        Group("large-operator", "Large Operator",
            new("summation", "Summation", "∑▒" + P),
            new("summation-limits", "Summation with Limits", "∑_" + P + "^" + P + "▒" + P),
            new("product-limits", "Product with Limits", "∏_" + P + "^" + P + "▒" + P),
            new("union-limits", "Union with Limits", "⋃_" + P + "^" + P + "▒" + P),
            new("intersection-limits", "Intersection with Limits", "⋂_" + P + "^" + P + "▒" + P)),
        Group("bracket", "Bracket",
            new("parentheses", "Parentheses", "(" + P + ")"),
            new("square-brackets", "Square Brackets", "[" + P + "]"),
            new("braces", "Braces", "{" + P + "}"),
            new("angle-brackets", "Angle Brackets", "⟨" + P + "⟩"),
            new("floor", "Floor", "⌊" + P + "⌋"),
            new("ceiling", "Ceiling", "⌈" + P + "⌉"),
            new("absolute-value", "Absolute Value", "|" + P + "|"),
            new("norm", "Norm", "‖" + P + "‖"),
            new("binomial", "Binomial Coefficient", "(" + P + "¦" + P + ")"),
            new("cases", "Cases", "{█(" + P + "@" + P + ")〗")),
        Group("function", "Function",
            new("sine", "Sine", "sin\u2061" + P),
            new("cosine", "Cosine", "cos\u2061" + P),
            new("tangent", "Tangent", "tan\u2061" + P),
            new("inverse-sine", "Inverse Sine", "sin^(-1)\u2061" + P),
            new("sine-theta", "Sine of Theta", "sin\u2061θ"),
            new("cosine-2x", "Cosine of 2x", "cos\u2061〖2x〗")),
        Group("accent", "Accent",
            new("dot", "Dot", P + "\u0307"),
            new("double-dot", "Double Dot", P + "\u0308"),
            new("hat", "Hat", P + "\u0302"),
            new("tilde", "Tilde", P + "\u0303"),
            new("vector", "Vector", P + "\u20D7"),
            new("bar", "Bar", P + "\u0305"),
            new("overbar", "Overbar", "¯(" + P + ")"),
            new("underbar", "Underbar", "▁(" + P + ")"),
            new("overbrace", "Overbrace", "⏞(" + P + ")"),
            new("underbrace", "Underbrace", "⏟(" + P + ")")),
        Group("limit-and-log", "Limit and Log",
            new("log-base", "Logarithm with Base", "log_" + P + "\u2061" + P),
            new("log", "Logarithm", "log\u2061" + P),
            new("natural-log", "Natural Logarithm", "ln\u2061" + P),
            new("limit", "Limit", "lim┬" + P + "\u2061" + P),
            new("minimum", "Minimum", "min┬" + P + "\u2061" + P),
            new("maximum", "Maximum", "max┬" + P + "\u2061" + P),
            new("limit-example", "Limit Example", "lim┬(n→∞)\u2061(1+1/n)^n")),
        Group("operator", "Operator",
            new("colon-equal", "Colon Equal", "≔"),
            new("defined-as", "Equal to by Definition", "≝"),
            new("delta-equal", "Delta Equal To", "≜"),
            new("arrow-above", "Right Arrow with Text Above", "〖→〗┴" + P),
            new("arrow-below", "Right Arrow with Text Below", "〖→〗┬" + P),
            new("yields", "Yields", "〖→〗┴(yields)")),
        Group("matrix", "Matrix",
            new("1x2", "1×2 Empty Matrix", "■(" + P + "&" + P + ")"),
            new("2x1", "2×1 Empty Matrix", "■(" + P + "@" + P + ")"),
            new("2x2", "2×2 Empty Matrix", "■(" + P + "&" + P + "@" + P + "&" + P + ")"),
            new("3x3", "3×3 Empty Matrix", "■(" + P + "&" + P + "&" + P + "@" + P + "&" + P + "&" + P + "@" + P + "&" + P + "&" + P + ")"),
            new("identity-2x2", "2×2 Identity Matrix", "■(1&0@0&1)"),
            new("parentheses-2x2", "2×2 Matrix with Parentheses", "(■(" + P + "&" + P + "@" + P + "&" + P + "))"),
            new("brackets-2x2", "2×2 Matrix with Brackets", "[■(" + P + "&" + P + "@" + P + "&" + P + ")]")),
    ];

    /// <summary>Every structure of every gallery, in gallery order.</summary>
    public static IReadOnlyList<EquationStructureDefinition> Structures { get; } =
        Groups.SelectMany(group => group.Structures).ToArray();

    private static EquationStructureGroup Group(string id, string label, params EquationStructureDefinition[] structures) =>
        new(id, label, structures.Select(structure => structure with { GroupId = id }).ToArray());
}
