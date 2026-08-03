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

    /// <summary>
    /// Math properties inherited from the containing math graphic or document.
    /// Nullable values distinguish omitted properties from explicit values so
    /// inheritance can be resolved one property at a time.
    /// </summary>
    public sealed record MathProperties(
        MathParagraphBinaryBreak? BinaryBreak = null,
        MathParagraphBinarySubtraction? BinarySubtraction = null,
        string? MathFontFamily = null,
        bool? SmallFraction = null,
        MathParagraphJustification? DefaultJustification = null)
    {
        public bool HasValues =>
            BinaryBreak.HasValue ||
            BinarySubtraction.HasValue ||
            !string.IsNullOrWhiteSpace(MathFontFamily) ||
            SmallFraction.HasValue ||
            DefaultJustification.HasValue;

        public MathProperties Overlay(MathProperties? overriding) => overriding is null
            ? this
            : new MathProperties(
                overriding.BinaryBreak ?? BinaryBreak,
                overriding.BinarySubtraction ?? BinarySubtraction,
                overriding.MathFontFamily ?? MathFontFamily,
                overriding.SmallFraction ?? SmallFraction,
                overriding.DefaultJustification ?? DefaultJustification);
    }

    /// <summary>Math alphabet requested by <c>m:rPr/m:scr</c>.</summary>
    public enum MathAlphabet
    {
        Default,
        Roman,
        Script,
        Fraktur,
        DoubleStruck,
        SansSerif,
        Monospace
    }

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

        /// <summary>True when m:rPr/m:sty requests bold math text.</summary>
        public bool IsBold { get; }

        /// <summary>True when m:rPr/m:lit requests literal interpretation of the run text.</summary>
        public bool IsLiteral { get; }

        /// <summary>Requested math alphabet for ASCII letter/digit remapping.</summary>
        public MathAlphabet Alphabet { get; }

        /// <summary>
        /// When m:rPr/m:nor is absent the run uses the math italic style.
        /// When m:nor is set, or when m:lit is set without an explicit visual
        /// style, the parser can mark the run upright before shared layout.
        /// </summary>
        public Run(
            string text,
            bool isItalic = true,
            bool isBold = false,
            MathAlphabet alphabet = MathAlphabet.Default,
            bool isLiteral = false)
        {
            Text = text;
            IsItalic = isItalic;
            IsBold = isBold;
            Alphabet = alphabet;
            IsLiteral = isLiteral;
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

        /// <summary>True when <c>m:sSubSupPr/m:alnScr</c> aligns the script stack by right edge.</summary>
        public bool AlignScripts { get; }

        public SubSup(MathNode @base, MathNode sub, MathNode sup, bool alignScripts = false)
        {
            Base = @base; Sub = sub; Sup = sup;
            AlignScripts = alignScripts;
        }
    }

    /// <summary><c>m:sPre</c> -- base with pre-subscript and pre-superscript to its left.</summary>
    public sealed class PreSubSup : MathNode
    {
        public MathNode Base { get; }
        public new MathNode Sub { get; }
        public new MathNode Sup { get; }
        public PreSubSup(MathNode @base, MathNode sub, MathNode sup)
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

        /// <summary>True when m:naryPr/m:grow requests vertical operator growth.</summary>
        public bool GrowOperator { get; }

        public MathNode? SubLimit { get; }
        public MathNode? SupLimit { get; }
        public MathNode Operand { get; }

        public Nary(
            string operatorChar,
            bool limitsAboveBelow,
            MathNode? subLimit,
            MathNode? supLimit,
            MathNode operand,
            bool growOperator = false)
        {
            OperatorChar = operatorChar;
            LimitsAboveBelow = limitsAboveBelow;
            GrowOperator = growOperator;
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
        public enum DelimiterShape
        {
            Match,
            Centered
        }

        public string BegChar { get; }
        public string EndChar { get; }

        /// <summary>
        /// The separator glyph drawn between consecutive <see cref="Elements"/> when
        /// there are two or more (per ECMA-376 §22.1.2.20 CT_DPr m:sepChr).
        /// Default (element absent) is ",". An explicit empty value means no
        /// separator glyph is drawn. Irrelevant when there is a single element.
        /// </summary>
        public string SepChar { get; }

        /// <summary>
        /// Whether delimiter glyphs grow to match the inner expression height
        /// (ECMA-376 CT_DPr m:grow). Defaults to true when absent.
        /// </summary>
        public bool Grow { get; }

        /// <summary>
        /// Delimiter shape from <c>m:dPr/m:shp</c>. Match keeps the existing
        /// stretchy delimiter behavior; centered uses ordinary glyph height.
        /// </summary>
        public DelimiterShape Shape { get; }

        /// <summary>The inner expressions (one per m:e child).</summary>
        public IReadOnlyList<MathNode> Elements { get; }

        public Delim(
            string begChar,
            string endChar,
            IReadOnlyList<MathNode> elements,
            string sepChar = ",",
            bool grow = true,
            DelimiterShape shape = DelimiterShape.Match)
        {
            BegChar = begChar;
            EndChar = endChar;
            Elements = elements;
            SepChar = sepChar;
            Grow = grow;
            Shape = shape;
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

    /// <summary><c>m:box</c> -- transparent wrapper around a base expression.</summary>
    public sealed class Box : MathNode
    {
        public MathNode Base { get; }

        /// <summary>True when <c>m:boxPr/m:opEmu</c> makes the wrapped expression behave as one operator.</summary>
        public bool OperatorEmulator { get; }

        public Box(MathNode @base, bool operatorEmulator = false)
        {
            Base = @base;
            OperatorEmulator = operatorEmulator;
        }
    }

    /// <summary><c>m:argPr/m:argSz</c> -- script-size adjustment for a math argument.</summary>
    public sealed class ArgSize : MathNode
    {
        public MathNode Base { get; }

        /// <summary>Argument script-size delta, clamped to the OMML -2..2 range.</summary>
        public int Adjustment { get; }

        public ArgSize(MathNode @base, int adjustment)
        {
            Base = @base;
            Adjustment = System.Math.Clamp(adjustment, -2, 2);
        }
    }

    /// <summary><c>m:phant</c> -- optionally hidden expression that still reserves selected metrics.</summary>
    public sealed class Phantom : MathNode
    {
        public MathNode Base { get; }
        public bool Show { get; }
        public bool ZeroWidth { get; }
        public bool ZeroAscent { get; }
        public bool ZeroDescent { get; }

        /// <summary>
        /// Parsed <c>m:transp</c> flag consumed by bounded shared row spacing-class layout.
        /// </summary>
        public bool TransparentSpacing { get; }

        public Phantom(
            MathNode @base,
            bool show = true,
            bool zeroWidth = false,
            bool zeroAscent = false,
            bool zeroDescent = false,
            bool transparentSpacing = false)
        {
            Base = @base;
            Show = show;
            ZeroWidth = zeroWidth;
            ZeroAscent = zeroAscent;
            ZeroDescent = zeroDescent;
            TransparentSpacing = transparentSpacing;
        }
    }

    /// <summary><c>m:borderBox</c> -- a box with optional visible borders around a base expression.</summary>
    public sealed class BorderBox : MathNode
    {
        public MathNode Base { get; }
        public bool ShowTop { get; }
        public bool ShowBottom { get; }
        public bool ShowLeft { get; }
        public bool ShowRight { get; }
        public bool StrikeHorizontal { get; }
        public bool StrikeVertical { get; }
        public bool StrikeBottomLeftToTopRight { get; }
        public bool StrikeTopLeftToBottomRight { get; }

        public BorderBox(
            MathNode @base,
            bool showTop = true,
            bool showBottom = true,
            bool showLeft = true,
            bool showRight = true,
            bool strikeHorizontal = false,
            bool strikeVertical = false,
            bool strikeBottomLeftToTopRight = false,
            bool strikeTopLeftToBottomRight = false)
        {
            Base = @base;
            ShowTop = showTop;
            ShowBottom = showBottom;
            ShowLeft = showLeft;
            ShowRight = showRight;
            StrikeHorizontal = strikeHorizontal;
            StrikeVertical = strikeVertical;
            StrikeBottomLeftToTopRight = strikeBottomLeftToTopRight;
            StrikeTopLeftToBottomRight = strikeTopLeftToBottomRight;
        }
    }

    // ── Group character ──────────────────────────────────────────────────────

    /// <summary><c>m:groupChr</c> — a grouping character above/below the base.</summary>
    public sealed class GroupChr : MathNode
    {
        public enum GroupChrVerticalJustification
        {
            /// <summary>Legacy constructed-node behavior: keep the grouped expression baseline.</summary>
            Baseline,
            /// <summary><c>m:groupChrPr/m:vertJc</c> top: align the group-character object top to the baseline.</summary>
            Top,
            /// <summary><c>m:groupChrPr/m:vertJc</c> bot: align the group-character object bottom to the baseline.</summary>
            Bottom
        }

        public string GrpChar { get; }
        public MathNode Base { get; }
        public bool IsAbove { get; }
        public GroupChrVerticalJustification VerticalJustification { get; }
        public GroupChr(
            string grpChar,
            MathNode @base,
            bool isAbove = true,
            GroupChrVerticalJustification verticalJustification = GroupChrVerticalJustification.Baseline)
        {
            GrpChar = grpChar;
            Base = @base;
            IsAbove = isAbove;
            VerticalJustification = verticalJustification;
        }
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

        public enum MatrixBaseJustification
        {
            Top,
            Center,
            Bottom
        }

        public enum MatrixSpacingRule
        {
            Single = 0,
            OneAndHalf = 1,
            Double = 2,
            Exactly = 3,
            Multiple = 4
        }

        /// <summary>Rows x Columns grid of nodes.</summary>
        public IReadOnlyList<IReadOnlyList<MathNode>> Rows { get; }

        /// <summary>Optional per-column alignment metadata; missing entries default to center.</summary>
        public IReadOnlyList<MatrixColumnAlignment> ColumnAlignments { get; }

        /// <summary>Vertical matrix baseline justification from m:mPr/m:baseJc.</summary>
        public MatrixBaseJustification BaseJustification { get; }

        /// <summary>Optional row-spacing rule from m:mPr/m:rSpRule.</summary>
        public MatrixSpacingRule? RowSpacingRule { get; }

        /// <summary>Optional row-spacing value from m:mPr/m:rSp.</summary>
        public int? RowSpacing { get; }

        /// <summary>Optional column-gap rule from m:mPr/m:cGpRule.</summary>
        public MatrixSpacingRule? ColumnGapRule { get; }

        /// <summary>Optional column-gap value from m:mPr/m:cGp.</summary>
        public int? ColumnGap { get; }

        /// <summary>Optional minimum column width from m:mPr/m:cSp, in twips.</summary>
        public int? ColumnSpacingTwips { get; }

        /// <summary>True when m:mPr/m:plcHide hides visible placeholders for empty matrix cells.</summary>
        public bool HidePlaceholders { get; }

        public Matrix(
            IReadOnlyList<IReadOnlyList<MathNode>> rows,
            IReadOnlyList<MatrixColumnAlignment>? columnAlignments = null,
            MatrixBaseJustification baseJustification = MatrixBaseJustification.Center,
            MatrixSpacingRule? rowSpacingRule = null,
            int? rowSpacing = null,
            MatrixSpacingRule? columnGapRule = null,
            int? columnGap = null,
            int? columnSpacingTwips = null,
            bool hidePlaceholders = false)
        {
            Rows = rows;
            ColumnAlignments = columnAlignments ?? System.Array.Empty<MatrixColumnAlignment>();
            BaseJustification = baseJustification;
            RowSpacingRule = rowSpacingRule;
            RowSpacing = rowSpacing;
            ColumnGapRule = columnGapRule;
            ColumnGap = columnGap;
            ColumnSpacingTwips = columnSpacingTwips;
            HidePlaceholders = hidePlaceholders;
        }
    }

    // ── Equation array ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>m:eqArr</c> — equation array with each <c>m:e</c> rendered as a stacked row.
    /// Optional row alignment points come from invisible direct <c>m:aln</c> markers.
    /// </summary>
    public sealed class EqArray : MathNode
    {
        public enum EqArrayBaseJustification
        {
            Top,
            Center,
            Bottom
        }

        public enum EqArraySpacingRule
        {
            Single = 0,
            OneAndHalf = 1,
            Double = 2,
            Exactly = 3,
            Multiple = 4
        }

        /// <summary>The ordered row expressions from direct m:e children.</summary>
        public IReadOnlyList<MathNode> Rows { get; }

        /// <summary>
        /// Optional direct-child index before which each row's invisible m:aln marker appeared.
        /// Missing entries or null values mean the row has no alignment marker.
        /// </summary>
        public IReadOnlyList<int?> AlignmentPointIndices { get; }

        /// <summary>Vertical equation-array baseline justification from m:eqArrPr/m:baseJc.</summary>
        public EqArrayBaseJustification BaseJustification { get; }

        /// <summary>Optional row-spacing rule from m:eqArrPr/m:rSpRule.</summary>
        public EqArraySpacingRule? RowSpacingRule { get; }

        /// <summary>Optional row-spacing value from m:eqArrPr/m:rSp.</summary>
        public int? RowSpacing { get; }

        public EqArray(
            IReadOnlyList<MathNode> rows,
            IReadOnlyList<int?>? alignmentPointIndices = null,
            EqArrayBaseJustification baseJustification = EqArrayBaseJustification.Center,
            EqArraySpacingRule? rowSpacingRule = null,
            int? rowSpacing = null)
        {
            Rows = rows;
            AlignmentPointIndices = alignmentPointIndices ?? System.Array.Empty<int?>();
            BaseJustification = baseJustification;
            RowSpacingRule = rowSpacingRule;
            RowSpacing = rowSpacing;
        }

        public int? GetAlignmentPointIndex(int rowIndex) =>
            rowIndex >= 0 && rowIndex < AlignmentPointIndices.Count
                ? AlignmentPointIndices[rowIndex]
                : null;
    }

    // ── Row (horizontal sequence) ────────────────────────────────────────────

    /// <summary>
    /// A horizontal sequence of sibling nodes (corresponds to the children of
    /// <c>m:oMath</c> / <c>m:e</c> / <c>m:num</c> / <c>m:den</c> / etc.).
    /// </summary>
    public enum MathParagraphJustification
    {
        Left,
        Center,
        Right,
        CenterGroup
    }

    /// <summary>
    /// Placement of a binary operator when a math paragraph wraps at that
    /// operator (m:brkBin). The default is Before, matching Office Math.
    /// </summary>
    public enum MathParagraphBinaryBreak
    {
        Before,
        After,
        Repeat
    }

    /// <summary>
    /// Sign pair used when a subtraction operator is repeated across a math
    /// paragraph break (m:brkBinSub). The default is MinusMinus.
    /// </summary>
    public enum MathParagraphBinarySubtraction
    {
        MinusMinus,
        PlusMinus,
        MinusPlus
    }

    /// <summary>
    /// <c>m:oMathPara</c> wrapper carrying paragraph-level equation alignment metadata.
    /// Alignment is applied by shared layout only when an available paragraph width is supplied.
    /// </summary>
    public sealed class MathParagraph : MathNode
    {
        public MathNode Content { get; }
        public MathParagraphJustification Justification { get; }
        public MathParagraphBinaryBreak BinaryBreak { get; }
        public MathParagraphBinarySubtraction BinarySubtraction { get; }

        /// <summary>Optional equation-wide font from <c>m:mathPr/m:mathFont</c>.</summary>
        public string? MathFontFamily { get; }

        /// <summary>Resolved <c>m:mathPr/m:smallFrac</c> setting.</summary>
        public bool? SmallFraction { get; }

        public MathParagraph(
            MathNode content,
            MathParagraphJustification justification,
            MathParagraphBinaryBreak binaryBreak = MathParagraphBinaryBreak.Before,
            MathParagraphBinarySubtraction binarySubtraction = MathParagraphBinarySubtraction.MinusMinus,
            string? mathFontFamily = null,
            bool? smallFraction = null)
        {
            Content = content;
            Justification = justification;
            BinaryBreak = binaryBreak;
            BinarySubtraction = binarySubtraction;
            MathFontFamily = mathFontFamily;
            SmallFraction = smallFraction;
        }
    }

    /// <summary>
    /// Root wrapper used when inline OMML inherits properties from its
    /// containing graphic or document and has no paragraph node to carry them.
    /// </summary>
    public sealed class MathRoot : MathNode
    {
        public MathNode Content { get; }
        public MathProperties Properties { get; }

        public MathRoot(MathNode content, MathProperties properties)
        {
            Content = content;
            Properties = properties;
        }
    }

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

