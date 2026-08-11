namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item W1, basic OMML equations):
// An equation is modelled as an OPTIONAL INLINE RUN MARK (Run.Equation) rather than a new block type.
// This mirrors how every other inline feature (images, footnote/endnote references, content controls,
// fields) is already carried on Run, so equations flow through the existing run sequence, hyperlink/
// comment/revision wrapping, table cells, headers and footers with zero new plumbing — and they
// round-trip through docx as an inline m:oMath emitted in place of the run's w:t. An equation is a flat,
// ordered list of MathRun parts; each part is either plain math text (m:r/m:t) or one of the common OMML
// structures — superscript (m:sSup), subscript (m:sSub), sub-superscript (m:sSubSup), fraction (m:f),
// radical (m:rad), n-ary (m:nary, sum/integral/product with limits), an accented character (m:acc),
// an over/under-bar (m:bar), a bracketed delimiter (m:d), a matrix (m:m) or an equation array
// (m:eqArr). Most structure slots store
// plain math text; scripts carry optional nested base/sub/sup equations, fractions additionally carry
// optional nested numerator/denominator equations, radicals carry an optional nested radicand, n-ary
// operators carry optional nested lower/upper/operand equations, delimiters carry an optional nested
// content equation, and matrices/equation arrays carry optional nested cell equations so common
// OfficeMath slots can round-trip without a broad recursive-slot rewrite. A Matrix additionally carries
// a small grid of text cells as its fallback display/editing surface. That
// covers the high-value structures from Word's Equation tools while staying well short of the full
// recursive OMML schema — richer constructs degrade to their plain math text on read so nothing throws.

/// <summary>
/// The kind of an OMML math fragment carried by a <see cref="MathRun"/>.
/// <list type="bullet">
/// <item><see cref="Text"/> — a plain run of math text (m:r/m:t).</item>
/// <item><see cref="Superscript"/> — a base raised to an exponent (m:sSup).</item>
/// <item><see cref="Subscript"/> — a base with a subscript (m:sSub).</item>
/// <item><see cref="SubSuperscript"/> — a base with both sub- and super-script (m:sSubSup).</item>
/// <item><see cref="Fraction"/> — a numerator over a denominator (m:f).</item>
/// <item><see cref="Radical"/> — a (square or nth) root (m:rad).</item>
/// <item><see cref="NAry"/> — an n-ary operator (sum/integral/product) with limits (m:nary).</item>
/// <item><see cref="Accent"/> — a base with an accent mark (hat/bar/vec/dot/tilde) over it (m:acc).</item>
/// <item><see cref="Bar"/> — a base with an over- or under-bar (m:bar).</item>
/// <item><see cref="Delimiter"/> — a bracketed/parenthesised expression (m:d).</item>
/// <item><see cref="Matrix"/> — a grid of cells (m:m).</item>
/// </list>
/// </summary>
public enum MathRunKind
{
    Text,
    Superscript,
    Subscript,
    SubSuperscript,
    Fraction,
    Radical,
    NAry,
    Accent,
    Bar,
    Delimiter,
    Matrix,
    EquationArray,
    FunctionApply,
    GroupChar
}

/// <summary>
/// One fragment of an <see cref="Equation"/>. Each <see cref="Kind"/> uses the slots meaningful to it;
/// the rest stay empty. Kept deliberately small and immutable so it round-trips cleanly and so consumers
/// can pattern-match on <see cref="Kind"/>.
/// <list type="bullet">
/// <item><see cref="MathRunKind.Text"/> → <see cref="Text"/>.</item>
/// <item><see cref="MathRunKind.Superscript"/> → <see cref="ScriptBaseEquation"/>/<see cref="Base"/> raised to <see cref="ScriptSupEquation"/>/<see cref="Sup"/>.</item>
/// <item><see cref="MathRunKind.Subscript"/> → <see cref="ScriptBaseEquation"/>/<see cref="Base"/> with subscript <see cref="ScriptSubEquation"/>/<see cref="Sub"/>.</item>
/// <item><see cref="MathRunKind.SubSuperscript"/> → <see cref="ScriptBaseEquation"/>/<see cref="Base"/> with <see cref="ScriptSubEquation"/>/<see cref="Sub"/> and <see cref="ScriptSupEquation"/>/<see cref="Sup"/>.</item>
/// <item><see cref="MathRunKind.Fraction"/> → <see cref="NumeratorEquation"/>/<see cref="Numerator"/> over <see cref="DenominatorEquation"/>/<see cref="Denominator"/>.</item>
/// <item><see cref="MathRunKind.Radical"/> → <see cref="RadicandEquation"/>/<see cref="Base"/> under a root of degree <see cref="Degree"/> (empty = square root).</item>
/// <item><see cref="MathRunKind.NAry"/> → operator <see cref="Operator"/> from <see cref="NAryLowerLimitEquation"/>/<see cref="Sub"/> to <see cref="NAryUpperLimitEquation"/>/<see cref="Sup"/> over <see cref="NAryOperandEquation"/>/<see cref="Base"/>.</item>
/// <item><see cref="MathRunKind.Accent"/> → <see cref="Base"/> with the accent glyph <see cref="Accent"/> over it.</item>
/// <item><see cref="MathRunKind.Bar"/> → <see cref="Base"/> with a bar above (<see cref="BarTop"/> true) or below it.</item>
/// <item><see cref="MathRunKind.Delimiter"/> → <see cref="DelimiterContentEquation"/>/<see cref="Base"/> wrapped in <see cref="OpenChar"/>/<see cref="CloseChar"/>.</item>
/// <item><see cref="MathRunKind.Matrix"/> → <see cref="Matrix"/>.</item>
/// </list>
/// </summary>
public sealed record MathRun
{
    public const int MaxNestedEquationDepth = 16;

    /// <summary>The fragment kind.</summary>
    public MathRunKind Kind { get; init; } = MathRunKind.Text;

    /// <summary>Plain math text (only meaningful when <see cref="Kind"/> is <see cref="MathRunKind.Text"/>).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The base/operand: the base of a sub/superscript, the radicand of a radical, the operand of an
    /// n-ary operator, or the content of a delimiter.
    /// </summary>
    public string Base { get; init; } = string.Empty;

    /// <summary>The superscript / upper-limit slot (superscript, sub-superscript, n-ary).</summary>
    public string Sup { get; init; } = string.Empty;

    /// <summary>The subscript / lower-limit slot (subscript, sub-superscript, n-ary).</summary>
    public string Sub { get; init; } = string.Empty;

    /// <summary>Optional structured base equation for nested OMML script slots.</summary>
    public Equation? ScriptBaseEquation { get; init; }

    /// <summary>Optional structured subscript equation for nested OMML script slots.</summary>
    public Equation? ScriptSubEquation { get; init; }

    /// <summary>Optional structured superscript equation for nested OMML script slots.</summary>
    public Equation? ScriptSupEquation { get; init; }

    /// <summary>The numerator of a fraction (only meaningful for <see cref="MathRunKind.Fraction"/>).</summary>
    public string Numerator { get; init; } = string.Empty;

    /// <summary>The denominator of a fraction (only meaningful for <see cref="MathRunKind.Fraction"/>).</summary>
    public string Denominator { get; init; } = string.Empty;

    /// <summary>Optional structured numerator equation for nested OMML fraction slots.</summary>
    public Equation? NumeratorEquation { get; init; }

    /// <summary>Optional structured denominator equation for nested OMML fraction slots.</summary>
    public Equation? DenominatorEquation { get; init; }

    /// <summary>Optional structured radicand equation for nested OMML radical slots.</summary>
    public Equation? RadicandEquation { get; init; }

    /// <summary>Optional structured degree equation for nested OMML radical slots.</summary>
    public Equation? DegreeEquation { get; init; }

    /// <summary>Optional structured content equation for nested OMML delimiter slots.</summary>
    public Equation? DelimiterContentEquation { get; init; }

    /// <summary>Optional structured argument equation for nested OMML function-apply slots.</summary>
    public Equation? FunctionArgumentEquation { get; init; }

    /// <summary>Optional structured lower-limit equation for nested OMML n-ary slots.</summary>
    public Equation? NAryLowerLimitEquation { get; init; }

    /// <summary>Optional structured upper-limit equation for nested OMML n-ary slots.</summary>
    public Equation? NAryUpperLimitEquation { get; init; }

    /// <summary>Optional structured operand/body equation for nested OMML n-ary slots.</summary>
    public Equation? NAryOperandEquation { get; init; }

    /// <summary>Optional structured base equation for nested OMML accent/bar/group-character slots.</summary>
    public Equation? DecoratorBaseEquation { get; init; }

    /// <summary>The radical's degree (empty = square root; non-empty = nth root). Only for <see cref="MathRunKind.Radical"/>.</summary>
    public string Degree { get; init; } = string.Empty;

    /// <summary>The n-ary operator glyph (∑, ∫, ∏…). Only for <see cref="MathRunKind.NAry"/>.</summary>
    public string Operator { get; init; } = string.Empty;

    /// <summary>
    /// The accent glyph placed over the base (hat ̂, bar ̄, vector →, dot ̇, tilde ̃…). Only meaningful for
    /// <see cref="MathRunKind.Accent"/>; the default is a combining circumflex (hat).
    /// </summary>
    public string Accent { get; init; } = "̂";

    /// <summary>
    /// Whether a <see cref="MathRunKind.Bar"/> sits above the base (true → OMML m:pos "top", an overbar) or
    /// below it (false → "bot", an underbar). Only meaningful for <see cref="MathRunKind.Bar"/>.
    /// </summary>
    public bool BarTop { get; init; } = true;

    /// <summary>The opening delimiter glyph (default "("). Only for <see cref="MathRunKind.Delimiter"/>.</summary>
    public string OpenChar { get; init; } = "(";

    /// <summary>The closing delimiter glyph (default ")"). Only for <see cref="MathRunKind.Delimiter"/>.</summary>
    public string CloseChar { get; init; } = ")";

    /// <summary>
    /// Extra delimiter arguments beyond the first (a multi-argument <c>m:d</c> — e.g. a binomial/case/
    /// matrix-style delimiter group with more than one <c>m:e</c>). Empty for the common single-argument
    /// delimiter. Each entry is that argument's plain-text fallback;
    /// <see cref="AdditionalDelimiterContentEquations"/> carries the matching structured equation (or null)
    /// at the same index when the argument holds nested OMML. Only meaningful for
    /// <see cref="MathRunKind.Delimiter"/>.
    /// </summary>
    public IReadOnlyList<string> AdditionalDelimiterArguments { get; init; } = [];

    /// <summary>
    /// Structured equations parallel to <see cref="AdditionalDelimiterArguments"/> (null at an index whose
    /// argument is plain text). Only meaningful for <see cref="MathRunKind.Delimiter"/>.
    /// </summary>
    public IReadOnlyList<Equation?> AdditionalDelimiterContentEquations { get; init; } = [];

    /// <summary>
    /// The separator glyph placed between multi-argument delimiter arguments (<c>m:dPr/m:sepChr</c>).
    /// Only written/meaningful when <see cref="AdditionalDelimiterArguments"/> is non-empty.
    /// </summary>
    public string DelimiterSeparator { get; init; } = ",";

    /// <summary>The matrix/equation-array grid (only meaningful for <see cref="MathRunKind.Matrix"/> or <see cref="MathRunKind.EquationArray"/>).</summary>
    public MathMatrix? Matrix { get; init; }

    /// <summary>The function name (only meaningful for <see cref="MathRunKind.FunctionApply"/>).</summary>
    public string FuncName { get; init; } = string.Empty;

    /// <summary>The grouping character (only meaningful for <see cref="MathRunKind.GroupChar"/>).</summary>
    public string GroupChr { get; init; } = "\u23DE";

    /// <summary>The grouping character position: "top" or "bot" (only meaningful for <see cref="MathRunKind.GroupChar"/>).</summary>
    public string GroupChrPos { get; init; } = "top";

    /// <summary>Creates a plain math-text fragment (m:r/m:t).</summary>
    public static MathRun PlainText(string text) => new() { Kind = MathRunKind.Text, Text = text };

    /// <summary>Creates a superscript fragment (m:sSup): <paramref name="@base"/> raised to <paramref name="sup"/>.</summary>
    public static MathRun Superscript(string @base, string sup) =>
        new() { Kind = MathRunKind.Superscript, Base = @base, Sup = sup };

    /// <summary>Creates a superscript fragment (m:sSup) whose base and exponent are structured equations.</summary>
    public static MathRun Superscript(Equation baseEquation, Equation supEquation)
    {
        ArgumentNullException.ThrowIfNull(baseEquation);
        ArgumentNullException.ThrowIfNull(supEquation);

        return new()
        {
            Kind = MathRunKind.Superscript,
            Base = baseEquation.LinearText,
            Sup = supEquation.LinearText,
            ScriptBaseEquation = baseEquation,
            ScriptSupEquation = supEquation
        };
    }

    /// <summary>Creates a subscript fragment (m:sSub): <paramref name="@base"/> with subscript <paramref name="sub"/>.</summary>
    public static MathRun Subscript(string @base, string sub) =>
        new() { Kind = MathRunKind.Subscript, Base = @base, Sub = sub };

    /// <summary>Creates a subscript fragment (m:sSub) whose base and subscript are structured equations.</summary>
    public static MathRun Subscript(Equation baseEquation, Equation subEquation)
    {
        ArgumentNullException.ThrowIfNull(baseEquation);
        ArgumentNullException.ThrowIfNull(subEquation);

        return new()
        {
            Kind = MathRunKind.Subscript,
            Base = baseEquation.LinearText,
            Sub = subEquation.LinearText,
            ScriptBaseEquation = baseEquation,
            ScriptSubEquation = subEquation
        };
    }

    /// <summary>Creates a sub-superscript fragment (m:sSubSup): <paramref name="@base"/> with both <paramref name="sub"/> and <paramref name="sup"/>.</summary>
    public static MathRun SubSuperscript(string @base, string sub, string sup) =>
        new() { Kind = MathRunKind.SubSuperscript, Base = @base, Sub = sub, Sup = sup };

    /// <summary>Creates a sub-superscript fragment (m:sSubSup) whose base, subscript and superscript are structured equations.</summary>
    public static MathRun SubSuperscript(Equation baseEquation, Equation subEquation, Equation supEquation)
    {
        ArgumentNullException.ThrowIfNull(baseEquation);
        ArgumentNullException.ThrowIfNull(subEquation);
        ArgumentNullException.ThrowIfNull(supEquation);

        return new()
        {
            Kind = MathRunKind.SubSuperscript,
            Base = baseEquation.LinearText,
            Sub = subEquation.LinearText,
            Sup = supEquation.LinearText,
            ScriptBaseEquation = baseEquation,
            ScriptSubEquation = subEquation,
            ScriptSupEquation = supEquation
        };
    }

    /// <summary>Creates a fraction fragment (m:f): <paramref name="numerator"/> over <paramref name="denominator"/>.</summary>
    public static MathRun Fraction(string numerator, string denominator) =>
        new() { Kind = MathRunKind.Fraction, Numerator = numerator, Denominator = denominator };

    /// <summary>Creates a fraction fragment (m:f) whose numerator and denominator are structured equations.</summary>
    public static MathRun Fraction(Equation numerator, Equation denominator)
    {
        ArgumentNullException.ThrowIfNull(numerator);
        ArgumentNullException.ThrowIfNull(denominator);

        return new()
        {
            Kind = MathRunKind.Fraction,
            Numerator = numerator.LinearText,
            Denominator = denominator.LinearText,
            NumeratorEquation = numerator,
            DenominatorEquation = denominator
        };
    }

    /// <summary>
    /// Creates a radical fragment (m:rad): the root of <paramref name="radicand"/>. A null/empty
    /// <paramref name="degree"/> is a square root (no m:deg); otherwise an nth root.
    /// </summary>
    public static MathRun Radical(string radicand, string degree = "") =>
        new() { Kind = MathRunKind.Radical, Base = radicand, Degree = degree ?? string.Empty };

    /// <summary>
    /// Creates a radical fragment (m:rad) whose radicand is a structured equation.
    /// </summary>
    public static MathRun Radical(Equation radicand, string degree = "")
    {
        ArgumentNullException.ThrowIfNull(radicand);

        return new()
        {
            Kind = MathRunKind.Radical,
            Base = radicand.LinearText,
            Degree = degree ?? string.Empty,
            RadicandEquation = radicand
        };
    }

    /// <summary>Creates a radical fragment (m:rad) whose degree is a structured equation.</summary>
    public static MathRun Radical(string radicand, Equation degree)
    {
        ArgumentNullException.ThrowIfNull(degree);

        return new()
        {
            Kind = MathRunKind.Radical,
            Base = radicand,
            Degree = degree.LinearText,
            DegreeEquation = degree
        };
    }

    /// <summary>Creates a radical fragment (m:rad) whose radicand and degree are structured equations.</summary>
    public static MathRun Radical(Equation radicand, Equation degree)
    {
        ArgumentNullException.ThrowIfNull(radicand);
        ArgumentNullException.ThrowIfNull(degree);

        return new()
        {
            Kind = MathRunKind.Radical,
            Base = radicand.LinearText,
            Degree = degree.LinearText,
            RadicandEquation = radicand,
            DegreeEquation = degree
        };
    }

    /// <summary>
    /// Creates an n-ary fragment (m:nary): <paramref name="@operator"/> (e.g. ∑/∫/∏) applied to
    /// <paramref name="operand"/> with lower limit <paramref name="sub"/> and upper limit <paramref name="sup"/>.
    /// </summary>
    public static MathRun NAry(string @operator, string sub, string sup, string operand) =>
        new() { Kind = MathRunKind.NAry, Operator = @operator, Sub = sub, Sup = sup, Base = operand };

    /// <summary>
    /// Creates an n-ary fragment (m:nary) whose lower limit, upper limit and operand are structured equations.
    /// Null limit equations are treated as empty text slots.
    /// </summary>
    public static MathRun NAry(string @operator, Equation? lowerLimit, Equation? upperLimit, Equation operand)
    {
        ArgumentNullException.ThrowIfNull(operand);

        return new()
        {
            Kind = MathRunKind.NAry,
            Operator = @operator,
            Sub = lowerLimit?.LinearText ?? string.Empty,
            Sup = upperLimit?.LinearText ?? string.Empty,
            Base = operand.LinearText,
            NAryLowerLimitEquation = lowerLimit,
            NAryUpperLimitEquation = upperLimit,
            NAryOperandEquation = operand
        };
    }

    /// <summary>
    /// Creates an accent fragment (m:acc): <paramref name="@base"/> with the accent glyph
    /// <paramref name="accent"/> over it (default a combining circumflex/hat).
    /// </summary>
    public static MathRun AccentOf(string @base, string accent = "̂") =>
        new() { Kind = MathRunKind.Accent, Base = @base, Accent = string.IsNullOrEmpty(accent) ? "̂" : accent };

    /// <summary>Creates an accent fragment (m:acc) whose accented base is a structured equation.</summary>
    public static MathRun AccentOf(Equation baseEquation, string accent = "̂")
    {
        ArgumentNullException.ThrowIfNull(baseEquation);

        return new()
        {
            Kind = MathRunKind.Accent,
            Base = baseEquation.LinearText,
            Accent = string.IsNullOrEmpty(accent) ? "̂" : accent,
            DecoratorBaseEquation = baseEquation
        };
    }

    /// <summary>
    /// Creates a bar fragment (m:bar): <paramref name="@base"/> with an overbar (<paramref name="top"/> true,
    /// the default) or an underbar (<paramref name="top"/> false).
    /// </summary>
    public static MathRun BarOf(string @base, bool top = true) =>
        new() { Kind = MathRunKind.Bar, Base = @base, BarTop = top };

    /// <summary>Creates a bar fragment (m:bar) whose barred base is a structured equation.</summary>
    public static MathRun BarOf(Equation baseEquation, bool top = true)
    {
        ArgumentNullException.ThrowIfNull(baseEquation);

        return new()
        {
            Kind = MathRunKind.Bar,
            Base = baseEquation.LinearText,
            BarTop = top,
            DecoratorBaseEquation = baseEquation
        };
    }

    /// <summary>Creates a delimiter fragment (m:d): <paramref name="content"/> wrapped in <paramref name="open"/>/<paramref name="close"/>.</summary>
    public static MathRun Delimiter(string content, string open = "(", string close = ")") =>
        new() { Kind = MathRunKind.Delimiter, Base = content, OpenChar = open, CloseChar = close };

    /// <summary>Creates a delimiter fragment (m:d) whose content is a structured equation.</summary>
    public static MathRun Delimiter(Equation content, string open = "(", string close = ")")
    {
        ArgumentNullException.ThrowIfNull(content);

        return new()
        {
            Kind = MathRunKind.Delimiter,
            Base = content.LinearText,
            OpenChar = open,
            CloseChar = close,
            DelimiterContentEquation = content
        };
    }

    /// <summary>
    /// Creates a multi-argument delimiter fragment (m:d) with more than one m:e child — a binomial/case/
    /// matrix-style delimiter group — joined by <paramref name="separator"/> (m:dPr/m:sepChr). The first
    /// argument occupies the ordinary <see cref="Base"/> slot; every argument after it is carried in
    /// <see cref="AdditionalDelimiterArguments"/> so none are dropped on read or write.
    /// </summary>
    public static MathRun Delimiter(IReadOnlyList<string> arguments, string open = "(", string close = ")", string separator = ",")
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
            return Delimiter(string.Empty, open, close);

        return new()
        {
            Kind = MathRunKind.Delimiter,
            Base = arguments[0],
            OpenChar = open,
            CloseChar = close,
            AdditionalDelimiterArguments = arguments.Skip(1).ToList(),
            DelimiterSeparator = separator
        };
    }

    /// <summary>Creates a matrix fragment (m:m) from a grid.</summary>
    public static MathRun MatrixOf(MathMatrix matrix) =>
        new() { Kind = MathRunKind.Matrix, Matrix = matrix };

    /// <summary>Creates an equation-array fragment (m:eqArr) from one-cell rows.</summary>
    public static MathRun EquationArrayOf(MathMatrix array) =>
        new() { Kind = MathRunKind.EquationArray, Matrix = array };

    /// <summary>Creates a function-application fragment (m:func): <paramref name="funcName"/> applied to <paramref name="argument"/>.</summary>
    public static MathRun FunctionApply(string funcName, string argument) =>
        new() { Kind = MathRunKind.FunctionApply, FuncName = funcName, Base = argument };

    /// <summary>Creates a function-application fragment (m:func) whose argument is a structured equation.</summary>
    public static MathRun FunctionApply(string funcName, Equation argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        return new()
        {
            Kind = MathRunKind.FunctionApply,
            FuncName = funcName,
            Base = argument.LinearText,
            FunctionArgumentEquation = argument
        };
    }

    /// <summary>Creates a group-character fragment (m:groupChr): <paramref name="@base"/> grouped by <paramref name="groupChr"/>.</summary>
    public static MathRun GroupCharOf(string @base, string groupChr = "\u23DE", string groupChrPos = "top") =>
        new()
        {
            Kind = MathRunKind.GroupChar,
            Base = @base,
            GroupChr = string.IsNullOrEmpty(groupChr) ? "\u23DE" : groupChr,
            GroupChrPos = string.IsNullOrEmpty(groupChrPos) ? "top" : groupChrPos
        };

    /// <summary>Creates a group-character fragment (m:groupChr) whose grouped base is a structured equation.</summary>
    public static MathRun GroupCharOf(Equation baseEquation, string groupChr = "\u23DE", string groupChrPos = "top")
    {
        ArgumentNullException.ThrowIfNull(baseEquation);

        return new()
        {
            Kind = MathRunKind.GroupChar,
            Base = baseEquation.LinearText,
            GroupChr = string.IsNullOrEmpty(groupChr) ? "\u23DE" : groupChr,
            GroupChrPos = string.IsNullOrEmpty(groupChrPos) ? "top" : groupChrPos,
            DecoratorBaseEquation = baseEquation
        };
    }

    /// <summary>
    /// A best-effort linear (plain-text) rendering of this fragment, used for the host run's fallback
    /// text and for the editor's lightweight visual stand-in.
    /// </summary>
    public string LinearText => LinearTextWithDepth(0);

    internal string LinearTextWithDepth(int depth) => Kind switch
    {
        MathRunKind.Superscript => $"{SlotLinearText(ScriptBaseEquation, Base, depth)}^{SlotLinearText(ScriptSupEquation, Sup, depth)}",
        MathRunKind.Subscript => $"{SlotLinearText(ScriptBaseEquation, Base, depth)}_{SlotLinearText(ScriptSubEquation, Sub, depth)}",
        MathRunKind.SubSuperscript => $"{SlotLinearText(ScriptBaseEquation, Base, depth)}_{SlotLinearText(ScriptSubEquation, Sub, depth)}^{SlotLinearText(ScriptSupEquation, Sup, depth)}",
        MathRunKind.Fraction => $"{SlotLinearText(NumeratorEquation, Numerator, depth)}/{SlotLinearText(DenominatorEquation, Denominator, depth)}",
        MathRunKind.Radical => string.IsNullOrEmpty(SlotLinearText(DegreeEquation, Degree, depth))
            ? $"√({SlotLinearText(RadicandEquation, Base, depth)})"
            : $"{SlotLinearText(DegreeEquation, Degree, depth)}√({SlotLinearText(RadicandEquation, Base, depth)})",
        MathRunKind.NAry => $"{Operator}({SlotLinearText(NAryLowerLimitEquation, Sub, depth)}..{SlotLinearText(NAryUpperLimitEquation, Sup, depth)}) {SlotLinearText(NAryOperandEquation, Base, depth)}".TrimEnd(),
        MathRunKind.Accent => $"{SlotLinearText(DecoratorBaseEquation, Base, depth)}{Accent}",
        MathRunKind.Bar => BarTop ? $"‾{SlotLinearText(DecoratorBaseEquation, Base, depth)}‾" : $"_{SlotLinearText(DecoratorBaseEquation, Base, depth)}_",
        MathRunKind.Delimiter => DelimiterLinearText(depth),
        MathRunKind.Matrix => Matrix?.LinearTextWithDepth(depth) ?? string.Empty,
        MathRunKind.EquationArray => Matrix?.EquationArrayLinearTextWithDepth(depth) ?? string.Empty,
        MathRunKind.FunctionApply => FunctionLinearText(depth),
        MathRunKind.GroupChar => string.Equals(GroupChrPos, "bot", StringComparison.OrdinalIgnoreCase)
            ? $"{SlotLinearText(DecoratorBaseEquation, Base, depth)}{GroupChr}"
            : $"{GroupChr}{SlotLinearText(DecoratorBaseEquation, Base, depth)}",
        _ => Text
    };

    private string FunctionLinearText(int depth)
    {
        var argument = SlotLinearText(FunctionArgumentEquation, Base, depth);
        return string.IsNullOrEmpty(FuncName) ? argument : $"{FuncName}({argument})";
    }

    /// <summary>
    /// Linear text for a delimiter: the first argument plus every entry in
    /// <see cref="AdditionalDelimiterArguments"/> (preferring each entry's structured equation over its
    /// plain-text fallback), joined by <see cref="DelimiterSeparator"/> and wrapped in the open/close glyphs.
    /// </summary>
    private string DelimiterLinearText(int depth)
    {
        var first = SlotLinearText(DelimiterContentEquation, Base, depth);
        if (AdditionalDelimiterArguments.Count == 0)
            return $"{OpenChar}{first}{CloseChar}";

        var rest = string.Join(DelimiterSeparator, AdditionalDelimiterArguments.Select((text, index) =>
            SlotLinearText(
                index < AdditionalDelimiterContentEquations.Count ? AdditionalDelimiterContentEquations[index] : null,
                text,
                depth)));
        return $"{OpenChar}{first}{DelimiterSeparator}{rest}{CloseChar}";
    }

    private static string SlotLinearText(Equation? equation, string fallback, int depth)
    {
        if (equation is null)
            return fallback;

        return depth >= MaxNestedEquationDepth
            ? fallback
            : equation.LinearTextWithDepth(depth + 1);
    }
}

/// <summary>
/// A small dense grid of plain-math-text cells backing a <see cref="MathRunKind.Matrix"/> fragment
/// (OMML m:m). Rows are lists of cell strings, with optional parallel nested cell equations for m:e
/// slots that contain structured OMML. Word re-lays-out the matrix on open.
/// </summary>
public sealed class MathMatrix
{
    /// <summary>The matrix rows, each an ordered list of fallback cell strings.</summary>
    public List<List<string>> Rows { get; } = [];

    /// <summary>Optional structured equations for matrix cells, parallel to <see cref="Rows"/>.</summary>
    public List<List<Equation?>> CellEquations { get; } = [];

    public MathMatrix() { }

    /// <summary>Creates a matrix from rows of cell strings (rows are copied).</summary>
    public MathMatrix(IEnumerable<IEnumerable<string>> rows)
    {
        foreach (var row in rows)
            Rows.Add([.. row]);
    }

    /// <summary>Creates a matrix from rows of optional structured cell equations.</summary>
    public static MathMatrix FromCellEquations(IEnumerable<IEnumerable<Equation?>> rows)
    {
        var matrix = new MathMatrix();
        foreach (var row in rows)
        {
            var textRow = new List<string>();
            var equationRow = new List<Equation?>();
            foreach (var equation in row)
            {
                textRow.Add(equation?.LinearText ?? string.Empty);
                equationRow.Add(equation);
            }

            matrix.Rows.Add(textRow);
            matrix.CellEquations.Add(equationRow);
        }

        return matrix;
    }

    /// <summary>The number of rows.</summary>
    public int RowCount => Math.Max(Rows.Count, CellEquations.Count);

    /// <summary>The number of columns (the longest row's length; 0 for an empty matrix).</summary>
    public int ColumnCount => RowCount == 0
        ? 0
        : Enumerable.Range(0, RowCount).Max(RowColumnCount);

    /// <summary>Returns the structured equation for the addressed cell, if any.</summary>
    public Equation? CellEquationAt(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0)
            return null;

        if (rowIndex >= CellEquations.Count)
            return null;

        var row = CellEquations[rowIndex];
        return columnIndex < row.Count ? row[columnIndex] : null;
    }

    /// <summary>Returns the flattened fallback text for the addressed cell.</summary>
    public string CellTextAt(int rowIndex, int columnIndex) => CellTextAt(rowIndex, columnIndex, depth: 0);

    internal string CellTextAt(int rowIndex, int columnIndex, int depth)
    {
        if (CellEquationAt(rowIndex, columnIndex) is { } equation && depth < MathRun.MaxNestedEquationDepth)
            return equation.LinearTextWithDepth(depth + 1);

        if (rowIndex < 0 || columnIndex < 0 || rowIndex >= Rows.Count)
            return string.Empty;

        var row = Rows[rowIndex];
        return columnIndex < row.Count ? row[columnIndex] : string.Empty;
    }

    private int RowColumnCount(int rowIndex)
    {
        var textColumns = rowIndex < Rows.Count ? Rows[rowIndex].Count : 0;
        var equationColumns = rowIndex < CellEquations.Count ? CellEquations[rowIndex].Count : 0;
        return Math.Max(textColumns, equationColumns);
    }

    /// <summary>A 2×2 identity matrix (1 0 / 0 1) — the Insert > Equation matrix preset.</summary>
    public static MathMatrix Identity2x2() => new([["1", "0"], ["0", "1"]]);

    /// <summary>A best-effort linear rendering: rows joined by "; ", cells within a row by ", ", in brackets.</summary>
    public string LinearText =>
        LinearTextWithDepth(0);

    internal string LinearTextWithDepth(int depth) =>
        "[" + string.Join("; ", Enumerable.Range(0, RowCount)
            .Select(rowIndex => string.Join(", ", Enumerable.Range(0, RowColumnCount(rowIndex))
                .Select(columnIndex => CellTextAt(rowIndex, columnIndex, depth))))) + "]";

    internal string EquationArrayLinearTextWithDepth(int depth) =>
        string.Join("; ", Enumerable.Range(0, RowCount)
            .Select(rowIndex => string.Join(", ", Enumerable.Range(0, RowColumnCount(rowIndex))
                .Select(columnIndex => CellTextAt(rowIndex, columnIndex, depth)))));
}

/// <summary>
/// A basic inline mathematical equation: an ordered list of <see cref="MathRun"/> fragments that maps onto
/// an OMML <c>m:oMath</c>. Carried by a <see cref="Run"/> via <see cref="Run.Equation"/>. Stores the OMML
/// subset FreeW round-trips (plain text, sub/super-scripts, fraction, radical, n-ary, accent, bar,
/// delimiter, matrix, equation array); richer structures degrade to plain math text on read so nothing throws.
/// </summary>
public sealed class Equation
{
    /// <summary>The ordered math fragments making up the equation (left to right).</summary>
    public List<MathRun> Runs { get; } = [];

    /// <summary>
    /// Whether this equation is Word's paragraph-level "Display"/"Professional" layout — the standard
    /// equation centred on its own line, wrapped in <c>m:oMathPara</c> — rather than inline within the
    /// surrounding text flow (a bare <c>m:oMath</c> emitted in place of the host run). Defaults to false
    /// (inline), matching every equation FreeW authored before this flag existed. <see cref="DocxWriter"/>
    /// keys off this flag to choose which OMML container to emit; <see cref="DocxReader"/> sets it when it
    /// recovers an <c>m:oMathPara</c>.
    /// </summary>
    public bool IsDisplayMath { get; set; }

    public Equation() { }

    /// <summary>Creates an equation from an ordered set of fragments.</summary>
    public Equation(IEnumerable<MathRun> runs) => Runs.AddRange(runs);

    /// <summary>Convenience: a single-fragment plain-text equation (e.g. "x + 1").</summary>
    public static Equation FromText(string text) => new([MathRun.PlainText(text)]);

    /// <summary>A best-effort linear (plain-text) rendering of the whole equation (fragments concatenated).</summary>
    public string LinearText => LinearTextWithDepth(0);

    internal string LinearTextWithDepth(int depth)
    {
        if (depth > MathRun.MaxNestedEquationDepth)
            return string.Empty;

        return string.Concat(Runs.Select(r => r.LinearTextWithDepth(depth)));
    }

    /// <summary>Creates an independent copy while preserving nested-equation graph identity.</summary>
    public Equation Clone()
    {
        var equations = new Dictionary<Equation, Equation>(ReferenceEqualityComparer.Instance);
        var matrices = new Dictionary<MathMatrix, MathMatrix>(ReferenceEqualityComparer.Instance);
        return CloneEquation(this, equations, matrices);
    }

    private static Equation CloneEquation(
        Equation source,
        Dictionary<Equation, Equation> equations,
        Dictionary<MathMatrix, MathMatrix> matrices)
    {
        if (equations.TryGetValue(source, out var existing))
            return existing;

        var clone = new Equation { IsDisplayMath = source.IsDisplayMath };
        equations[source] = clone;
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRun(run, equations, matrices));
        return clone;
    }

    private static MathRun CloneRun(
        MathRun source,
        Dictionary<Equation, Equation> equations,
        Dictionary<MathMatrix, MathMatrix> matrices) => source with
    {
        ScriptBaseEquation = CloneOptional(source.ScriptBaseEquation, equations, matrices),
        ScriptSubEquation = CloneOptional(source.ScriptSubEquation, equations, matrices),
        ScriptSupEquation = CloneOptional(source.ScriptSupEquation, equations, matrices),
        NumeratorEquation = CloneOptional(source.NumeratorEquation, equations, matrices),
        DenominatorEquation = CloneOptional(source.DenominatorEquation, equations, matrices),
        RadicandEquation = CloneOptional(source.RadicandEquation, equations, matrices),
        DegreeEquation = CloneOptional(source.DegreeEquation, equations, matrices),
        DelimiterContentEquation = CloneOptional(source.DelimiterContentEquation, equations, matrices),
        AdditionalDelimiterContentEquations = source.AdditionalDelimiterContentEquations.Count == 0
            ? source.AdditionalDelimiterContentEquations
            : source.AdditionalDelimiterContentEquations.Select(e => CloneOptional(e, equations, matrices)).ToList(),
        FunctionArgumentEquation = CloneOptional(source.FunctionArgumentEquation, equations, matrices),
        NAryLowerLimitEquation = CloneOptional(source.NAryLowerLimitEquation, equations, matrices),
        NAryUpperLimitEquation = CloneOptional(source.NAryUpperLimitEquation, equations, matrices),
        NAryOperandEquation = CloneOptional(source.NAryOperandEquation, equations, matrices),
        DecoratorBaseEquation = CloneOptional(source.DecoratorBaseEquation, equations, matrices),
        Matrix = CloneMatrix(source.Matrix, equations, matrices)
    };

    private static Equation? CloneOptional(
        Equation? source,
        Dictionary<Equation, Equation> equations,
        Dictionary<MathMatrix, MathMatrix> matrices) =>
        source is null ? null : CloneEquation(source, equations, matrices);

    private static MathMatrix? CloneMatrix(
        MathMatrix? source,
        Dictionary<Equation, Equation> equations,
        Dictionary<MathMatrix, MathMatrix> matrices)
    {
        if (source is null)
            return null;
        if (matrices.TryGetValue(source, out var existing))
            return existing;

        var clone = new MathMatrix();
        matrices[source] = clone;
        foreach (var row in source.Rows)
            clone.Rows.Add([.. row]);
        foreach (var row in source.CellEquations)
            clone.CellEquations.Add(row.Select(cell => CloneOptional(cell, equations, matrices)).ToList());
        return clone;
    }
}
