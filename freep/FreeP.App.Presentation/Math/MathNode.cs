namespace FreeP.App.Compositor.MathLayout;

// ── OMML Math node tree (Theme 27) ────────────────────────────────────────────
//
// Each OMML construct is parsed into one of these sealed discriminated-union
// nodes.  The full tree is framework-free so both WPF and Avalonia can share it.
//
// Unsupported constructs collapse into MathNode.Unknown whose Children list
// collects the fallback-text runs parsed from their m:t descendants.

/// <summary>Abstract base for all parsed OMML nodes.</summary>
public abstract class MathNode
{
    private MathNode() { }

    // ── Leaf: text run ──────────────────────────────────────────────────────

    /// <summary>
    /// A math run of text: corresponds to <c>m:r/m:t</c>.
    /// Variables are italic; operators, digits and functions are upright.
    /// </summary>
    public sealed class Run : MathNode
    {
        /// <summary>The text content of the m:t element.</summary>
        public string Text { get; }

        /// <summary>True when m:rPr/m:nor is absent (italic = normal math style).</summary>
        public bool IsItalic { get; }

        /// <summary>
        /// When m:rPr/m:lit is present or m:nor is absent the run uses the math italic style.
        /// When m:nor is set (literal/roman) the run is upright.
        /// </summary>
        public Run(string text, bool isItalic = true)
        {
            Text = text;
            IsItalic = isItalic;
        }
    }

    // ── Fraction ────────────────────────────────────────────────────────────

    /// <summary>
    /// The fraction bar style: <c>m:fPr/m:type</c> per ECMA-376 §22.1.2.34 (CT_FPr) /
    /// §22.1.2.35 (ST_FType).
    /// </summary>
    public enum FracType
    {
        /// <summary>"bar" (default) — numerator over denominator with a horizontal bar.</summary>
        Bar,
        /// <summary>"skw" — skewed: a compact diagonal fraction.</summary>
        Skewed,
        /// <summary>"lin" — linear: numerator, slash, denominator inline on the baseline.</summary>
        Linear,
        /// <summary>"noBar" — stacked numerator over denominator with no bar (binomial style).</summary>
        NoBar
    }

    /// <summary>
    /// Fraction: <c>m:f</c> with <c>m:num</c> and <c>m:den</c>.
    /// Rendered per <see cref="Type"/> (default: numerator over denominator with a
    /// horizontal bar).
    /// </summary>
    public sealed class Frac : MathNode
    {
        public MathNode Numerator { get; }
        public MathNode Denominator { get; }

        /// <summary>The fraction bar style (m:fPr/m:type). Default <see cref="FracType.Bar"/>.</summary>
        public FracType Type { get; }

        public Frac(MathNode numerator, MathNode denominator, FracType type = FracType.Bar)
        {
            Numerator = numerator;
            Denominator = denominator;
            Type = type;
        }
    }

    // ── Superscript / Subscript / SubSup ────────────────────────────────────

    /// <summary><c>m:sSup</c> — base with raised superscript.</summary>
    public sealed class Sup : MathNode
    {
        public MathNode Base { get; }
        public MathNode Script { get; }
        public Sup(MathNode @base, MathNode script) { Base = @base; Script = script; }
    }

    /// <summary><c>m:sSub</c> — base with lowered subscript.</summary>
    public sealed class Sub : MathNode
    {
        public MathNode Base { get; }
        public MathNode Script { get; }
        public Sub(MathNode @base, MathNode script) { Base = @base; Script = script; }
    }

    /// <summary><c>m:sSubSup</c> — base with both subscript and superscript.</summary>
    public sealed class SubSup : MathNode
    {
        public MathNode Base { get; }
        public new MathNode Sub { get; }
        public new MathNode Sup { get; }
        public SubSup(MathNode @base, MathNode sub, MathNode sup)
        {
            Base = @base; Sub = sub; Sup = sup;
        }
    }

    // ── Radical ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>m:rad</c> — radical with optional degree index and radicand.
    /// The degree is absent for a plain square root.
    /// </summary>
    public sealed class Rad : MathNode
    {
        /// <summary>Index/degree (e.g. 3 for ³v). Null = square root.</summary>
        public MathNode? Degree { get; }
        public MathNode Radicand { get; }
        public Rad(MathNode? degree, MathNode radicand)
        {
            Degree = degree;
            Radicand = radicand;
        }
    }

    // ── N-ary (sum, product, integral) ──────────────────────────────────────

    /// <summary>
    /// <c>m:nary</c> — n-ary operator (? ? ? etc.) with optional sub/sup limits and operand.
    /// </summary>
    public sealed class Nary : MathNode
    {
        /// <summary>The n-ary character glyph, e.g. "∫", "∑", "∏". Default "∫" (integral).</summary>
        public string OperatorChar { get; }

        /// <summary>True for ?/? style (limits above/below); false for ? style (as scripts).</summary>
        public bool LimitsAboveBelow { get; }

        public MathNode? SubLimit { get; }
        public MathNode? SupLimit { get; }
        public MathNode Operand { get; }

        public Nary(string operatorChar, bool limitsAboveBelow, MathNode? subLimit, MathNode? supLimit, MathNode operand)
        {
            OperatorChar = operatorChar;
            LimitsAboveBelow = limitsAboveBelow;
            SubLimit = subLimit;
            SupLimit = supLimit;
            Operand = operand;
        }
    }

    // ── Function apply ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>m:limLow</c> / <c>m:limUpp</c> — a base expression with a centered
    /// lower or upper limit.
    /// </summary>
    public sealed class Limit : MathNode
    {
        public MathNode Base { get; }
        public MathNode LimitValue { get; }
        public bool IsUpper { get; }

        public Limit(MathNode @base, MathNode limitValue, bool isUpper)
        {
            Base = @base;
            LimitValue = limitValue;
            IsUpper = isUpper;
        }
    }

    /// <summary><c>m:func</c> — function name applied to argument (e.g. sin x).</summary>
    public sealed class Func : MathNode
    {
        public MathNode FunctionName { get; }
        public MathNode Argument { get; }
        public Func(MathNode functionName, MathNode argument)
        {
            FunctionName = functionName;
            Argument = argument;
        }
    }

    // ── Delimiter ────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>m:d</c> — delimiter with auto-sized brackets around separator-separated children.
    /// Default brackets: "(" and ")". Default separator (m:sepChr): ",".
    /// </summary>
    public sealed class Delim : MathNode
    {
        public string BegChar { get; }
        public string EndChar { get; }

        /// <summary>
        /// The separator glyph drawn between consecutive <see cref="Elements"/> when
        /// there are two or more (per ECMA-376 §22.1.2.20 CT_DPr m:sepChr).
        /// Default (element absent) is ",". An explicit empty value means no
        /// separator glyph is drawn. Irrelevant when there is a single element.
        /// </summary>
        public string SepChar { get; }

        /// <summary>The inner expressions (one per m:e child).</summary>
        public IReadOnlyList<MathNode> Elements { get; }

        public Delim(string begChar, string endChar, IReadOnlyList<MathNode> elements, string sepChar = ",")
        {
            BegChar = begChar;
            EndChar = endChar;
            Elements = elements;
            SepChar = sepChar;
        }
    }

    // ── Accent (hat, bar over base) ──────────────────────────────────────────

    /// <summary>
    /// <c>m:acc</c> — accent character over a base.
    /// Common: "^" (hat), "̄" (bar), "⃗" (arrow), "̃" (tilde).
    /// </summary>
    public sealed class Acc : MathNode
    {
        /// <summary>Unicode accent character (default "^").</summary>
        public string AccentChar { get; }
        public MathNode Base { get; }
        public Acc(string accentChar, MathNode @base) { AccentChar = accentChar; Base = @base; }
    }

    // ── Bar ──────────────────────────────────────────────────────────────────

    /// <summary><c>m:bar</c> — overline (or underline) drawn over the base.</summary>
    public sealed class Bar : MathNode
    {
        public MathNode Base { get; }
        /// <summary>True = overline; false = underline.</summary>
        public bool IsOver { get; }
        public Bar(MathNode @base, bool isOver = true) { Base = @base; IsOver = isOver; }
    }

    // ── Group character ──────────────────────────────────────────────────────

    /// <summary><c>m:groupChr</c> — a grouping character above/below the base.</summary>
    public sealed class GroupChr : MathNode
    {
        public string GrpChar { get; }
        public MathNode Base { get; }
        public bool IsAbove { get; }
        public GroupChr(string grpChar, MathNode @base, bool isAbove = true)
        { GrpChar = grpChar; Base = @base; IsAbove = isAbove; }
    }

    // ── Matrix ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>m:m</c> — matrix with rows <c>m:mr</c> each containing cells <c>m:e</c>.
    /// Usually appears inside a <see cref="Delim"/>.
    /// </summary>
    public sealed class Matrix : MathNode
    {
        public enum MatrixColumnAlignment
        {
            Left,
            Center,
            Right
        }

        /// <summary>Rows x Columns grid of nodes.</summary>
        public IReadOnlyList<IReadOnlyList<MathNode>> Rows { get; }

        /// <summary>Optional per-column alignment metadata; missing entries default to center.</summary>
        public IReadOnlyList<MatrixColumnAlignment> ColumnAlignments { get; }

        public Matrix(
            IReadOnlyList<IReadOnlyList<MathNode>> rows,
            IReadOnlyList<MatrixColumnAlignment>? columnAlignments = null)
        {
            Rows = rows;
            ColumnAlignments = columnAlignments ?? System.Array.Empty<MatrixColumnAlignment>();
        }
    }

    // ── Equation array ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>m:eqArr</c> — equation array with each <c>m:e</c> rendered as a stacked row.
    /// Alignment-point semantics are intentionally left for a future slice.
    /// </summary>
    public sealed class EqArray : MathNode
    {
        /// <summary>The ordered row expressions from direct m:e children.</summary>
        public IReadOnlyList<MathNode> Rows { get; }
        public EqArray(IReadOnlyList<MathNode> rows) { Rows = rows; }
    }

    // ── Row (horizontal sequence) ────────────────────────────────────────────

    /// <summary>
    /// A horizontal sequence of sibling nodes (corresponds to the children of
    /// <c>m:oMath</c> / <c>m:e</c> / <c>m:num</c> / <c>m:den</c> / etc.).
    /// </summary>
    public sealed class Row : MathNode
    {
        public IReadOnlyList<MathNode> Children { get; }
        public Row(IReadOnlyList<MathNode> children) { Children = children; }
    }

    // ── Unknown / fallback ───────────────────────────────────────────────────

    /// <summary>
    /// Any OMML element not explicitly handled.
    /// The plain fallback text is collected from its m:t descendants and
    /// rendered inline as an upright math run.
    /// </summary>
    public sealed class Unknown : MathNode
    {
        /// <summary>Flattened m:t text from all descendants.</summary>
        public string FallbackText { get; }
        public Unknown(string fallbackText) { FallbackText = fallbackText; }
    }
}

