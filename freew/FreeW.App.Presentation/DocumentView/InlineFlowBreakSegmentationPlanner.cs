namespace FreeW.App.Presentation.DocumentView;

/// <summary>The renderer-neutral flow boundary represented by an inline break run.</summary>
public enum InlineFlowBreakKind
{
    None,
    Column,
    Page,
}

/// <summary>
/// Describes one renderer-projected inline run. Text runs contribute to the source offset; break runs do not.
/// </summary>
public readonly record struct InlineFlowRunInput(
    int SourceLength,
    bool IsPageBreak = false,
    bool IsColumnBreak = false);

/// <summary>Resolved source mapping and break policy for one projected inline run.</summary>
public readonly record struct InlineFlowRunPlan(
    int RunIndex,
    int SourceOffset,
    int SourceLength,
    InlineFlowBreakKind BreakKind);

/// <summary>An inline break at a stable source offset.</summary>
public readonly record struct InlineFlowBreakDescriptor(
    int RunIndex,
    int SourceOffset,
    InlineFlowBreakKind Kind);

/// <summary>
/// A contiguous renderer fragment. The break marker that terminates a fragment remains in that fragment,
/// while <see cref="BreakBefore"/> describes the native pagination primitive for the fragment that follows.
/// </summary>
public readonly record struct InlineFlowSegmentPlan(
    int StartRunIndex,
    int RunCount,
    int SourceStartOffset,
    int SourceLength,
    InlineFlowBreakKind BreakBefore)
{
    public int EndRunIndex => StartRunIndex + RunCount;
}

/// <summary>Shared segmentation and source-offset plan consumed by the WPF and Avalonia renderers.</summary>
public sealed record InlineFlowBreakSegmentationPlan(
    IReadOnlyList<InlineFlowRunPlan> Runs,
    IReadOnlyList<InlineFlowBreakDescriptor> Breaks,
    IReadOnlyList<InlineFlowSegmentPlan> Segments,
    int SourceLength)
{
    public bool HasInlineBreaks => Breaks.Count > 0;

    public int SourceOffsetAtBoundary(int runBoundaryIndex)
    {
        if (runBoundaryIndex <= 0 || Runs.Count == 0)
            return 0;
        if (runBoundaryIndex >= Runs.Count)
            return SourceLength;
        return Runs[runBoundaryIndex].SourceOffset;
    }
}

/// <summary>
/// Owns inline page/column-break precedence, source mapping, and fragmentation independently of UI types.
/// </summary>
public static class InlineFlowBreakSegmentationPlanner
{
    public static InlineFlowBreakSegmentationPlan Build(
        IReadOnlyList<InlineFlowRunInput> runs,
        bool pageBreakBefore = false)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var runPlans = new List<InlineFlowRunPlan>(runs.Count);
        var breaks = new List<InlineFlowBreakDescriptor>();
        var segments = new List<InlineFlowSegmentPlan>();
        var sourceOffset = 0;
        var segmentStartRunIndex = 0;
        var segmentSourceStartOffset = 0;
        var breakBefore = pageBreakBefore ? InlineFlowBreakKind.Page : InlineFlowBreakKind.None;

        for (var runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            if (run.SourceLength < 0)
                throw new ArgumentOutOfRangeException(nameof(runs), "Inline source lengths cannot be negative.");

            var breakKind = ResolveBreakKind(run.IsPageBreak, run.IsColumnBreak);
            var sourceLength = breakKind == InlineFlowBreakKind.None ? run.SourceLength : 0;
            runPlans.Add(new InlineFlowRunPlan(runIndex, sourceOffset, sourceLength, breakKind));

            if (breakKind != InlineFlowBreakKind.None)
            {
                breaks.Add(new InlineFlowBreakDescriptor(runIndex, sourceOffset, breakKind));
                segments.Add(new InlineFlowSegmentPlan(
                    segmentStartRunIndex,
                    runIndex - segmentStartRunIndex + 1,
                    segmentSourceStartOffset,
                    sourceOffset - segmentSourceStartOffset,
                    breakBefore));
                segmentStartRunIndex = runIndex + 1;
                segmentSourceStartOffset = sourceOffset;
                breakBefore = breakKind;
            }
            else
                sourceOffset += sourceLength;
        }

        segments.Add(new InlineFlowSegmentPlan(
            segmentStartRunIndex,
            runs.Count - segmentStartRunIndex,
            segmentSourceStartOffset,
            sourceOffset - segmentSourceStartOffset,
            breakBefore));

        return new InlineFlowBreakSegmentationPlan(runPlans, breaks, segments, sourceOffset);
    }

    public static InlineFlowBreakKind ResolveBreakKind(bool isPageBreak, bool isColumnBreak) =>
        isPageBreak
            ? InlineFlowBreakKind.Page
            : isColumnBreak
                ? InlineFlowBreakKind.Column
                : InlineFlowBreakKind.None;
}
