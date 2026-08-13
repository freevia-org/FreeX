using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Rendering;

public enum CellTextBaselineKind
{
    Baseline,
    Superscript,
    Subscript,
}

public enum CellRichTextMaterializationMode
{
    FormattedDisplayTextRanges,
    NativeRunText,
}

public sealed record CellTextMaterializationProfile(
    bool ApplyCellScriptToRichText,
    CellRichTextMaterializationMode RichTextMode)
{
    public static CellTextMaterializationProfile Wpf { get; } =
        new(true, CellRichTextMaterializationMode.FormattedDisplayTextRanges);

    public static CellTextMaterializationProfile Avalonia { get; } =
        new(false, CellRichTextMaterializationMode.NativeRunText);
}

public sealed record CellTextFormattingInputs(
    string DisplayText,
    string NumberFormatCode,
    bool IsNumericOrDate);

public sealed record CellTextRunMaterializationSegment(
    ResolvedCellTextRun Run,
    string Text,
    int Start,
    int Length);

public sealed record CellTextMaterializationPlan(
    CellTextFormattingInputs Formatting,
    double BaseFontSize,
    double RenderedFontSize,
    double BaselineOffset,
    CellTextBaselineKind Baseline,
    bool HasRichText,
    IReadOnlyList<CellTextRunMaterializationSegment> RunSegments);

/// <summary>
/// Portable policy for turning display-ready cell text and resolved rich runs into renderer inputs.
/// Native text layout, measurement, clipping, and drawing remain in the platform adapters.
/// </summary>
public static class CellTextMaterializationPlanner
{
    public const double ScriptFontSizeFactor = 0.583;
    public const double SuperscriptBaselineRatio = 0.33;
    public const double SubscriptBaselineRatio = 0.14;

    private static readonly IReadOnlyList<CellTextRunMaterializationSegment> NoSegments =
        Array.AsReadOnly(Array.Empty<CellTextRunMaterializationSegment>());

    public static CellTextMaterializationPlan Plan(
        string displayText,
        bool isNumericOrDate,
        CellStyle? style,
        double displayFontSize,
        IReadOnlyList<ResolvedCellTextRun>? richRuns,
        CellTextMaterializationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(displayText);
        ArgumentNullException.ThrowIfNull(profile);

        var hasRichText = richRuns is { Count: > 0 };
        var baseline = ResolveBaseline(style);
        var applyCellScript = baseline != CellTextBaselineKind.Baseline &&
            (!hasRichText || profile.ApplyCellScriptToRichText);
        var renderedFontSize = applyCellScript
            ? displayFontSize * ScriptFontSizeFactor
            : displayFontSize;
        var baselineOffset = applyCellScript
            ? baseline switch
            {
                CellTextBaselineKind.Superscript => -displayFontSize * SuperscriptBaselineRatio,
                CellTextBaselineKind.Subscript => displayFontSize * SubscriptBaselineRatio,
                _ => 0,
            }
            : 0;

        return new CellTextMaterializationPlan(
            new CellTextFormattingInputs(
                displayText,
                style?.NumberFormat ?? CellStyle.Default.NumberFormat,
                isNumericOrDate),
            displayFontSize,
            renderedFontSize,
            baselineOffset,
            applyCellScript ? baseline : CellTextBaselineKind.Baseline,
            hasRichText,
            MaterializeRuns(displayText, richRuns, profile.RichTextMode));
    }

    public static IReadOnlyList<CellTextRunMaterializationSegment> MaterializeRuns(
        string displayText,
        IReadOnlyList<ResolvedCellTextRun>? runs,
        CellRichTextMaterializationMode mode)
    {
        ArgumentNullException.ThrowIfNull(displayText);
        if (runs is null or { Count: 0 })
            return NoSegments;

        var segments = new List<CellTextRunMaterializationSegment>(runs.Count);
        var offset = 0;
        foreach (var run in runs)
        {
            var sourceLength = run.Text.Length;
            if (mode == CellRichTextMaterializationMode.NativeRunText)
            {
                segments.Add(new CellTextRunMaterializationSegment(
                    run,
                    run.Text,
                    offset,
                    sourceLength));
                offset += sourceLength;
                continue;
            }

            if (sourceLength == 0)
                continue;
            if (offset >= displayText.Length)
                break;

            var length = Math.Min(sourceLength, displayText.Length - offset);
            segments.Add(new CellTextRunMaterializationSegment(
                run,
                displayText.Substring(offset, length),
                offset,
                length));
            offset += sourceLength;
        }

        return Array.AsReadOnly(segments.ToArray());
    }

    private static CellTextBaselineKind ResolveBaseline(CellStyle? style)
    {
        if (style?.Superscript == true)
            return CellTextBaselineKind.Superscript;
        if (style?.Subscript == true)
            return CellTextBaselineKind.Subscript;
        return CellTextBaselineKind.Baseline;
    }
}
