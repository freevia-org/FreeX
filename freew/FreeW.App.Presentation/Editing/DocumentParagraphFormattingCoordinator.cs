using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Owns paragraph-formatting decisions after a renderer has projected its native selection to model indices.
/// </summary>
public sealed class DocumentParagraphFormattingCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentParagraphFormattingCoordinator(DocumentEditingSession session)
    {
        _session = session;
    }

    public bool SetBorder(IReadOnlyList<int> blockIndices, ParagraphBorder? border) =>
        Format(blockIndices, formatting => formatting with { Border = border });

    public bool ToggleBorder(
        IReadOnlyList<int> blockIndices,
        string colorHex = "#000000",
        double widthPt = 0.5)
    {
        var targets = ResolveTargets(blockIndices);
        if (targets.Count == 0)
            return false;

        var enable = targets.Any(index => ((Paragraph)_session.Document.Blocks[index]).Formatting.Border is null);
        var border = enable ? new ParagraphBorder(colorHex, widthPt) : null;
        return Format(targets, formatting => formatting with { Border = border });
    }

    public bool SetShading(
        IReadOnlyList<int> blockIndices,
        string? colorHex,
        ShadingPattern pattern = ShadingPattern.Clear)
    {
        var normalizedColor = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex;
        return Format(blockIndices, formatting => formatting with
        {
            ShadingColorHex = normalizedColor,
            ShadingPattern = normalizedColor is null ? ShadingPattern.Clear : pattern,
        });
    }

    public bool ToggleShading(
        IReadOnlyList<int> blockIndices,
        string? colorHex = "#FFF2CC")
    {
        var targets = ResolveTargets(blockIndices);
        if (targets.Count == 0)
            return false;

        var normalizedColor = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex;
        var clear = normalizedColor is null
            || targets.All(index => string.Equals(
                ((Paragraph)_session.Document.Blocks[index]).Formatting.ShadingColorHex,
                normalizedColor,
                StringComparison.OrdinalIgnoreCase));
        return SetShading(targets, clear ? null : normalizedColor);
    }

    public bool ToggleKeepWithNext(IReadOnlyList<int> blockIndices) =>
        Toggle(
            blockIndices,
            formatting => formatting.KeepWithNext,
            (formatting, value) => formatting with { KeepWithNext = value });

    public bool ToggleKeepLinesTogether(IReadOnlyList<int> blockIndices) =>
        Toggle(
            blockIndices,
            formatting => formatting.KeepLinesTogether,
            (formatting, value) => formatting with { KeepLinesTogether = value });

    public bool ToggleWidowControl(IReadOnlyList<int> blockIndices) =>
        Toggle(
            blockIndices,
            formatting => formatting.WidowControl,
            (formatting, value) => formatting with
            {
                WidowControl = value,
                WidowControlIsSet = true,
            });

    /// <summary>
    /// Toggles one list kind across the complete renderer-projected paragraph selection.
    /// A mixed selection becomes uniformly listed; a selection already using the requested
    /// kind becomes plain paragraphs. Removing list formatting also clears list-only state.
    /// </summary>
    public bool ToggleListKind(IReadOnlyList<int> blockIndices, ListKind kind)
    {
        if (kind is ListKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A concrete list kind is required.");

        var targets = ResolveTargets(blockIndices);
        if (targets.Count == 0)
            return false;

        var enable = targets.Any(index =>
            ((Paragraph)_session.Document.Blocks[index]).Formatting.ListKind != kind);

        // Turning a plain Number list on is the only gesture that ever creates one, so it is also
        // the only place that can tell a brand-new list apart from one being resumed. When the
        // paragraph immediately before this selection is not already a Number list, this is a new,
        // unrelated list and must restart its counter at 1 -- otherwise it silently keeps counting
        // from whatever earlier list last left off, with no UI anywhere to correct it (see
        // ListRestartCounter / DocumentListMarkerSequencePlanner, which already honor an explicit
        // ListStartOverride for Number lists once one is set). Only the first paragraph of the
        // newly-enabled run carries the restart marker: the planner reads ListStartOverride per
        // paragraph, so marking every paragraph would split the run into one-item lists.
        var restartTarget = enable && kind == ListKind.Number && !ContinuesAdjacentNumberList(targets)
            ? targets.Min()
            : (int?)null;

        var pending = new Queue<int>(targets);
        return Format(targets, formatting =>
        {
            var index = pending.Dequeue();
            return enable
                ? formatting with
                {
                    ListKind = kind,
                    ListStartOverride = formatting.ListKind == kind
                        ? formatting.ListStartOverride
                        : index == restartTarget ? 1 : null,
                }
                : formatting with
                {
                    ListKind = ListKind.None,
                    ListLevel = 0,
                    ListStartOverride = null,
                };
        });
    }

    /// <summary>
    /// True when the paragraph immediately preceding this selection is already a Number list --
    /// i.e. this toggle extends that existing list rather than starting an unrelated new one.
    /// </summary>
    private bool ContinuesAdjacentNumberList(IReadOnlyList<int> targets)
    {
        var precedingIndex = targets.Min() - 1;
        return precedingIndex >= 0
            && _session.Document.Blocks[precedingIndex] is Paragraph preceding
            && preceding.Formatting.ListKind == ListKind.Number;
    }

    public bool SetLineSpacing(IReadOnlyList<int> blockIndices, double multiplier) =>
        SetLineSpacing(blockIndices, LineSpacingRule.Multiple, multiplier);

    public bool SetLineSpacing(
        IReadOnlyList<int> blockIndices,
        LineSpacingRule rule,
        double value) =>
        Format(blockIndices, formatting => rule == LineSpacingRule.Multiple
            ? formatting with
            {
                LineRule = rule,
                LineSpacing = Math.Max(0.5, value),
                LineSpacingIsSet = true,
            }
            : formatting with
            {
                LineRule = rule,
                LineHeightPt = Math.Max(1, value),
                LineSpacingIsSet = true,
            });

    public bool SetTabStops(IReadOnlyList<int> blockIndices, IReadOnlyList<TabStop> tabStops)
    {
        ArgumentNullException.ThrowIfNull(tabStops);
        var normalized = tabStops.ToArray();
        return Format(blockIndices, formatting => formatting with { TabStops = normalized });
    }

    public bool ApplyDialogFormatting(
        IReadOnlyList<int> blockIndices,
        TextAlignment alignment,
        double indentLeftPt,
        double indentRightPt,
        double firstLineIndentPt,
        double spaceBeforePt,
        double spaceAfterPt,
        LineSpacingRule lineRule,
        double lineSpacingValue) =>
        Format(blockIndices, formatting =>
        {
            var updated = formatting with
            {
                Alignment = alignment,
                IndentLeftPt = Math.Max(0, indentLeftPt),
                IndentRightPt = Math.Max(0, indentRightPt),
                FirstLineIndentPt = firstLineIndentPt,
                SpaceBeforePt = Math.Max(0, spaceBeforePt),
                SpaceAfterPt = Math.Max(0, spaceAfterPt),
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true,
                LineSpacingIsSet = true,
            };
            return lineRule == LineSpacingRule.Multiple
                ? updated with
                {
                    LineRule = lineRule,
                    LineSpacing = Math.Max(0.5, lineSpacingValue),
                }
                : updated with
                {
                    LineRule = lineRule,
                    LineHeightPt = Math.Max(1, lineSpacingValue),
                };
        });

    public bool ApplyDialogFormatting(
        IReadOnlyList<int> blockIndices,
        double indentLeftPt,
        double indentRightPt,
        double firstLineIndentPt,
        double spaceBeforePt,
        double spaceAfterPt,
        double lineSpacing,
        bool keepWithNext,
        bool keepLinesTogether,
        bool widowControl,
        bool pageBreakBefore,
        bool suppressAutoHyphens,
        bool suppressLineNumbers,
        bool contextualSpacing) =>
        Format(blockIndices, formatting => formatting with
        {
            IndentLeftPt = Math.Max(0, indentLeftPt),
            IndentRightPt = Math.Max(0, indentRightPt),
            FirstLineIndentPt = firstLineIndentPt,
            SpaceBeforePt = Math.Max(0, spaceBeforePt),
            SpaceAfterPt = Math.Max(0, spaceAfterPt),
            SpaceBeforeIsSet = true,
            SpaceAfterIsSet = true,
            LineRule = LineSpacingRule.Multiple,
            LineSpacing = Math.Max(0.5, lineSpacing),
            LineSpacingIsSet = true,
            KeepWithNext = keepWithNext,
            KeepLinesTogether = keepLinesTogether,
            WidowControl = widowControl,
            WidowControlIsSet = true,
            PageBreakBefore = pageBreakBefore,
            SuppressAutoHyphens = suppressAutoHyphens,
            SuppressAutoHyphensIsSet = true,
            SuppressLineNumbers = suppressLineNumbers,
            SuppressLineNumbersIsSet = true,
            ContextualSpacing = contextualSpacing,
        });

    private bool Toggle(
        IReadOnlyList<int> blockIndices,
        Func<ParagraphFormatting, bool> isSet,
        Func<ParagraphFormatting, bool, ParagraphFormatting> set)
    {
        var targets = ResolveTargets(blockIndices);
        if (targets.Count == 0)
            return false;

        var value = targets.Any(index => !isSet(((Paragraph)_session.Document.Blocks[index]).Formatting));
        return Format(targets, formatting => set(formatting, value));
    }

    private bool Format(
        IReadOnlyList<int> blockIndices,
        Func<ParagraphFormatting, ParagraphFormatting> transform) =>
        _session.FormatParagraphs(ResolveTargets(blockIndices), transform);

    private IReadOnlyList<int> ResolveTargets(IReadOnlyList<int> blockIndices)
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        return blockIndices
            .Where(index => index >= 0
                && index < _session.Document.Blocks.Count
                && _session.Document.Blocks[index] is Paragraph)
            .Distinct()
            .ToArray();
    }
}
