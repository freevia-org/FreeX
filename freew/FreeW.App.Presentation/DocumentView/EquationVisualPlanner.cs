using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum EquationVisualSegmentRole
{
    Text,
    Base,
    Superscript,
    Subscript,
    LinearFallback
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

public sealed record EquationVisualPlan(
    string LinearText,
    string MathFontFamily,
    bool Italic,
    IReadOnlyList<EquationVisualSegment> Segments);

public static class EquationVisualPlanner
{
    public const string DefaultMathFontFamily = "Cambria Math, Cambria, Times New Roman, serif";
    public const double ScriptFontSizeScale = 0.65;
    public const double SuperscriptBaselineOffsetEm = 0.25;
    public const double SubscriptBaselineOffsetEm = -0.18;

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

    public static EquationVisualPlan Build(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);

        var segments = new List<EquationVisualSegment>();
        foreach (var run in equation.Runs)
            AddRunSegments(run, segments);

        if (segments.Count == 0 && equation.LinearText.Length > 0)
            segments.Add(new EquationVisualSegment(equation.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle));

        return new EquationVisualPlan(
            equation.LinearText,
            DefaultMathFontFamily,
            Italic: true,
            Segments: segments);
    }

    private static void AddRunSegments(MathRun run, List<EquationVisualSegment> segments)
    {
        switch (run.Kind)
        {
            case MathRunKind.Text:
                AddIfAny(segments, run.Text, EquationVisualSegmentRole.Text, NormalStyle);
                break;

            case MathRunKind.Superscript:
                AddIfAny(segments, run.Base, EquationVisualSegmentRole.Base, NormalStyle);
                AddIfAny(segments, run.Sup, EquationVisualSegmentRole.Superscript, SuperscriptStyle);
                break;

            case MathRunKind.Subscript:
                AddIfAny(segments, run.Base, EquationVisualSegmentRole.Base, NormalStyle);
                AddIfAny(segments, run.Sub, EquationVisualSegmentRole.Subscript, SubscriptStyle);
                break;

            case MathRunKind.SubSuperscript:
                AddIfAny(segments, run.Base, EquationVisualSegmentRole.Base, NormalStyle);
                AddIfAny(segments, run.Sub, EquationVisualSegmentRole.Subscript, SubscriptStyle);
                AddIfAny(segments, run.Sup, EquationVisualSegmentRole.Superscript, SuperscriptStyle);
                break;

            default:
                AddIfAny(segments, run.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle);
                break;
        }
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
