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
    LinearFallback
}

public enum EquationVisualElementKind
{
    Segments,
    Fraction,
    Radical,
    NAry
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
    public static EquationVisualElement FromSegments(
        string linearText,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(EquationVisualElementKind.Segments, linearText, segments, string.Empty, string.Empty, string.Empty, string.Empty);

    public static EquationVisualElement Fraction(
        string linearText,
        string numerator,
        string denominator,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(EquationVisualElementKind.Fraction, linearText, segments, numerator, denominator, string.Empty, string.Empty);

    public static EquationVisualElement Radical(
        string linearText,
        string radicand,
        string degree,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(EquationVisualElementKind.Radical, linearText, segments, string.Empty, string.Empty, radicand, degree);

    public static EquationVisualElement NAry(
        string linearText,
        string @operator,
        string lowerLimit,
        string upperLimit,
        string operand,
        IReadOnlyList<EquationVisualSegment> segments) =>
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
            operand);
}

public sealed record EquationVisualPlan(
    string LinearText,
    string MathFontFamily,
    bool Italic,
    IReadOnlyList<EquationVisualSegment> Segments,
    IReadOnlyList<EquationVisualElement> Elements);

public static class EquationVisualPlanner
{
    public const string DefaultMathFontFamily = "Cambria Math, Cambria, Times New Roman, serif";
    public const double ScriptFontSizeScale = 0.65;
    public const double StructureFontSizeScale = 0.9;
    public const double SuperscriptBaselineOffsetEm = 0.25;
    public const double SubscriptBaselineOffsetEm = -0.18;
    public const double LargeOperatorFontSizeScale = 1.45;
    public const string FractionBarText = "\u2044";
    public const string RadicalSignText = "\u221a";

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

    public static EquationVisualPlan Build(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);

        var segments = new List<EquationVisualSegment>();
        var elements = new List<EquationVisualElement>();
        foreach (var run in equation.Runs)
            AddRunVisual(run, segments, elements);

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
        List<EquationVisualElement> elements)
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
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Base, EquationVisualSegmentRole.Base, NormalStyle),
                    Segment(run.Sup, EquationVisualSegmentRole.Superscript, SuperscriptStyle));
                break;

            case MathRunKind.Subscript:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Base, EquationVisualSegmentRole.Base, NormalStyle),
                    Segment(run.Sub, EquationVisualSegmentRole.Subscript, SubscriptStyle));
                break;

            case MathRunKind.SubSuperscript:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Base, EquationVisualSegmentRole.Base, NormalStyle),
                    Segment(run.Sub, EquationVisualSegmentRole.Subscript, SubscriptStyle),
                    Segment(run.Sup, EquationVisualSegmentRole.Superscript, SuperscriptStyle));
                break;

            case MathRunKind.Fraction:
                AddFractionElement(run, segments, elements);
                break;

            case MathRunKind.Radical:
                AddRadicalElement(run, segments, elements);
                break;

            case MathRunKind.NAry:
                AddNAryElement(run, segments, elements);
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

    private static void AddFractionElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.Numerator, EquationVisualSegmentRole.FractionNumerator, StructureStyle);
        AddIfAny(runSegments, FractionBarText, EquationVisualSegmentRole.FractionBar, NormalStyle);
        AddIfAny(runSegments, run.Denominator, EquationVisualSegmentRole.FractionDenominator, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Fraction(run.LinearText, run.Numerator, run.Denominator, runSegments));
    }

    private static void AddRadicalElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.Degree, EquationVisualSegmentRole.RadicalDegree, SuperscriptStyle);
        AddIfAny(runSegments, RadicalSignText, EquationVisualSegmentRole.RadicalSign, NormalStyle);
        AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.RadicalRadicand, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Radical(run.LinearText, run.Base, run.Degree, runSegments));
    }

    private static void AddNAryElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.Operator, EquationVisualSegmentRole.NAryOperator, LargeOperatorStyle);
        AddIfAny(runSegments, run.Sub, EquationVisualSegmentRole.NAryLowerLimit, SubscriptStyle);
        AddIfAny(runSegments, run.Sup, EquationVisualSegmentRole.NAryUpperLimit, SuperscriptStyle);
        AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.NAryOperand, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.NAry(
            run.LinearText,
            run.Operator,
            run.Sub,
            run.Sup,
            run.Base,
            runSegments));
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
}
