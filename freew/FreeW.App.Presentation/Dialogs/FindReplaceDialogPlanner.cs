using System.Diagnostics.CodeAnalysis;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum FindReplaceOptionKind
{
    MatchCase,
    WholeWord,
    UseWildcards
}

public enum FindReplaceDialogFieldKind
{
    Find,
    Replace,
}

public enum FindReplaceDialogActionKind
{
    FindNext,
    Replace,
    ReplaceAll,
    Close,
}

public enum FindReplaceValidationError
{
    SearchTermRequired
}

public readonly record struct FindReplaceOptionChoice(
    FindReplaceOptionKind Kind,
    string Label,
    string AutomationId);

public sealed record FindReplaceDialogFieldSpec(
    FindReplaceDialogFieldKind Kind,
    string Label,
    string AutomationId);

public sealed record FindReplaceDialogActionSpec(
    FindReplaceDialogActionKind Kind,
    string Label,
    string AutomationId);

public sealed record FindReplaceDialogMetrics(
    double WindowWidth,
    double OuterMargin,
    double FieldMinWidth,
    double ButtonMinWidth,
    double RowTopMargin,
    double ActionTopMargin);

public sealed record FindReplaceDialogSurfaceSpec(
    string Title,
    IReadOnlyList<FindReplaceDialogFieldSpec> Fields,
    IReadOnlyList<FindReplaceOptionChoice> Options,
    IReadOnlyList<FindReplaceDialogActionSpec> Actions,
    string SpecialButtonLabel,
    string SpecialButtonAutomationId,
    string GoToSectionLabel,
    string GoToButtonLabel,
    string GoToButtonAutomationId,
    string GoToTargetAutomationId,
    FindReplaceDialogMetrics Metrics)
{
    public FindReplaceDialogFieldSpec Field(FindReplaceDialogFieldKind kind) =>
        Fields.First(field => field.Kind == kind);

    public FindReplaceOptionChoice Option(FindReplaceOptionKind kind) =>
        Options.First(option => option.Kind == kind);
}

public readonly record struct FindReplaceOptionPlan(
    FindReplaceOptionKind Kind,
    string Label,
    bool IsEnabled);

/// <summary>
/// FreeW's search-option triple stays app-local on purpose. Only <c>MatchCase</c> is common to all
/// three sister apps: FreeX's option set is <c>Within</c>/<c>SearchOrder</c>/<c>LookIn</c> plus
/// match-entire-CELL (a different rule from whole-WORD), and FreeP has no wildcard concept at all.
/// The single portable decision here -- wildcards suppress whole-word matching -- has exactly one
/// consumer, so <see cref="FindReplaceDialogPlanner.NormalizeOptions"/> and
/// <see cref="FindReplaceDialogPlanner.IsOptionEnabled"/> are deliberately NOT extracted into
/// <see cref="FindReplaceDialogPolicy"/>.
/// </summary>
public readonly record struct FindReplaceSearchOptions(
    bool MatchCase,
    bool WholeWord,
    bool UseWildcards);

public sealed record FindReplaceSearchRequest(
    string Term,
    FindReplaceSearchOptions Options);

public sealed record FindReplaceReplaceRequest(
    string Term,
    string Replacement,
    FindReplaceSearchOptions Options);

/// <summary>
/// A located Find/Replace hit. <see cref="Block"/> is always a top-level <c>document.Blocks</c> index
/// (of a <see cref="Paragraph"/> or, for a table hit, the owning <see cref="Table"/>). When the hit is
/// inside a table cell, <see cref="TableRow"/>/<see cref="TableCol"/>/<see cref="TableParagraphIndex"/>
/// locate the cell and the paragraph within it -- <see cref="TableCol"/> is the GRID-PROJECTED column
/// (<see cref="TableGridProjection.StartColumn"/>), not a raw <see cref="TableRow.Cells"/> index, matching
/// the convention every consumer (DocumentView.GetCellParagraph via TableGridProjection.StartingAt, the
/// PlacedChar.CellCol placed-glyph lookup, the _cellCaret/_cellAnchor tuples) already uses; for a body
/// paragraph hit all three are null.
/// </summary>
public readonly record struct FindReplaceMatch(
    int Block,
    int Start,
    int Length,
    int? TableRow = null,
    int? TableCol = null,
    int? TableParagraphIndex = null)
{
    public bool IsInTableCell => TableRow is not null;
}

public enum FindReplaceGoToTargetKind
{
    DocumentStart,
    DocumentEnd,
    Heading,
    Bookmark
}

public sealed record FindReplaceGoToTarget(
    FindReplaceGoToTargetKind Kind,
    int BlockIndex,
    string Label)
{
    public override string ToString() => Label;
}

public sealed record FindReplaceGoToExecutionPlan(
    FindReplaceGoToTargetKind Kind,
    int BlockIndex,
    string Label,
    string StatusText);

public static class FindReplaceDialogPlanner
{
    public const string SearchTermRequiredMessage = FindReplaceDialogPolicy.SearchTermRequiredMessage;

    public static FindReplacePolicyTextDescriptor PolicyTextDescriptor { get; } = new(
        CommonShellTextResources.FindReplaceSearchTermRequired,
        CommonShellTextResources.FindReplaceNoMatches,
        Text("FreeW_FindReplace_NoReplacements", FindReplacePolicyTextSpec.NeutralEnglish.NoReplacements),
        CommonShellTextResources.FindReplaceNotFoundFormat,
        CommonShellTextResources.FindReplaceMatchFormat,
        Text("FreeW_FindReplace_ReplacedOccurrences_Format", FindReplacePolicyTextSpec.NeutralEnglish.ReplacedOccurrencesFormat),
        Text("FreeW_FindReplace_ReplacementsMade_Format", FindReplacePolicyTextSpec.NeutralEnglish.ReplacementsMadeFormat));

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        PolicyTextDescriptor.SearchTermRequired.ResourceKey,
        PolicyTextDescriptor.NoMatches.ResourceKey,
        PolicyTextDescriptor.NoReplacements.ResourceKey,
        PolicyTextDescriptor.NotFoundFormat.ResourceKey,
        PolicyTextDescriptor.MatchFormat.ResourceKey,
        PolicyTextDescriptor.ReplacedOccurrencesFormat.ResourceKey,
        PolicyTextDescriptor.ReplacementsMadeFormat.ResourceKey,
    ];

    public static FindReplacePolicyTextSpec ResolvePolicyText(Func<string, string?>? getText = null) =>
        FindReplacePolicyTextSpec.FromDescriptor(PolicyTextDescriptor, getText);

    private static readonly FindReplaceOptionChoice[] OptionChoiceValues =
    [
        new(FindReplaceOptionKind.MatchCase, "Match case", "FindReplaceMatchCaseCheckBox"),
        new(FindReplaceOptionKind.WholeWord, "Whole word", "FindReplaceWholeWordCheckBox"),
        new(FindReplaceOptionKind.UseWildcards, "Use wildcards  (* ? [ ] < >)", "FindReplaceUseWildcardsCheckBox")
    ];

    public static FindReplaceDialogSurfaceSpec Surface { get; } = new(
        "Find & Replace",
        [
            new(FindReplaceDialogFieldKind.Find, "Find:", "FindReplaceFindTextBox"),
            new(FindReplaceDialogFieldKind.Replace, "Replace:", "FindReplaceReplacementTextBox"),
        ],
        OptionChoiceValues,
        [
            new(FindReplaceDialogActionKind.FindNext, "Find Next", "FindReplaceFindNextButton"),
            new(FindReplaceDialogActionKind.Replace, "Replace", "FindReplaceReplaceButton"),
            new(FindReplaceDialogActionKind.ReplaceAll, "Replace All", "FindReplaceReplaceAllButton"),
            new(FindReplaceDialogActionKind.Close, "Close", "FindReplaceCloseButton"),
        ],
        "Special \u25be",
        "FindReplaceSpecialButton",
        "Go to:",
        "Go",
        "FindReplaceGoToButton",
        "FindReplaceGoToTargetComboBox",
        new FindReplaceDialogMetrics(
            WindowWidth: 420,
            OuterMargin: 14,
            FieldMinWidth: 220,
            ButtonMinWidth: 84,
            RowTopMargin: 6,
            ActionTopMargin: 10));

    public static IReadOnlyList<FindReplaceOptionChoice> OptionChoices => OptionChoiceValues;

    public static IReadOnlyList<FindReplaceOptionPlan> BuildOptionPlans(FindReplaceSearchOptions options)
    {
        var effective = NormalizeOptions(options);
        return OptionChoiceValues
            .Select(choice => new FindReplaceOptionPlan(
                choice.Kind,
                choice.Label,
                IsOptionEnabled(choice.Kind, effective)))
            .ToArray();
    }

    public static string LabelFor(FindReplaceOptionKind kind) =>
        OptionChoiceValues.First(choice => choice.Kind == kind).Label;

    public static bool IsOptionEnabled(FindReplaceOptionKind kind, FindReplaceSearchOptions options) =>
        kind != FindReplaceOptionKind.WholeWord || !options.UseWildcards;

    public static FindReplaceSearchOptions NormalizeOptions(FindReplaceSearchOptions options) =>
        options.UseWildcards
            ? options with { WholeWord = false }
            : options;

    public static IReadOnlyList<FindReplaceGoToTarget> BuildGoToTargets(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var targets = new List<FindReplaceGoToTarget>
        {
            new(FindReplaceGoToTargetKind.DocumentStart, 0, "Document start"),
            new(FindReplaceGoToTargetKind.DocumentEnd, Math.Max(0, document.Blocks.Count - 1), "Document end"),
        };

        foreach (var entry in DocumentOutline.Of(document))
        {
            var text = string.IsNullOrWhiteSpace(entry.Text) ? "(untitled heading)" : entry.Text;
            targets.Add(new FindReplaceGoToTarget(
                FindReplaceGoToTargetKind.Heading,
                entry.BlockIndex,
                new string(' ', entry.Level * 2) + text));
        }

        targets.AddRange(Bookmarks.List(document).Select(bookmark =>
            new FindReplaceGoToTarget(
                FindReplaceGoToTargetKind.Bookmark,
                bookmark.BlockIndex,
                $"Bookmark: {bookmark.Name}")));
        return targets;
    }

    public static FindReplaceGoToExecutionPlan? PlanGoTo(
        FindReplaceGoToTarget? target,
        int blockCount)
    {
        if (target is null)
            return null;

        var lastBlockIndex = Math.Max(0, blockCount - 1);
        var blockIndex = target.Kind switch
        {
            FindReplaceGoToTargetKind.DocumentStart => 0,
            FindReplaceGoToTargetKind.DocumentEnd => lastBlockIndex,
            _ => Math.Clamp(target.BlockIndex, 0, lastBlockIndex),
        };
        var label = target.Label.Trim();
        return new FindReplaceGoToExecutionPlan(
            target.Kind,
            blockIndex,
            label,
            $"Jumped to {label}.");
    }

    public static bool ShouldUsePlainEditorSearch(FindReplaceSearchOptions options)
    {
        var effective = NormalizeOptions(options);
        return !effective.MatchCase && !effective.WholeWord && !effective.UseWildcards;
    }

    public static bool TryCreateSearchRequest(
        string? term,
        FindReplaceSearchOptions options,
        out FindReplaceSearchRequest? request,
        out FindReplaceValidationError? error)
    {
        request = null;
        error = null;

        if (!TryValidateSearchTerm(term, out error))
        {
            return false;
        }

        request = new FindReplaceSearchRequest(term, NormalizeOptions(options));
        return true;
    }

    public static bool TryCreateReplaceRequest(
        string? term,
        string? replacement,
        FindReplaceSearchOptions options,
        out FindReplaceReplaceRequest? request,
        out FindReplaceValidationError? error)
    {
        request = null;
        error = null;

        if (!TryValidateSearchTerm(term, out error))
        {
            return false;
        }

        request = new FindReplaceReplaceRequest(term, replacement ?? string.Empty, NormalizeOptions(options));
        return true;
    }

    public static string ValidationMessageFor(
        FindReplaceValidationError? error,
        FindReplacePolicyTextSpec? text = null) =>
        FindReplaceDialogPolicy.ValidationMessageFor(ToSharedValidationError(error), text);

    public static string BuildFindStatus(
        FindReplaceSearchRequest request,
        bool found,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildFindStatus(request.Term, found, text);
    }

    public static string BuildReplaceStatus(
        FindReplaceReplaceRequest request,
        bool replaced,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildReplaceStatus(request.Term, replaced, text);
    }

    public static string BuildReplaceAllStatus(
        FindReplaceReplaceRequest request,
        int replacementCount,
        bool inSelection = false,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus(request.Term, replacementCount, text);
        return inSelection && replacementCount > 0
            ? status[..^1] + " in selection."
            : status;
    }

    private static ResourceTextDescriptor Text(string resourceKey, string fallbackText) =>
        new(resourceKey, fallbackText);

    public static bool DocumentContains(TextDocument document, FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CountMatches(document, request.Term, request.Options) > 0;
    }

    public static IReadOnlyList<(int Start, int Length)> FindAll(
        string? text,
        string? term,
        FindReplaceSearchOptions options)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return [];

        var effective = NormalizeOptions(options);
        return TextSearch.FindAll(
                text,
                term,
                effective.MatchCase,
                effective.WholeWord,
                effective.UseWildcards)
            .ToList();
    }

    public static bool MatchesExactly(
        string? text,
        string? term,
        FindReplaceSearchOptions options) =>
        text is not null
        && FindAll(text, term, options)
            .Any(match => match.Start == 0 && match.Length == text.Length);

    /// <summary>
    /// Locates the next Find/Replace match at or after <paramref name="fromBlock"/>/<paramref name="fromOffset"/>,
    /// wrapping back to the start of the document (and, within the starting block, to any match before
    /// the start offset) when nothing later is found. When the search resumes inside a table cell -- i.e.
    /// the caret is currently positioned in one -- pass <paramref name="fromTableCell"/> (row, column,
    /// paragraph-in-cell index, and text offset within that paragraph) so the walk continues from that
    /// exact cell position instead of restarting the owning table from its first cell every call.
    /// </summary>
    public static FindReplaceMatch? FindNextMatch(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options,
        int fromBlock,
        int fromOffset,
        (int Row, int Col, int ParagraphIndex, int Offset)? fromTableCell = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term) || document.Blocks.Count == 0)
            return null;

        var startBlock = Math.Clamp(fromBlock, 0, document.Blocks.Count - 1);
        for (var step = 0; step < document.Blocks.Count; step++)
        {
            var blockIndex = (startBlock + step) % document.Blocks.Count;
            var block = document.Blocks[blockIndex];

            if (block is Paragraph paragraph)
            {
                var startAt = step == 0 ? Math.Clamp(fromOffset, 0, paragraph.PlainText.Length) : 0;
                var match = FindAll(paragraph.PlainText, term, options)
                    .FirstOrDefault(item => item.Start >= startAt);
                if (match.Length > 0)
                    return new FindReplaceMatch(blockIndex, match.Start, match.Length);
                continue;
            }

            if (block is Table table)
            {
                var startCell = step == 0 ? fromTableCell : null;
                var tableMatch = EnumerateTableMatches(table, term, options)
                    .FirstOrDefault(item => IsTableMatchAtOrAfter(item, startCell));
                if (tableMatch.Length > 0)
                    return new FindReplaceMatch(
                        blockIndex, tableMatch.Start, tableMatch.Length,
                        tableMatch.Row, tableMatch.Col, tableMatch.ParagraphIndex);
            }
        }

        if (startBlock >= 0 && document.Blocks[startBlock] is Paragraph startParagraph)
        {
            var startAt = Math.Clamp(fromOffset, 0, startParagraph.PlainText.Length);
            var match = FindAll(startParagraph.PlainText, term, options)
                .FirstOrDefault(item => item.Start < startAt);
            if (match.Length > 0)
                return new FindReplaceMatch(startBlock, match.Start, match.Length);
        }
        else if (startBlock >= 0 && document.Blocks[startBlock] is Table startTable && fromTableCell is not null)
        {
            var wrapMatch = EnumerateTableMatches(startTable, term, options)
                .FirstOrDefault(item => IsTableMatchBefore(item, fromTableCell.Value));
            if (wrapMatch.Length > 0)
                return new FindReplaceMatch(
                    startBlock, wrapMatch.Start, wrapMatch.Length,
                    wrapMatch.Row, wrapMatch.Col, wrapMatch.ParagraphIndex);
        }

        return null;
    }

    public static int CountMatches(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term))
            return 0;

        var effective = NormalizeOptions(options);
        var count = 0;
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    count += FindAll(paragraph.PlainText, term, effective).Count;
                    break;
                case Table table:
                    count += EnumerateTableMatches(table, term, effective).Count();
                    break;
            }
        }

        return count;
    }

    private readonly record struct TableTextMatch(int Row, int Col, int ParagraphIndex, int Start, int Length);

    /// <summary>
    /// Walks a table's cells in row-major, then-column order (matching how a reader/Find Next would
    /// encounter them) and yields every match in every cell paragraph. Deliberately does NOT descend into
    /// <see cref="TableCell.NestedTables"/> -- FreeW's Avalonia editor has no caret/selection model for
    /// placing a cursor inside a nested table cell at all yet, so a match reported there could never be
    /// selected or replaced; that is a separate, larger gap than this one (see FindReplaceDialogPlanner
    /// remarks / the calling shell's Find &amp; Replace notes).
    /// </summary>
    private static IEnumerable<TableTextMatch> EnumerateTableMatches(
        Table table,
        string term,
        FindReplaceSearchOptions options)
    {
        for (var row = 0; row < table.Rows.Count; row++)
        {
            // Yield the GRID-PROJECTED column (TableGridProjection.StartColumn), not the raw
            // TableRow.Cells index -- every consumer (DocumentView.GetCellParagraph via
            // TableGridProjection.StartingAt, FindCellGlyphOffset's PlacedChar.CellCol match, the
            // _cellCaret/_cellAnchor tuple SelectFindReplaceMatch builds) addresses cells by grid
            // column. The two conventions coincide only when every cell in the row has GridSpan == 1;
            // with any horizontally merged cell they diverge, so a raw index here either resolves to
            // the wrong cell (TableGridProjection.StartingAt returns the merged cell that starts
            // earlier) or to no cell at all (a raw index that lands mid-span has no StartColumn match),
            // corrupting or silently dropping the replacement. See TableGridProjectionTests /
            // TableGridProjection.ProjectRow for the projection this must match.
            foreach (var projected in TableGridProjection.ProjectRow(table.Rows[row]))
            {
                var paragraphs = projected.Cell.Paragraphs;
                for (var paraIdx = 0; paraIdx < paragraphs.Count; paraIdx++)
                {
                    foreach (var (start, length) in FindAll(paragraphs[paraIdx].PlainText, term, options))
                        yield return new TableTextMatch(row, projected.StartColumn, paraIdx, start, length);
                }
            }
        }
    }

    private static bool IsTableMatchAtOrAfter(
        TableTextMatch match,
        (int Row, int Col, int ParagraphIndex, int Offset)? from)
    {
        if (from is not { } f)
            return true;
        if (match.Row != f.Row) return match.Row > f.Row;
        if (match.Col != f.Col) return match.Col > f.Col;
        if (match.ParagraphIndex != f.ParagraphIndex) return match.ParagraphIndex > f.ParagraphIndex;
        return match.Start >= f.Offset;
    }

    private static bool IsTableMatchBefore(
        TableTextMatch match,
        (int Row, int Col, int ParagraphIndex, int Offset) from)
    {
        if (match.Row != from.Row) return match.Row < from.Row;
        if (match.Col != from.Col) return match.Col < from.Col;
        if (match.ParagraphIndex != from.ParagraphIndex) return match.ParagraphIndex < from.ParagraphIndex;
        return match.Start < from.Offset;
    }

    private static bool TryValidateSearchTerm(
        [NotNullWhen(true)] string? term,
        out FindReplaceValidationError? error)
    {
        if (FindReplaceDialogPolicy.TryValidateSearchTerm(term, out var sharedError))
        {
            error = null;
            return true;
        }

        error = ToLocalValidationError(sharedError);
        return false;
    }

    private static FindReplaceValidationError ToLocalValidationError(FindReplaceValidationErrorKind? error) =>
        error switch
        {
            FindReplaceValidationErrorKind.SearchTermRequired => FindReplaceValidationError.SearchTermRequired,
            _ => FindReplaceValidationError.SearchTermRequired
        };

    private static FindReplaceValidationErrorKind? ToSharedValidationError(FindReplaceValidationError? error) =>
        error switch
        {
            FindReplaceValidationError.SearchTermRequired => FindReplaceValidationErrorKind.SearchTermRequired,
            _ => FindReplaceValidationErrorKind.SearchTermRequired
        };
}
