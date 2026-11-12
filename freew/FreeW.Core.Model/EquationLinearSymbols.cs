using System.Collections.Frozen;

namespace FreeW.Core.Model;

/// <summary>
/// The <c>\name</c> vocabulary of FreeW's linear equation format (Word's "UnicodeMath" math-autocorrect
/// names) and the character classes the linear grammar gives a structural meaning to.
/// </summary>
public static class EquationLinearSymbols
{
    public const string InvisibleOpen = "〖";        // 〖  \begin
    public const string InvisibleClose = "〗";       // 〗  \end
    public const string ExplicitOpen = "├";         // ├  \open
    public const string ExplicitClose = "┤";        // ┤  \close
    public const string ArgumentSeparator = "│";    // │  \vbar
    public const string Radical = "√";              // √  \sqrt
    public const string CubeRoot = "∛";             // ∛  \cbrt
    public const string FourthRoot = "∜";           // ∜  \qdrt
    public const string Matrix = "■";               // ■  \matrix
    public const string EquationArray = "█";        // █  \eqarray
    public const string Overbar = "¯";              // ¯  \overbar
    public const string Underbar = "▁";             // ▁  \underbar
    public const string Of = "▒";                   // ▒  \of
    public const string FunctionApply = "⁡";        // invisible function application  \funcapply
    public const string Below = "┬";                // ┬  \below
    public const string Above = "┴";                // ┴  \above
    public const string NoBarFraction = "¦";        // ¦  \atop
    public const string SkewedFraction = "⁄";       // ⁄  \sdiv
    public const string LinearFraction = "∕";       // ∕  \ldiv
    public const string OpaqueOpen = "⟦";           // ⟦
    public const string OpaqueClose = "⟧";          // ⟧
    public const string Placeholder = "⬚";          // ⬚  \placeholder

    /// <summary>Every <c>\name</c> the parser recognises, mapped to the text it stands for.</summary>
    public static FrozenDictionary<string, string> Names { get; } = BuildNames();

    /// <summary>Function names the parser treats as one upright operand (and applies to what follows).</summary>
    public static FrozenSet<string> FunctionNames { get; } = new[]
    {
        "sin", "cos", "tan", "cot", "sec", "csc",
        "sinh", "cosh", "tanh", "coth", "sech", "csch",
        "arcsin", "arccos", "arctan", "arccot", "arcsec", "arccsc",
        "log", "ln", "lg", "exp", "lim", "limsup", "liminf", "max", "min", "sup", "inf",
        "det", "gcd", "deg", "dim", "hom", "ker", "arg", "Pr",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Function names whose subscript is a limit written below the name (m:limLow).</summary>
    public static FrozenSet<string> LimitFunctionNames { get; } = new[]
    {
        "lim", "limsup", "liminf", "max", "min", "sup", "inf",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The n-ary operator glyphs (m:nary/m:naryPr/m:chr) the linear format can express.</summary>
    public static FrozenSet<string> NAryOperators { get; } = new[]
    {
        "∑", "∏", "∐", "∫", "∬", "∭", "∮", "∯", "∰",
        "⋃", "⋂", "⋁", "⋀", "⨀", "⨁", "⨂", "⨄", "⨆",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Combining accent marks (m:acc/m:accPr/m:chr) written after their base.</summary>
    public static FrozenSet<string> Accents { get; } = new[]
    {
        "̀", "́", "̂", "̃", "̄", "̅", "̆", "̇", "̈",
        "̌", "̿", "⃑", "⃗", "⃛",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Grouping characters (m:groupChr) mapped to the position ("top"/"bot") they naturally take.</summary>
    public static FrozenDictionary<string, string> GroupCharacters { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["⏞"] = "top", // ⏞ \overbrace
        ["⏟"] = "bot", // ⏟ \underbrace
        ["⏜"] = "top", // ⏜ \overparen
        ["⏝"] = "bot", // ⏝ \underparen
        ["⎴"] = "top", // ⎴ \overbracket
        ["⎵"] = "bot", // ⎵ \underbracket
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal static FrozenSet<string> OpenBrackets { get; } = new[]
    {
        "(", "[", "{", "⟨", "⌊", "⌈", InvisibleOpen,
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static FrozenSet<string> CloseBrackets { get; } = new[]
    {
        ")", "]", "}", "⟩", "⌋", "⌉", InvisibleClose,
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static FrozenSet<string> BarBrackets { get; } = new[] { "|", "‖" }.ToFrozenSet(StringComparer.Ordinal);

    internal static FrozenSet<string> ScriptSigns { get; } = new[] { "-", "+", "−", "±", "∓" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Symbols that, like letters, can be a fraction operand or script base on their own.</summary>
    internal static FrozenSet<string> OperandSymbols { get; } = new[]
    {
        "∞", "∂", "∇", "∅", "′", "″", "‴", Placeholder,
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> StructuralCharacters = new[]
    {
        "\\", "\"", "/", "^", "_", "&", "@",
        ExplicitOpen, ExplicitClose, ArgumentSeparator, Radical, CubeRoot, FourthRoot, Matrix, EquationArray,
        Overbar, Underbar, Of, FunctionApply, Below, Above, NoBarFraction, SkewedFraction, LinearFraction,
        OpaqueOpen, OpaqueClose,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="character"/> (one Unicode scalar as a string) can have a structural meaning
    /// in the linear format and therefore has to be escaped with a backslash to be taken literally.
    /// </summary>
    public static bool IsStructural(string character) =>
        StructuralCharacters.Contains(character)
        || OpenBrackets.Contains(character)
        || CloseBrackets.Contains(character)
        || BarBrackets.Contains(character)
        || NAryOperators.Contains(character)
        || Accents.Contains(character)
        || GroupCharacters.ContainsKey(character);

    private static FrozenDictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Greek lower case
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
            ["epsilon"] = "ϵ", ["varepsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η",
            ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ",
            ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ",
            ["omicron"] = "ο", ["pi"] = "π", ["varpi"] = "ϖ", ["rho"] = "ρ",
            ["varrho"] = "ϱ", ["sigma"] = "σ", ["varsigma"] = "ς", ["tau"] = "τ",
            ["upsilon"] = "υ", ["phi"] = "ϕ", ["varphi"] = "φ", ["chi"] = "χ",
            ["psi"] = "ψ", ["omega"] = "ω",

            // Greek upper case
            ["Alpha"] = "Α", ["Beta"] = "Β", ["Gamma"] = "Γ", ["Delta"] = "Δ",
            ["Epsilon"] = "Ε", ["Zeta"] = "Ζ", ["Eta"] = "Η", ["Theta"] = "Θ",
            ["Iota"] = "Ι", ["Kappa"] = "Κ", ["Lambda"] = "Λ", ["Mu"] = "Μ",
            ["Nu"] = "Ν", ["Xi"] = "Ξ", ["Omicron"] = "Ο", ["Pi"] = "Π",
            ["Rho"] = "Ρ", ["Sigma"] = "Σ", ["Tau"] = "Τ", ["Upsilon"] = "Υ",
            ["Phi"] = "Φ", ["Chi"] = "Χ", ["Psi"] = "Ψ", ["Omega"] = "Ω",

            // Basic math
            ["infty"] = "∞", ["pm"] = "±", ["mp"] = "∓", ["times"] = "×",
            ["div"] = "÷", ["cdot"] = "⋅", ["ast"] = "∗", ["circ"] = "∘",
            ["bullet"] = "∙", ["partial"] = "∂", ["nabla"] = "∇", ["degree"] = "°",
            ["prime"] = "′", ["pprime"] = "″", ["ppprime"] = "‴", ["emptyset"] = "∅",
            ["hbar"] = "ℏ", ["ell"] = "ℓ", ["Re"] = "ℜ", ["Im"] = "ℑ",
            ["aleph"] = "ℵ", ["wp"] = "℘", ["angle"] = "∠", ["therefore"] = "∴",
            ["because"] = "∵", ["dots"] = "…", ["ldots"] = "…", ["cdots"] = "⋯",
            ["vdots"] = "⋮", ["ddots"] = "⋱", ["neg"] = "¬", ["surd"] = "√",

            // Relations
            ["leq"] = "≤", ["le"] = "≤", ["geq"] = "≥", ["ge"] = "≥",
            ["neq"] = "≠", ["ne"] = "≠", ["approx"] = "≈", ["equiv"] = "≡",
            ["cong"] = "≅", ["sim"] = "∼", ["simeq"] = "≃", ["propto"] = "∝",
            ["ll"] = "≪", ["gg"] = "≫", ["prec"] = "≺", ["succ"] = "≻",
            ["in"] = "∈", ["ni"] = "∋", ["notin"] = "∉", ["subset"] = "⊂",
            ["superset"] = "⊃", ["subseteq"] = "⊆", ["superseteq"] = "⊇",
            ["perp"] = "⊥", ["parallel"] = "∥", ["mid"] = "∣", ["asymp"] = "≍",
            ["doteq"] = "≐", ["models"] = "⊨", ["vdash"] = "⊢", ["dashv"] = "⊣",

            // Operators
            ["cup"] = "∪", ["cap"] = "∩", ["vee"] = "∨", ["wedge"] = "∧",
            ["oplus"] = "⊕", ["ominus"] = "⊖", ["otimes"] = "⊗", ["odot"] = "⊙",
            ["setminus"] = "∖", ["forall"] = "∀", ["exists"] = "∃", ["star"] = "⋆",
            ["uplus"] = "⊎", ["sqcup"] = "⊔", ["sqcap"] = "⊓", ["wr"] = "≀",
            ["top"] = "⊤", ["bot"] = "⊥",

            // Arrows
            ["rightarrow"] = "→", ["to"] = "→", ["leftarrow"] = "←", ["gets"] = "←",
            ["leftrightarrow"] = "↔", ["uparrow"] = "↑", ["downarrow"] = "↓",
            ["updownarrow"] = "↕", ["Rightarrow"] = "⇒", ["Leftarrow"] = "⇐",
            ["Leftrightarrow"] = "⇔", ["Uparrow"] = "⇑", ["Downarrow"] = "⇓",
            ["mapsto"] = "↦", ["nearrow"] = "↗", ["searrow"] = "↘",
            ["swarrow"] = "↙", ["nwarrow"] = "↖", ["hookrightarrow"] = "↪",
            ["hookleftarrow"] = "↩", ["rightharpoonup"] = "⇀", ["leftharpoonup"] = "↼",
            ["longrightarrow"] = "⟶", ["longleftarrow"] = "⟵", ["implies"] = "⟹",
            ["iff"] = "⟺",

            // N-ary operators
            ["sum"] = "∑", ["prod"] = "∏", ["coprod"] = "∐", ["amalg"] = "∐",
            ["int"] = "∫", ["iint"] = "∬", ["iiint"] = "∭", ["oint"] = "∮",
            ["oiint"] = "∯", ["oiiint"] = "∰", ["bigcup"] = "⋃", ["bigcap"] = "⋂",
            ["bigvee"] = "⋁", ["bigwedge"] = "⋀", ["bigodot"] = "⨀",
            ["bigoplus"] = "⨁", ["bigotimes"] = "⨂", ["biguplus"] = "⨄",
            ["bigsqcup"] = "⨆",

            // Structures
            ["sqrt"] = Radical, ["cbrt"] = CubeRoot, ["qdrt"] = FourthRoot,
            ["matrix"] = Matrix, ["eqarray"] = EquationArray, ["of"] = Of, ["funcapply"] = FunctionApply,
            ["begin"] = InvisibleOpen, ["end"] = InvisibleClose, ["open"] = ExplicitOpen,
            ["close"] = ExplicitClose, ["left"] = ExplicitOpen, ["right"] = ExplicitClose,
            ["vbar"] = ArgumentSeparator, ["overbar"] = Overbar, ["underbar"] = Underbar,
            ["below"] = Below, ["above"] = Above, ["atop"] = NoBarFraction, ["sdiv"] = SkewedFraction,
            ["ldiv"] = LinearFraction, ["over"] = "/", ["placeholder"] = Placeholder,
            ["overbrace"] = "⏞", ["underbrace"] = "⏟", ["overparen"] = "⏜",
            ["underparen"] = "⏝", ["overbracket"] = "⎴", ["underbracket"] = "⎵",

            // Delimiters
            ["lbrace"] = "{", ["rbrace"] = "}", ["lbrack"] = "[", ["rbrack"] = "]",
            ["langle"] = "⟨", ["rangle"] = "⟩", ["bra"] = "⟨", ["ket"] = "⟩",
            ["lfloor"] = "⌊", ["rfloor"] = "⌋", ["lceil"] = "⌈", ["rceil"] = "⌉",
            ["norm"] = "‖", ["Vert"] = "‖", ["vert"] = "|",

            // Accents (combining marks applied to the preceding operand)
            ["hat"] = "̂", ["check"] = "̌", ["tilde"] = "̃", ["acute"] = "́",
            ["grave"] = "̀", ["dot"] = "̇", ["ddot"] = "̈", ["dddot"] = "⃛",
            ["breve"] = "̆", ["bar"] = "̅", ["Bar"] = "̿", ["vec"] = "⃗",
            ["hvec"] = "⃑",
        };

        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
