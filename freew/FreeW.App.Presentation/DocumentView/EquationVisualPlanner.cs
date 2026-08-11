using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum EquationVisualSegmentRole
{
    Text,
    Base,
    Superscript,
    Subscript,
    FractionNumerator,
    FractionBar,
    FractionDenominator,
    RadicalDegree,
    RadicalSign,
    RadicalRadicand,
    NAryOperator,
    NAryLowerLimit,
    NAryUpperLimit,
    NAryOperand,
    MatrixOpenDelimiter,
    MatrixCell,
    MatrixColumnSeparator,
    MatrixRowSeparator,
    MatrixCloseDelimiter,
    AccentMark,
    AccentBase,
    BarMark,
    BarBase,
    DelimiterOpen,
    DelimiterContent,
    DelimiterClose,
    GroupCharMark,
    GroupCharBase,
    FunctionName,
    FunctionOpenDelimiter,
    FunctionArgument,
    FunctionCloseDelimiter,
    LinearFallback,
    DelimiterSeparator
}

public enum EquationVisualElementKind
{
    Segments,
    Fraction,
    Radical,
    NAry,
    Matrix,
    EquationArray,
    Accent,
    Bar,
    Delimiter,
    GroupChar,
    FunctionApply
}

public enum EquationVisualBaselineRole
{
    Normal,
    Superscript,
    Subscript
}

public sealed record EquationVisualStyle(
    string FontFamily,
    bool Italic,
    double FontSizeScale,
    EquationVisualBaselineRole BaselineRole,
    double BaselineOffsetEm);

public sealed record EquationVisualSegment(
    string Text,
    EquationVisualSegmentRole Role,
    EquationVisualStyle Style);

public sealed record EquationVisualMatrixCell(
    int RowIndex,
    int ColumnIndex,
    string Text,
    EquationVisualPlan? CellPlan = null);

public sealed record EquationVisualMatrixRow(
    int RowIndex,
    IReadOnlyList<EquationVisualMatrixCell> Cells);

public sealed record EquationVisualElement(
    EquationVisualElementKind Kind,
    string LinearText,
    IReadOnlyList<EquationVisualSegment> Segments,
    string Numerator,
    string Denominator,
    string Radicand,
    string Degree,
    string Operator = "",
    string LowerLimit = "",
    string UpperLimit = "",
    string Operand = "")
{
    public IReadOnlyList<EquationVisualMatrixRow> MatrixRows { get; init; } = [];
    public string BaseText { get; init; } = string.Empty;
    public string Accent { get; init; } = string.Empty;
    public bool BarTop { get; init; } = true;
    public string OpenDelimiter { get; init; } = string.Empty;
    public string CloseDelimiter { get; init; } = string.Empty;
    public string GroupCharacter { get; init; } = string.Empty;
    public string GroupCharacterPosition { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string FunctionArgument { get; init; } = string.Empty;
    public string ScriptSubscriptText { get; init; } = string.Empty;
    public string ScriptSuperscriptText { get; init; } = string.Empty;
    public EquationVisualPlan? ScriptBasePlan { get; init; }
    public EquationVisualPlan? ScriptSubscriptPlan { get; init; }
    public EquationVisualPlan? ScriptSuperscriptPlan { get; init; }
    public EquationVisualPlan? NumeratorPlan { get; init; }
    public EquationVisualPlan? DenominatorPlan { get; init; }
    public EquationVisualPlan? RadicandPlan { get; init; }
    public EquationVisualPlan? DegreePlan { get; init; }
    public EquationVisualPlan? DelimiterContentPlan { get; init; }
    /// <summary>
    /// Multi-argument delimiter arguments beyond the first (index 0 lives in <see cref="BaseText"/>/
    /// <see cref="DelimiterContentPlan"/>). Empty for the ordinary single-argument delimiter.
    /// </summary>
    public IReadOnlyList<string> AdditionalDelimiterArgumentTexts { get; init; } = [];
    /// <summary>Structured plans parallel to <see cref="AdditionalDelimiterArgumentTexts"/> (null at a plain-text index).</summary>
    public IReadOnlyList<EquationVisualPlan?> AdditionalDelimiterArgumentPlans { get; init; } = [];
    /// <summary>Separator glyph placed between multi-argument delimiter arguments (e.g. "," ).</summary>
    public string DelimiterSeparatorText { get; init; } = ",";
    public EquationVisualPlan? FunctionArgumentPlan { get; init; }
    public EquationVisualPlan? NAryLowerLimitPlan { get; init; }
    public EquationVisualPlan? NAryUpperLimitPlan { get; init; }
    public EquationVisualPlan? NAryOperandPlan { get; init; }
    public EquationVisualPlan? AccentBasePlan { get; init; }
    public EquationVisualPlan? BarBasePlan { get; init; }
    public EquationVisualPlan? GroupCharBasePlan { get; init; }

    public int MatrixRowCount => MatrixRows.Count;

    public int MatrixColumnCount => MatrixRows.Count == 0
        ? 0
        : MatrixRows.Max(row => row.Cells.Count);

    public bool GroupCharacterTop => !string.Equals(
        GroupCharacterPosition,
        "bot",
        StringComparison.OrdinalIgnoreCase);

    public static EquationVisualElement FromSegments(
        string linearText,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(EquationVisualElementKind.Segments, linearText, segments, string.Empty, string.Empty, string.Empty, string.Empty);

    public static EquationVisualElement Script(
        string linearText,
        string baseText,
        string subscriptText,
        string superscriptText,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? basePlan = null,
        EquationVisualPlan? subscriptPlan = null,
        EquationVisualPlan? superscriptPlan = null) =>
        new(EquationVisualElementKind.Segments, linearText, segments, string.Empty, string.Empty, string.Empty, string.Empty)
        {
            BaseText = baseText,
            ScriptSubscriptText = subscriptText,
            ScriptSuperscriptText = superscriptText,
            ScriptBasePlan = basePlan,
            ScriptSubscriptPlan = subscriptPlan,
            ScriptSuperscriptPlan = superscriptPlan
        };

    public static EquationVisualElement Fraction(
        string linearText,
        string numerator,
        string denominator,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? numeratorPlan = null,
        EquationVisualPlan? denominatorPlan = null) =>
        new(EquationVisualElementKind.Fraction, linearText, segments, numerator, denominator, string.Empty, string.Empty)
        {
            NumeratorPlan = numeratorPlan,
            DenominatorPlan = denominatorPlan
        };

    public static EquationVisualElement Radical(
        string linearText,
        string radicand,
        string degree,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? radicandPlan = null,
        EquationVisualPlan? degreePlan = null) =>
        new(EquationVisualElementKind.Radical, linearText, segments, string.Empty, string.Empty, radicand, degree)
        {
            RadicandPlan = radicandPlan,
            DegreePlan = degreePlan
        };

    public static EquationVisualElement NAry(
        string linearText,
        string @operator,
        string lowerLimit,
        string upperLimit,
        string operand,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? lowerLimitPlan = null,
        EquationVisualPlan? upperLimitPlan = null,
        EquationVisualPlan? operandPlan = null) =>
        new(
            EquationVisualElementKind.NAry,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            @operator,
            lowerLimit,
            upperLimit,
            operand)
        {
            NAryLowerLimitPlan = lowerLimitPlan,
            NAryUpperLimitPlan = upperLimitPlan,
            NAryOperandPlan = operandPlan
        };

    public static EquationVisualElement Matrix(
        string linearText,
        IReadOnlyList<EquationVisualMatrixRow> rows,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.Matrix,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            MatrixRows = rows
        };

    public static EquationVisualElement EquationArray(
        string linearText,
        IReadOnlyList<EquationVisualMatrixRow> rows,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.EquationArray,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            MatrixRows = rows
        };

    public static EquationVisualElement AccentElement(
        string linearText,
        string baseText,
        string accent,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? basePlan = null) =>
        new(
            EquationVisualElementKind.Accent,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            Accent = accent,
            AccentBasePlan = basePlan
        };

    public static EquationVisualElement Bar(
        string linearText,
        string baseText,
        bool barTop,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? basePlan = null) =>
        new(
            EquationVisualElementKind.Bar,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            BarTop = barTop,
            BarBasePlan = basePlan
        };

    public static EquationVisualElement Delimiter(
        string linearText,
        string baseText,
        string openDelimiter,
        string closeDelimiter,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? delimiterContentPlan = null,
        IReadOnlyList<string>? additionalArgumentTexts = null,
        IReadOnlyList<EquationVisualPlan?>? additionalArgumentPlans = null,
        string delimiterSeparator = ",") =>
        new(
            EquationVisualElementKind.Delimiter,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            OpenDelimiter = openDelimiter,
            CloseDelimiter = closeDelimiter,
            DelimiterContentPlan = delimiterContentPlan,
            AdditionalDelimiterArgumentTexts = additionalArgumentTexts ?? [],
            AdditionalDelimiterArgumentPlans = additionalArgumentPlans ?? [],
            DelimiterSeparatorText = delimiterSeparator
        };

    public static EquationVisualElement GroupChar(
        string linearText,
        string baseText,
        string groupCharacter,
        string groupCharacterPosition,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? basePlan = null) =>
        new(
            EquationVisualElementKind.GroupChar,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            GroupCharacter = groupCharacter,
            GroupCharacterPosition = groupCharacterPosition,
            GroupCharBasePlan = basePlan
        };

    public static EquationVisualElement FunctionApply(
        string linearText,
        string functionName,
        string argument,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? argumentPlan = null) =>
        new(
            EquationVisualElementKind.FunctionApply,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            FunctionName = functionName,
            FunctionArgument = argument,
            FunctionArgumentPlan = argumentPlan
        };
}

public sealed record EquationVisualPlan(
    string LinearText,
    string MathFontFamily,
    bool Italic,
    IReadOnlyList<EquationVisualSegment> Segments,
    IReadOnlyList<EquationVisualElement> Elements);

public sealed record FreeWVisualEquationExpectation(
    int EquationCount,
    int ElementCount,
    int SegmentCount,
    int NestedSlotCount,
    int MaxNestedSlotDepth,
    IReadOnlyList<string> ElementKindCounts,
    IReadOnlyList<string> SegmentRoleCounts,
    IReadOnlyList<string> BaselineRoleCounts,
    IReadOnlyList<string> SegmentGeometrySignatures,
    IReadOnlyList<string> ElementGeometrySignatures,
    IReadOnlyList<string> SpacingGeometrySignatures,
    IReadOnlyList<string> SlotGeometrySignatures)
{
    public static FreeWVisualEquationExpectation Empty { get; } = new(
        EquationCount: 0,
        ElementCount: 0,
        SegmentCount: 0,
        NestedSlotCount: 0,
        MaxNestedSlotDepth: 0,
        ElementKindCounts: [],
        SegmentRoleCounts: [],
        BaselineRoleCounts: [],
        SegmentGeometrySignatures: [],
        ElementGeometrySignatures: [],
        SpacingGeometrySignatures: [],
        SlotGeometrySignatures: []);
}

public static class EquationVisualPlanner
{
    public const string DefaultMathFontFamily = "Cambria Math, Cambria, Times New Roman, serif";
    public const double ScriptFontSizeScale = 0.65;
    public const double StructureFontSizeScale = 0.9;
    public const double SuperscriptBaselineOffsetEm = 0.25;
    public const double SubscriptBaselineOffsetEm = -0.18;
    public const double LargeOperatorFontSizeScale = 1.32;
    public const double DecoratorFontSizeScale = 0.85;
    public const double DelimiterFontSizeScale = 1.25;
    public const double ScriptHorizontalGapEm = 0.06;
    public const double FractionStackGapEm = 0.12;
    public const double FractionBarThicknessEm = 0.05;
    public const double FractionBarOverhangEm = 0.08;
    public const double RadicalDegreeGapEm = 0.08;
    public const double RadicalRadicandGapEm = 0.1;
    public const double RadicalOverbarClearanceEm = 0.06;
    public const double NAryLimitGapEm = 0.08;
    public const double NAryOperandGapEm = 0.16;
    public const double MatrixColumnGapEm = 0.85;
    public const double MatrixRowGapEm = 0.08;
    public const double MatrixDelimiterGapEm = 0.12;
    public const string FractionBarText = "\u2044";
    public const string RadicalSignText = "\u221a";
    public const string MatrixOpenDelimiterText = "[";
    public const string MatrixCloseDelimiterText = "]";
    public const string MatrixColumnSeparatorText = "  ";
    public const string MatrixRowSeparatorText = "; ";
    public const string OverbarCueText = "\u00af";
    public const string UnderbarCueText = "_";

    private static EquationVisualStyle NormalStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        FontSizeScale: 1.0,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle SuperscriptStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        ScriptFontSizeScale,
        EquationVisualBaselineRole.Superscript,
        SuperscriptBaselineOffsetEm);

    private static EquationVisualStyle SubscriptStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        ScriptFontSizeScale,
        EquationVisualBaselineRole.Subscript,
        SubscriptBaselineOffsetEm);

    private static EquationVisualStyle StructureStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle LargeOperatorStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        LargeOperatorFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle MatrixDelimiterStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        FontSizeScale: 1.0,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle MatrixSeparatorStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle DecoratorStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        DecoratorFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle DelimiterStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        DelimiterFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle FunctionNameStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle FunctionDelimiterStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    public static EquationVisualPlan Build(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);

        return Build(equation, depth: 0);
    }

    public static FreeWVisualEquationExpectation BuildEvidence(TextDocument? document)
    {
        if (document is null)
            return FreeWVisualEquationExpectation.Empty;

        var equations = EnumerateEquations(document).ToList();
        return BuildEvidence(equations);
    }

    public static FreeWVisualEquationExpectation BuildEvidence(IReadOnlyList<Equation> equations)
    {
        ArgumentNullException.ThrowIfNull(equations);

        if (equations.Count == 0)
            return FreeWVisualEquationExpectation.Empty;

        var plans = equations.Select(equation => Build(equation)).ToList();
        var allElements = plans.SelectMany(EnumerateElements).ToList();
        var allSegments = plans.SelectMany(EnumerateSegments).ToList();
        var slotPlans = new List<(int EquationIndex, string OwnerPath, string SlotName, int Depth, EquationVisualPlan Plan)>();
        for (var equationIndex = 0; equationIndex < plans.Count; equationIndex++)
        {
            CollectSlotPlans(
                plans[equationIndex],
                equationIndex + 1,
                ownerPath: "eq=" + (equationIndex + 1).ToString(CultureInfo.InvariantCulture),
                depth: 0,
                slotPlans);
        }

        return new FreeWVisualEquationExpectation(
            EquationCount: plans.Count,
            ElementCount: allElements.Count,
            SegmentCount: allSegments.Count,
            NestedSlotCount: slotPlans.Count,
            MaxNestedSlotDepth: slotPlans.Count == 0 ? 0 : slotPlans.Max(slot => slot.Depth),
            ElementKindCounts: BuildCountSignatures(allElements.Select(element => element.Kind.ToString())),
            SegmentRoleCounts: BuildCountSignatures(allSegments.Select(segment => segment.Role.ToString())),
            BaselineRoleCounts: BuildCountSignatures(allSegments.Select(segment => segment.Style.BaselineRole.ToString())),
            SegmentGeometrySignatures: plans
                .SelectMany((plan, equationIndex) => BuildSegmentGeometrySignatures(plan, equationIndex + 1))
                .ToList(),
            ElementGeometrySignatures: plans
                .SelectMany((plan, equationIndex) => BuildElementGeometrySignatures(plan, equationIndex + 1))
                .ToList(),
            SpacingGeometrySignatures: plans
                .SelectMany((plan, equationIndex) => BuildSpacingGeometrySignatures(plan, equationIndex + 1))
                .ToList(),
            SlotGeometrySignatures: slotPlans
                .Select(slot => BuildSlotGeometrySignature(slot.EquationIndex, slot.OwnerPath, slot.SlotName, slot.Depth, slot.Plan))
                .OrderBy(signature => signature, StringComparer.Ordinal)
                .ToList());
    }

    private static EquationVisualPlan Build(Equation equation, int depth)
    {
        var segments = new List<EquationVisualSegment>();
        var elements = new List<EquationVisualElement>();
        foreach (var run in equation.Runs)
            AddRunVisual(run, segments, elements, depth);

        if (segments.Count == 0 && equation.LinearText.Length > 0)
        {
            var fallback = new EquationVisualSegment(
                equation.LinearText,
                EquationVisualSegmentRole.LinearFallback,
                NormalStyle);
            segments.Add(fallback);
            elements.Add(EquationVisualElement.FromSegments(equation.LinearText, [fallback]));
        }

        return new EquationVisualPlan(
            equation.LinearText,
            DefaultMathFontFamily,
            Italic: true,
            Segments: segments,
            Elements: elements);
    }

    private static void AddRunVisual(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        switch (run.Kind)
        {
            case MathRunKind.Text:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Text, EquationVisualSegmentRole.Text, NormalStyle));
                break;

            case MathRunKind.Superscript:
                AddScriptElement(run, segments, elements, depth, includeSubscript: false, includeSuperscript: true);
                break;

            case MathRunKind.Subscript:
                AddScriptElement(run, segments, elements, depth, includeSubscript: true, includeSuperscript: false);
                break;

            case MathRunKind.SubSuperscript:
                AddScriptElement(run, segments, elements, depth, includeSubscript: true, includeSuperscript: true);
                break;

            case MathRunKind.Fraction:
                AddFractionElement(run, segments, elements, depth);
                break;

            case MathRunKind.Radical:
                AddRadicalElement(run, segments, elements, depth);
                break;

            case MathRunKind.NAry:
                AddNAryElement(run, segments, elements, depth);
                break;

            case MathRunKind.Matrix:
                AddMatrixElement(run, segments, elements, depth);
                break;

            case MathRunKind.EquationArray:
                AddEquationArrayElement(run, segments, elements, depth);
                break;

            case MathRunKind.Accent:
                AddAccentElement(run, segments, elements, depth);
                break;

            case MathRunKind.Bar:
                AddBarElement(run, segments, elements, depth);
                break;

            case MathRunKind.Delimiter:
                AddDelimiterElement(run, segments, elements, depth);
                break;

            case MathRunKind.GroupChar:
                AddGroupCharElement(run, segments, elements, depth);
                break;

            case MathRunKind.FunctionApply:
                AddFunctionApplyElement(run, segments, elements, depth);
                break;

            default:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle));
                break;
        }
    }

    private static void AddSegmentElement(
        string linearText,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        params EquationVisualSegment?[] candidates)
    {
        var runSegments = candidates.Where(segment => segment is not null).Cast<EquationVisualSegment>().ToList();
        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.FromSegments(linearText, runSegments));
    }

    private static void AddScriptElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth,
        bool includeSubscript,
        bool includeSuperscript)
    {
        var basePlan = BuildSlotPlan(run.ScriptBaseEquation, depth);
        var subscriptPlan = includeSubscript ? BuildSlotPlan(run.ScriptSubEquation, depth) : null;
        var superscriptPlan = includeSuperscript ? BuildSlotPlan(run.ScriptSupEquation, depth) : null;
        var baseText = basePlan?.LinearText ?? run.Base;
        var subscriptText = subscriptPlan?.LinearText ?? run.Sub;
        var superscriptText = superscriptPlan?.LinearText ?? run.Sup;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, baseText, EquationVisualSegmentRole.Base, NormalStyle);
        if (includeSubscript)
            AddIfAny(runSegments, subscriptText, EquationVisualSegmentRole.Subscript, SubscriptStyle);
        if (includeSuperscript)
            AddIfAny(runSegments, superscriptText, EquationVisualSegmentRole.Superscript, SuperscriptStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Script(
            run.LinearText,
            baseText,
            includeSubscript ? subscriptText : string.Empty,
            includeSuperscript ? superscriptText : string.Empty,
            runSegments,
            basePlan,
            subscriptPlan,
            superscriptPlan));
    }

    private static void AddFractionElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var numeratorPlan = BuildSlotPlan(run.NumeratorEquation, depth);
        var denominatorPlan = BuildSlotPlan(run.DenominatorEquation, depth);
        var numeratorText = numeratorPlan?.LinearText ?? run.Numerator;
        var denominatorText = denominatorPlan?.LinearText ?? run.Denominator;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, numeratorText, EquationVisualSegmentRole.FractionNumerator, StructureStyle);
        AddIfAny(runSegments, FractionBarText, EquationVisualSegmentRole.FractionBar, NormalStyle);
        AddIfAny(runSegments, denominatorText, EquationVisualSegmentRole.FractionDenominator, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Fraction(
            run.LinearText,
            numeratorText,
            denominatorText,
            runSegments,
            numeratorPlan,
            denominatorPlan));
    }

    private static EquationVisualPlan? BuildSlotPlan(Equation? equation, int depth) =>
        equation is null || depth >= MathRun.MaxNestedEquationDepth
            ? null
            : Build(equation, depth + 1);

    private static void AddRadicalElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var radicandPlan = BuildSlotPlan(run.RadicandEquation, depth);
        var degreePlan = BuildSlotPlan(run.DegreeEquation, depth);
        var radicandText = radicandPlan?.LinearText ?? run.Base;
        var degreeText = degreePlan?.LinearText ?? run.Degree;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, degreeText, EquationVisualSegmentRole.RadicalDegree, SuperscriptStyle);
        AddIfAny(runSegments, RadicalSignText, EquationVisualSegmentRole.RadicalSign, NormalStyle);
        AddIfAny(runSegments, radicandText, EquationVisualSegmentRole.RadicalRadicand, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Radical(
            run.LinearText,
            radicandText,
            degreeText,
            runSegments,
            radicandPlan,
            degreePlan));
    }

    private static void AddNAryElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var lowerLimitPlan = BuildSlotPlan(run.NAryLowerLimitEquation, depth);
        var upperLimitPlan = BuildSlotPlan(run.NAryUpperLimitEquation, depth);
        var operandPlan = BuildSlotPlan(run.NAryOperandEquation, depth);
        var lowerLimitText = lowerLimitPlan?.LinearText ?? run.Sub;
        var upperLimitText = upperLimitPlan?.LinearText ?? run.Sup;
        var operandText = operandPlan?.LinearText ?? run.Base;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.Operator, EquationVisualSegmentRole.NAryOperator, LargeOperatorStyle);
        AddIfAny(runSegments, lowerLimitText, EquationVisualSegmentRole.NAryLowerLimit, SubscriptStyle);
        AddIfAny(runSegments, upperLimitText, EquationVisualSegmentRole.NAryUpperLimit, SuperscriptStyle);
        AddIfAny(runSegments, operandText, EquationVisualSegmentRole.NAryOperand, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.NAry(
            run.LinearText,
            run.Operator,
            lowerLimitText,
            upperLimitText,
            operandText,
            runSegments,
            lowerLimitPlan,
            upperLimitPlan,
            operandPlan));
    }

    private static void AddMatrixElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        if (run.Matrix is null)
        {
            AddSegmentElement(
                run.LinearText,
                segments,
                elements,
                Segment(run.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle));
            return;
        }

        var matrixRows = BuildMatrixRows(run.Matrix, depth);
        var runSegments = new List<EquationVisualSegment>
        {
            new(MatrixOpenDelimiterText, EquationVisualSegmentRole.MatrixOpenDelimiter, MatrixDelimiterStyle)
        };

        for (var rowIndex = 0; rowIndex < matrixRows.Count; rowIndex++)
        {
            if (rowIndex > 0)
                runSegments.Add(new EquationVisualSegment(
                    MatrixRowSeparatorText,
                    EquationVisualSegmentRole.MatrixRowSeparator,
                    MatrixSeparatorStyle));

            var row = matrixRows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                if (columnIndex > 0)
                    runSegments.Add(new EquationVisualSegment(
                        MatrixColumnSeparatorText,
                        EquationVisualSegmentRole.MatrixColumnSeparator,
                        MatrixSeparatorStyle));

                var cell = row.Cells[columnIndex];
                runSegments.Add(new EquationVisualSegment(
                    cell.Text,
                    EquationVisualSegmentRole.MatrixCell,
                    StructureStyle));
            }
        }

        runSegments.Add(new EquationVisualSegment(
            MatrixCloseDelimiterText,
            EquationVisualSegmentRole.MatrixCloseDelimiter,
            MatrixDelimiterStyle));

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Matrix(run.LinearText, matrixRows, runSegments));
    }

    private static IReadOnlyList<EquationVisualMatrixRow> BuildMatrixRows(MathMatrix matrix, int depth)
    {
        var columnCount = matrix.ColumnCount;
        var rows = new List<EquationVisualMatrixRow>();

        for (var rowIndex = 0; rowIndex < matrix.RowCount; rowIndex++)
        {
            var cells = new List<EquationVisualMatrixCell>();
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var cellPlan = BuildSlotPlan(matrix.CellEquationAt(rowIndex, columnIndex), depth);
                var text = cellPlan?.LinearText ?? matrix.CellTextAt(rowIndex, columnIndex);
                cells.Add(new EquationVisualMatrixCell(rowIndex, columnIndex, text, cellPlan));
            }

            rows.Add(new EquationVisualMatrixRow(rowIndex, cells));
        }

        return rows;
    }

    private static void AddEquationArrayElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        if (run.Matrix is null)
        {
            AddSegmentElement(
                run.LinearText,
                segments,
                elements,
                Segment(run.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle));
            return;
        }

        var rows = BuildMatrixRows(run.Matrix, depth);
        var runSegments = new List<EquationVisualSegment>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rowIndex > 0)
                runSegments.Add(new EquationVisualSegment(
                    MatrixRowSeparatorText,
                    EquationVisualSegmentRole.MatrixRowSeparator,
                    MatrixSeparatorStyle));

            var row = rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                if (columnIndex > 0)
                    runSegments.Add(new EquationVisualSegment(
                        MatrixColumnSeparatorText,
                        EquationVisualSegmentRole.MatrixColumnSeparator,
                        MatrixSeparatorStyle));

                var cell = row.Cells[columnIndex];
                runSegments.Add(new EquationVisualSegment(
                    cell.Text,
                    EquationVisualSegmentRole.MatrixCell,
                    StructureStyle));
            }
        }

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.EquationArray(run.LinearText, rows, runSegments));
    }

    private static void AddAccentElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var basePlan = BuildSlotPlan(run.DecoratorBaseEquation, depth);
        var baseText = basePlan?.LinearText ?? run.Base;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, AccentCueText(run.Accent), EquationVisualSegmentRole.AccentMark, DecoratorStyle);
        AddIfAny(runSegments, baseText, EquationVisualSegmentRole.AccentBase, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.AccentElement(
            run.LinearText,
            baseText,
            run.Accent,
            runSegments,
            basePlan));
    }

    private static void AddBarElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var basePlan = BuildSlotPlan(run.DecoratorBaseEquation, depth);
        var baseText = basePlan?.LinearText ?? run.Base;

        var runSegments = new List<EquationVisualSegment>();
        var markText = run.BarTop ? OverbarCueText : UnderbarCueText;
        if (run.BarTop)
        {
            AddIfAny(runSegments, markText, EquationVisualSegmentRole.BarMark, DecoratorStyle);
            AddIfAny(runSegments, baseText, EquationVisualSegmentRole.BarBase, StructureStyle);
        }
        else
        {
            AddIfAny(runSegments, baseText, EquationVisualSegmentRole.BarBase, StructureStyle);
            AddIfAny(runSegments, markText, EquationVisualSegmentRole.BarMark, DecoratorStyle);
        }

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Bar(
            run.LinearText,
            baseText,
            run.BarTop,
            runSegments,
            basePlan));
    }

    private static void AddDelimiterElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var delimiterContentPlan = BuildSlotPlan(run.DelimiterContentEquation, depth);
        var contentText = delimiterContentPlan?.LinearText ?? run.Base;

        // Multi-argument delimiter (binomial/case/matrix-style m:d with more than one m:e): argument 0
        // lives in Base/DelimiterContentEquation above, the rest are carried in AdditionalDelimiterArguments/
        // AdditionalDelimiterContentEquations. Plan every one of them so none are silently truncated on
        // screen even though the model has always round-tripped all of them.
        var additionalTexts = new List<string>(run.AdditionalDelimiterArguments.Count);
        var additionalPlans = new List<EquationVisualPlan?>(run.AdditionalDelimiterArguments.Count);
        for (var index = 0; index < run.AdditionalDelimiterArguments.Count; index++)
        {
            var argumentEquation = index < run.AdditionalDelimiterContentEquations.Count
                ? run.AdditionalDelimiterContentEquations[index]
                : null;
            var argumentPlan = BuildSlotPlan(argumentEquation, depth);
            additionalTexts.Add(argumentPlan?.LinearText ?? run.AdditionalDelimiterArguments[index]);
            additionalPlans.Add(argumentPlan);
        }

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.OpenChar, EquationVisualSegmentRole.DelimiterOpen, DelimiterStyle);
        AddIfAny(runSegments, contentText, EquationVisualSegmentRole.DelimiterContent, StructureStyle);
        for (var index = 0; index < additionalTexts.Count; index++)
        {
            AddIfAny(runSegments, run.DelimiterSeparator, EquationVisualSegmentRole.DelimiterSeparator, DelimiterStyle);
            AddIfAny(runSegments, additionalTexts[index], EquationVisualSegmentRole.DelimiterContent, StructureStyle);
        }
        AddIfAny(runSegments, run.CloseChar, EquationVisualSegmentRole.DelimiterClose, DelimiterStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Delimiter(
            run.LinearText,
            contentText,
            run.OpenChar,
            run.CloseChar,
            runSegments,
            delimiterContentPlan,
            additionalTexts,
            additionalPlans,
            run.DelimiterSeparator));
    }

    private static void AddGroupCharElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var basePlan = BuildSlotPlan(run.DecoratorBaseEquation, depth);
        var baseText = basePlan?.LinearText ?? run.Base;
        var groupOnTop = !string.Equals(run.GroupChrPos, "bot", StringComparison.OrdinalIgnoreCase);
        var runSegments = new List<EquationVisualSegment>();
        if (groupOnTop)
        {
            AddIfAny(runSegments, run.GroupChr, EquationVisualSegmentRole.GroupCharMark, DecoratorStyle);
            AddIfAny(runSegments, baseText, EquationVisualSegmentRole.GroupCharBase, StructureStyle);
        }
        else
        {
            AddIfAny(runSegments, baseText, EquationVisualSegmentRole.GroupCharBase, StructureStyle);
            AddIfAny(runSegments, run.GroupChr, EquationVisualSegmentRole.GroupCharMark, DecoratorStyle);
        }

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.GroupChar(
            run.LinearText,
            baseText,
            run.GroupChr,
            run.GroupChrPos,
            runSegments,
            basePlan));
    }

    private static void AddFunctionApplyElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var argumentPlan = BuildSlotPlan(run.FunctionArgumentEquation, depth);
        var argumentText = argumentPlan?.LinearText ?? run.Base;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.FuncName, EquationVisualSegmentRole.FunctionName, FunctionNameStyle);
        AddIfAny(runSegments, argumentText, EquationVisualSegmentRole.FunctionArgument, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.FunctionApply(
            run.LinearText,
            run.FuncName,
            argumentText,
            runSegments,
            argumentPlan));
    }

    private static string AccentCueText(string accent)
    {
        if (string.IsNullOrEmpty(accent))
            return "^";

        return accent switch
        {
            "\u0302" => "^",
            "\u0303" => "~",
            "\u0304" => OverbarCueText,
            "\u0307" => ".",
            "\u0308" => "..",
            "\u20d7" => "\u2192",
            _ => accent
        };
    }

    private static EquationVisualSegment? Segment(
        string? text,
        EquationVisualSegmentRole role,
        EquationVisualStyle style)
    {
        return string.IsNullOrEmpty(text)
            ? null
            : new EquationVisualSegment(text, role, style);
    }

    private static void AddIfAny(
        List<EquationVisualSegment> segments,
        string? text,
        EquationVisualSegmentRole role,
        EquationVisualStyle style)
    {
        if (!string.IsNullOrEmpty(text))
            segments.Add(new EquationVisualSegment(text, role, style));
    }

    private static IEnumerable<Equation> EnumerateEquations(TextDocument document)
    {
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.Equation is not null)
                    yield return run.Equation;
            }
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
                continue;
            }

            if (block is Table table)
            {
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
            }
        }
    }

    private static IEnumerable<EquationVisualElement> EnumerateElements(EquationVisualPlan plan)
    {
        foreach (var element in plan.Elements)
        {
            yield return element;

            foreach (var slot in SlotPlans(element))
                foreach (var child in EnumerateElements(slot.Plan))
                    yield return child;
        }
    }

    private static IEnumerable<EquationVisualSegment> EnumerateSegments(EquationVisualPlan plan)
    {
        foreach (var segment in plan.Segments)
            yield return segment;

        foreach (var element in plan.Elements)
            foreach (var slot in SlotPlans(element))
                foreach (var segment in EnumerateSegments(slot.Plan))
                    yield return segment;
    }

    private static void CollectSlotPlans(
        EquationVisualPlan plan,
        int equationIndex,
        string ownerPath,
        int depth,
        List<(int EquationIndex, string OwnerPath, string SlotName, int Depth, EquationVisualPlan Plan)> slots)
    {
        for (var elementIndex = 0; elementIndex < plan.Elements.Count; elementIndex++)
        {
            var element = plan.Elements[elementIndex];
            var elementPath = ownerPath + "|el=" + (elementIndex + 1).ToString(CultureInfo.InvariantCulture);
            foreach (var slot in SlotPlans(element))
            {
                var slotDepth = depth + 1;
                slots.Add((equationIndex, elementPath, slot.Name, slotDepth, slot.Plan));
                CollectSlotPlans(slot.Plan, equationIndex, elementPath + "|slot=" + slot.Name, slotDepth, slots);
            }
        }
    }

    private static IEnumerable<(string Name, EquationVisualPlan Plan)> SlotPlans(EquationVisualElement element)
    {
        if (element.ScriptBasePlan is not null)
            yield return ("script-base", element.ScriptBasePlan);
        if (element.ScriptSubscriptPlan is not null)
            yield return ("script-subscript", element.ScriptSubscriptPlan);
        if (element.ScriptSuperscriptPlan is not null)
            yield return ("script-superscript", element.ScriptSuperscriptPlan);
        if (element.NumeratorPlan is not null)
            yield return ("fraction-numerator", element.NumeratorPlan);
        if (element.DenominatorPlan is not null)
            yield return ("fraction-denominator", element.DenominatorPlan);
        if (element.RadicandPlan is not null)
            yield return ("radical-radicand", element.RadicandPlan);
        if (element.DegreePlan is not null)
            yield return ("radical-degree", element.DegreePlan);
        if (element.NAryLowerLimitPlan is not null)
            yield return ("nary-lower-limit", element.NAryLowerLimitPlan);
        if (element.NAryUpperLimitPlan is not null)
            yield return ("nary-upper-limit", element.NAryUpperLimitPlan);
        if (element.NAryOperandPlan is not null)
            yield return ("nary-operand", element.NAryOperandPlan);
        if (element.DelimiterContentPlan is not null)
            yield return ("delimiter-content", element.DelimiterContentPlan);
        for (var index = 0; index < element.AdditionalDelimiterArgumentPlans.Count; index++)
            if (element.AdditionalDelimiterArgumentPlans[index] is { } additionalPlan)
                yield return ("delimiter-content-" + (index + 2).ToString(CultureInfo.InvariantCulture), additionalPlan);
        if (element.FunctionArgumentPlan is not null)
            yield return ("function-argument", element.FunctionArgumentPlan);
        if (element.AccentBasePlan is not null)
            yield return ("accent-base", element.AccentBasePlan);
        if (element.BarBasePlan is not null)
            yield return ("bar-base", element.BarBasePlan);
        if (element.GroupCharBasePlan is not null)
            yield return ("groupchar-base", element.GroupCharBasePlan);

        foreach (var row in element.MatrixRows)
            foreach (var cell in row.Cells)
                if (cell.CellPlan is not null)
                    yield return (
                        "matrix-cell-r" + row.RowIndex.ToString(CultureInfo.InvariantCulture)
                            + "c" + cell.ColumnIndex.ToString(CultureInfo.InvariantCulture),
                        cell.CellPlan);
    }

    private static IReadOnlyList<string> BuildCountSignatures(IEnumerable<string> values) =>
        values
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key + "=" + group.Count().ToString(CultureInfo.InvariantCulture))
            .ToList();

    private static IEnumerable<string> BuildSegmentGeometrySignatures(
        EquationVisualPlan plan,
        int equationIndex)
    {
        for (var segmentIndex = 0; segmentIndex < plan.Segments.Count; segmentIndex++)
        {
            var segment = plan.Segments[segmentIndex];
            yield return string.Join(
                "|",
                EqPart(equationIndex),
                "seg=" + (segmentIndex + 1).ToString(CultureInfo.InvariantCulture),
                "role=" + segment.Role,
                "baseline=" + segment.Style.BaselineRole,
                "offsetEm=" + FormatDouble(segment.Style.BaselineOffsetEm),
                "scale=" + FormatDouble(segment.Style.FontSizeScale),
                "italic=" + BoolFlag(segment.Style.Italic),
                "text=" + NormalizeSignatureText(segment.Text));
        }
    }

    private static IEnumerable<string> BuildElementGeometrySignatures(
        EquationVisualPlan plan,
        int equationIndex)
    {
        for (var elementIndex = 0; elementIndex < plan.Elements.Count; elementIndex++)
        {
            var element = plan.Elements[elementIndex];
            yield return string.Join(
                "|",
                EqPart(equationIndex),
                "el=" + (elementIndex + 1).ToString(CultureInfo.InvariantCulture),
                "kind=" + element.Kind,
                "roles=" + JoinRoles(element.Segments),
                "baselines=" + JoinBaselines(element.Segments),
                "scales=" + JoinScales(element.Segments),
                BuildElementGeometryPart(element));
        }
    }

    private static string BuildSlotGeometrySignature(
        int equationIndex,
        string ownerPath,
        string slotName,
        int depth,
        EquationVisualPlan plan) =>
        string.Join(
            "|",
            EqPart(equationIndex),
            ownerPath,
            "slot=" + slotName,
            "depth=" + depth.ToString(CultureInfo.InvariantCulture),
            "text=" + NormalizeSignatureText(plan.LinearText),
            "segments=" + plan.Segments.Count.ToString(CultureInfo.InvariantCulture),
            "elements=" + plan.Elements.Count.ToString(CultureInfo.InvariantCulture),
            "roles=" + JoinRoles(plan.Segments),
            "baselines=" + JoinBaselines(plan.Segments));

    private static string BuildElementGeometryPart(EquationVisualElement element)
    {
        return element.Kind switch
        {
            EquationVisualElementKind.Segments when !string.IsNullOrEmpty(element.ScriptSubscriptText)
                || !string.IsNullOrEmpty(element.ScriptSuperscriptText) =>
                string.Join(
                    "|",
                    "geometry=script",
                    "base=" + NormalizeSignatureText(element.BaseText),
                    "subscript=" + NormalizeSignatureText(element.ScriptSubscriptText),
                    "superscript=" + NormalizeSignatureText(element.ScriptSuperscriptText),
                    "subOffsetEm=" + FormatDouble(SubscriptBaselineOffsetEm),
                    "supOffsetEm=" + FormatDouble(SuperscriptBaselineOffsetEm),
                    "scriptScale=" + FormatDouble(ScriptFontSizeScale)),
            EquationVisualElementKind.Fraction =>
                string.Join(
                    "|",
                    "geometry=fraction",
                    "numerator=" + NormalizeSignatureText(element.Numerator),
                    "bar=" + NormalizeSignatureText(FractionBarText),
                    "denominator=" + NormalizeSignatureText(element.Denominator),
                    "slotOrder=numerator,bar,denominator",
                    "stackGapEm=" + FormatDouble(FractionStackGapEm),
                    "barThicknessEm=" + FormatDouble(FractionBarThicknessEm)),
            EquationVisualElementKind.Radical =>
                string.Join(
                    "|",
                    "geometry=radical",
                    "degree=" + NormalizeSignatureText(element.Degree),
                    "sign=" + NormalizeSignatureText(RadicalSignText),
                    "radicand=" + NormalizeSignatureText(element.Radicand),
                    "degreeOffsetEm=" + FormatDouble(SuperscriptBaselineOffsetEm),
                    "radicandScale=" + FormatDouble(StructureFontSizeScale)),
            EquationVisualElementKind.NAry =>
                string.Join(
                    "|",
                    "geometry=nary",
                    "operator=" + NormalizeSignatureText(element.Operator),
                    "lower=" + NormalizeSignatureText(element.LowerLimit),
                    "upper=" + NormalizeSignatureText(element.UpperLimit),
                    "operand=" + NormalizeSignatureText(element.Operand),
                    "operatorScale=" + FormatDouble(LargeOperatorFontSizeScale),
                    "operandScale=" + FormatDouble(StructureFontSizeScale)),
            EquationVisualElementKind.Matrix or EquationVisualElementKind.EquationArray =>
                string.Join(
                    "|",
                    "geometry=" + element.Kind.ToString().ToLowerInvariant(),
                    "rows=" + element.MatrixRowCount.ToString(CultureInfo.InvariantCulture),
                    "columns=" + element.MatrixColumnCount.ToString(CultureInfo.InvariantCulture),
                    "cells=" + element.MatrixRows.Sum(row => row.Cells.Count).ToString(CultureInfo.InvariantCulture),
                    "cellTexts=" + JoinCellTexts(element),
                    "columnGapText=" + NormalizeSignatureText(MatrixColumnSeparatorText),
                    "rowGapText=" + NormalizeSignatureText(MatrixRowSeparatorText),
                    "openDelimiter=" + NormalizeSignatureText(element.Kind == EquationVisualElementKind.Matrix ? MatrixOpenDelimiterText : string.Empty),
                    "closeDelimiter=" + NormalizeSignatureText(element.Kind == EquationVisualElementKind.Matrix ? MatrixCloseDelimiterText : string.Empty)),
            EquationVisualElementKind.Accent =>
                string.Join(
                    "|",
                    "geometry=accent",
                    "mark=" + NormalizeSignatureText(AccentCueText(element.Accent)),
                    "base=" + NormalizeSignatureText(element.BaseText),
                    "markScale=" + FormatDouble(DecoratorFontSizeScale),
                    "markPosition=top"),
            EquationVisualElementKind.Bar =>
                string.Join(
                    "|",
                    "geometry=bar",
                    "mark=" + NormalizeSignatureText(element.BarTop ? OverbarCueText : UnderbarCueText),
                    "base=" + NormalizeSignatureText(element.BaseText),
                    "markScale=" + FormatDouble(DecoratorFontSizeScale),
                    "markPosition=" + (element.BarTop ? "top" : "bottom")),
            EquationVisualElementKind.Delimiter =>
                string.Join(
                    "|",
                    "geometry=delimiter",
                    "open=" + NormalizeSignatureText(element.OpenDelimiter),
                    "content=" + NormalizeSignatureText(element.BaseText),
                    "close=" + NormalizeSignatureText(element.CloseDelimiter),
                    "delimiterScale=" + FormatDouble(DelimiterFontSizeScale)),
            EquationVisualElementKind.GroupChar =>
                string.Join(
                    "|",
                    "geometry=groupchar",
                    "mark=" + NormalizeSignatureText(element.GroupCharacter),
                    "base=" + NormalizeSignatureText(element.BaseText),
                    "markPosition=" + (element.GroupCharacterTop ? "top" : "bottom"),
                    "markScale=" + FormatDouble(DecoratorFontSizeScale)),
            EquationVisualElementKind.FunctionApply =>
                string.Join(
                    "|",
                    "geometry=function-apply",
                    "name=" + NormalizeSignatureText(element.FunctionName),
                    "argument=" + NormalizeSignatureText(element.FunctionArgument),
                    "form=omml-name-argument",
                    "functionScale=" + FormatDouble(StructureFontSizeScale)),
            _ => "geometry=segments|text=" + NormalizeSignatureText(element.LinearText)
        };
    }

    private static IEnumerable<string> BuildSpacingGeometrySignatures(
        EquationVisualPlan plan,
        int equationIndex)
    {
        for (var elementIndex = 0; elementIndex < plan.Elements.Count; elementIndex++)
        {
            var element = plan.Elements[elementIndex];
            var spacing = BuildSpacingGeometryPart(element);
            if (spacing is null)
                continue;

            yield return string.Join(
                "|",
                EqPart(equationIndex),
                "el=" + (elementIndex + 1).ToString(CultureInfo.InvariantCulture),
                "kind=" + element.Kind,
                spacing);
        }
    }

    private static string? BuildSpacingGeometryPart(EquationVisualElement element)
    {
        return element.Kind switch
        {
            EquationVisualElementKind.Segments when !string.IsNullOrEmpty(element.ScriptSubscriptText)
                || !string.IsNullOrEmpty(element.ScriptSuperscriptText) =>
                string.Join(
                    "|",
                    "spacing=script",
                    "hasSubscript=" + BoolFlag(!string.IsNullOrEmpty(element.ScriptSubscriptText)),
                    "hasSuperscript=" + BoolFlag(!string.IsNullOrEmpty(element.ScriptSuperscriptText)),
                    "horizontalGapEm=" + FormatDouble(ScriptHorizontalGapEm),
                    "subOffsetEm=" + FormatDouble(SubscriptBaselineOffsetEm),
                    "supOffsetEm=" + FormatDouble(SuperscriptBaselineOffsetEm),
                    "scriptScale=" + FormatDouble(ScriptFontSizeScale)),
            EquationVisualElementKind.Fraction =>
                string.Join(
                    "|",
                    "spacing=fraction",
                    "layout=vertical-stack",
                    "numeratorAlign=center",
                    "denominatorAlign=center",
                    "stackGapEm=" + FormatDouble(FractionStackGapEm),
                    "barThicknessEm=" + FormatDouble(FractionBarThicknessEm),
                    "barOverhangEm=" + FormatDouble(FractionBarOverhangEm),
                    "numeratorSegments=" + SegmentCount(element.NumeratorPlan).ToString(CultureInfo.InvariantCulture),
                    "denominatorSegments=" + SegmentCount(element.DenominatorPlan).ToString(CultureInfo.InvariantCulture)),
            EquationVisualElementKind.Radical =>
                string.Join(
                    "|",
                    "spacing=radical",
                    "degreeGapEm=" + FormatDouble(RadicalDegreeGapEm),
                    "radicandGapEm=" + FormatDouble(RadicalRadicandGapEm),
                    "overbarClearanceEm=" + FormatDouble(RadicalOverbarClearanceEm),
                    "degreeOffsetEm=" + FormatDouble(SuperscriptBaselineOffsetEm),
                    "degreePresent=" + BoolFlag(!string.IsNullOrEmpty(element.Degree)),
                    "radicandSegments=" + SegmentCount(element.RadicandPlan).ToString(CultureInfo.InvariantCulture)),
            EquationVisualElementKind.NAry =>
                string.Join(
                    "|",
                    "spacing=nary",
                    "limitPlacement=above-below",
                    "lowerGapEm=" + FormatDouble(NAryLimitGapEm),
                    "upperGapEm=" + FormatDouble(NAryLimitGapEm),
                    "operandGapEm=" + FormatDouble(NAryOperandGapEm),
                    "operatorScale=" + FormatDouble(LargeOperatorFontSizeScale),
                    "limitScale=" + FormatDouble(ScriptFontSizeScale),
                    "operandScale=" + FormatDouble(StructureFontSizeScale),
                    "hasLower=" + BoolFlag(!string.IsNullOrEmpty(element.LowerLimit)),
                    "hasUpper=" + BoolFlag(!string.IsNullOrEmpty(element.UpperLimit))),
            EquationVisualElementKind.Matrix or EquationVisualElementKind.EquationArray =>
                string.Join(
                    "|",
                    "spacing=" + element.Kind.ToString().ToLowerInvariant(),
                    "rowGapEm=" + FormatDouble(MatrixRowGapEm),
                    "columnGapEm=" + FormatDouble(MatrixColumnGapEm),
                    "delimiterGapEm=" + FormatDouble(
                        element.Kind == EquationVisualElementKind.Matrix ? MatrixDelimiterGapEm : 0.0),
                    "rows=" + element.MatrixRowCount.ToString(CultureInfo.InvariantCulture),
                    "columns=" + element.MatrixColumnCount.ToString(CultureInfo.InvariantCulture)),
            _ => null
        };
    }

    private static string JoinRoles(IReadOnlyList<EquationVisualSegment> segments) =>
        string.Join(",", segments.Select(segment => segment.Role.ToString()));

    private static string JoinBaselines(IReadOnlyList<EquationVisualSegment> segments) =>
        string.Join(",", segments.Select(segment =>
            segment.Role + ":" + segment.Style.BaselineRole + "@" + FormatDouble(segment.Style.BaselineOffsetEm)));

    private static string JoinScales(IReadOnlyList<EquationVisualSegment> segments) =>
        string.Join(",", segments.Select(segment =>
            segment.Role + ":" + FormatDouble(segment.Style.FontSizeScale)));

    private static string JoinCellTexts(EquationVisualElement element) =>
        string.Join(
            ",",
            element.MatrixRows.SelectMany(row => row.Cells.Select(cell =>
                "r" + cell.RowIndex.ToString(CultureInfo.InvariantCulture)
                    + "c" + cell.ColumnIndex.ToString(CultureInfo.InvariantCulture)
                    + "=" + NormalizeSignatureText(cell.Text))));

    private static int SegmentCount(EquationVisualPlan? plan) =>
        plan?.Segments.Count ?? 0;

    private static string EqPart(int equationIndex) =>
        "eq=" + equationIndex.ToString(CultureInfo.InvariantCulture);

    private static string FormatDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string BoolFlag(bool value) => value ? "1" : "0";

    private static string NormalizeSignatureText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal)
            .Replace(",", ";", StringComparison.Ordinal);

        return string.Join(
            " ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
