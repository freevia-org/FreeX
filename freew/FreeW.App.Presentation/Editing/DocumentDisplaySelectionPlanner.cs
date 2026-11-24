using System.Text;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// One source run's resolved display projection, in paragraph order. Include hidden/textless runs
/// with empty display text. Atomic runs are selected by identity, never by their cached text offsets.
/// </summary>
/// <param name="DisplayToModelBoundaries">
/// Optional run-local UTF-16 boundary map, with DisplayText.Length + 1 entries. The first/last entries
/// are zero/SourceRun.Text.Length. An interior -1 marks a boundary that cannot be edited exactly.
/// Literal projections of equal length use identity mapping when this map is absent.
/// </param>
public sealed record DocumentRunDisplaySpan(
    int SourceRunIndex,
    Run SourceRun,
    string DisplayText,
    bool IsAtomic,
    IReadOnlyList<int>? DisplayToModelBoundaries = null);

/// <summary>A selected literal slice: display offsets are paragraph-local, model offsets run-local.</summary>
public sealed record DocumentLiteralRunSelection(
    DocumentRunDisplaySpan Span,
    int DisplayStart,
    int DisplayLength,
    int ModelStart,
    int ModelLength);

public enum DocumentDisplaySelectionRefusal
{
    None,
    PartialAtomicRun,
    MissingLiteralBoundaryMap,
    UnmappedLiteralBoundary,
    SplitSurrogatePair
}

/// <summary>
/// A read-only selection description. Refused plans still expose the exact selected display text,
/// but expose no destructive literal/atomic selections. CanMutate is false for collapsed selections.
/// No document, run, bookmark, revision, or note metadata is changed by planning.
/// </summary>
public sealed record DocumentDisplaySelectionPlan(
    string DisplayText,
    int DisplayStart,
    int DisplayEnd,
    string SelectedText,
    IReadOnlyList<DocumentLiteralRunSelection> LiteralRanges,
    IReadOnlyList<DocumentRunDisplaySpan> WholeAtomicRuns,
    IReadOnlyList<DocumentRunDisplaySpan> PreservedZeroWidthRuns,
    DocumentDisplaySelectionRefusal Refusal)
{
    public bool CanMutate => Refusal == DocumentDisplaySelectionRefusal.None && DisplayEnd > DisplayStart;
}

/// <summary>
/// Plans a selection within one paragraph/story's UTF-16 display stream. This does not infer story,
/// table, paragraph-mark, or content-lock semantics; the host must validate those before mutation.
/// </summary>
public static class DocumentDisplaySelectionPlanner
{
    public static DocumentDisplaySelectionPlan Plan(
        IReadOnlyList<DocumentRunDisplaySpan> spans,
        int anchorDisplayOffset,
        int activeDisplayOffset)
    {
        ArgumentNullException.ThrowIfNull(spans);
        var text = new StringBuilder();
        var zeroWidth = new List<DocumentRunDisplaySpan>();
        var previousIndex = -1;
        foreach (var span in spans)
        {
            ArgumentNullException.ThrowIfNull(span);
            ArgumentNullException.ThrowIfNull(span.SourceRun);
            ArgumentNullException.ThrowIfNull(span.DisplayText);
            if (span.SourceRunIndex <= previousIndex)
                throw new ArgumentException("Source run indexes must be nonnegative and strictly increasing.", nameof(spans));
            previousIndex = span.SourceRunIndex;
            ValidateBoundaryMap(span);
            text.Append(span.DisplayText);
            if (span.DisplayText.Length == 0)
                zeroWidth.Add(span);
        }

        var displayText = text.ToString();
        var start = Math.Clamp(Math.Min(anchorDisplayOffset, activeDisplayOffset), 0, displayText.Length);
        var end = Math.Clamp(Math.Max(anchorDisplayOffset, activeDisplayOffset), 0, displayText.Length);
        var literals = new List<DocumentLiteralRunSelection>();
        var atomics = new List<DocumentRunDisplaySpan>();
        var refusal = start < end && (SplitsSurrogate(displayText, start) || SplitsSurrogate(displayText, end))
            ? DocumentDisplaySelectionRefusal.SplitSurrogatePair
            : DocumentDisplaySelectionRefusal.None;
        var displayPosition = 0;
        if (start < end && refusal == DocumentDisplaySelectionRefusal.None)
        {
            foreach (var span in spans)
            {
                var runStart = displayPosition;
                displayPosition += span.DisplayText.Length;
                var localStart = Math.Max(start, runStart) - runStart;
                var localEnd = Math.Min(end, displayPosition) - runStart;
                if (span.DisplayText.Length == 0 || localEnd <= localStart)
                    continue;

                if (span.IsAtomic)
                {
                    if (localStart != 0 || localEnd != span.DisplayText.Length)
                    {
                        refusal = DocumentDisplaySelectionRefusal.PartialAtomicRun;
                        break;
                    }
                    atomics.Add(span);
                    continue;
                }

                if (span.DisplayToModelBoundaries is null && span.DisplayText.Length != span.SourceRun.Text.Length)
                {
                    refusal = DocumentDisplaySelectionRefusal.MissingLiteralBoundaryMap;
                    break;
                }
                var modelStart = span.DisplayToModelBoundaries?[localStart] ?? localStart;
                var modelEnd = span.DisplayToModelBoundaries?[localEnd] ?? localEnd;
                if (modelStart < 0 || modelEnd <= modelStart)
                {
                    refusal = DocumentDisplaySelectionRefusal.UnmappedLiteralBoundary;
                    break;
                }
                if (SplitsSurrogate(span.DisplayText, localStart) || SplitsSurrogate(span.DisplayText, localEnd)
                    || SplitsSurrogate(span.SourceRun.Text, modelStart) || SplitsSurrogate(span.SourceRun.Text, modelEnd))
                {
                    refusal = DocumentDisplaySelectionRefusal.SplitSurrogatePair;
                    break;
                }
                literals.Add(new(span, runStart + localStart, localEnd - localStart, modelStart, modelEnd - modelStart));
            }
        }

        return new(displayText, start, end, displayText[start..end],
            refusal == DocumentDisplaySelectionRefusal.None ? literals.ToArray() : [],
            refusal == DocumentDisplaySelectionRefusal.None ? atomics.ToArray() : [],
            zeroWidth.ToArray(), refusal);
    }

    private static bool SplitsSurrogate(string text, int boundary) =>
        boundary > 0 && boundary < text.Length
        && char.IsHighSurrogate(text[boundary - 1]) && char.IsLowSurrogate(text[boundary]);

    private static void ValidateBoundaryMap(DocumentRunDisplaySpan span)
    {
        if (span.DisplayToModelBoundaries is not { } map)
            return;
        if (span.IsAtomic || map.Count != span.DisplayText.Length + 1
            || map[0] != 0 || map[^1] != span.SourceRun.Text.Length)
            throw new ArgumentException("Literal boundary maps must cover the complete display and model text.", nameof(span));
        var previous = 0;
        foreach (var boundary in map)
        {
            if (boundary == -1)
                continue;
            if (boundary < previous || boundary > span.SourceRun.Text.Length)
                throw new ArgumentException("Literal boundary maps must be monotonic; only -1 may represent an unmapped boundary.", nameof(span));
            previous = boundary;
        }
    }
}
