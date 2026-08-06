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

/// <summary>
/// Identifies the field that receives initial focus when the modeless Find &amp; Replace surface opens.
/// Both desktop hosts consume this shared intent so Ctrl+F and Ctrl+H remain distinguishable.
/// </summary>
public enum FindReplaceDialogOpenMode
{
    Find,
    Replace
}

public readonly record struct FindReplaceOptionChoice(
    FindReplaceOptionKind Kind,
    string Label);

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

public readonly record struct FindReplaceMatch(int Block, int Start, int Length);

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

    private static readonly FindReplaceOptionChoice[] OptionChoiceValues =
    [
        new(FindReplaceOptionKind.MatchCase, "Match case"),
        new(FindReplaceOptionKind.WholeWord, "Whole word"),
        new(FindReplaceOptionKind.UseWildcards, "Use wildcards  (* ? [ ] < >)")
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

    public static string ValidationMessageFor(FindReplaceValidationError? error) =>
        FindReplaceDialogPolicy.ValidationMessageFor(ToSharedValidationError(error));

    public static string BuildFindStatus(FindReplaceSearchRequest request, bool found)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildFindStatus(request.Term, found);
    }

    public static string BuildReplaceStatus(FindReplaceReplaceRequest request, bool replaced)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildReplaceStatus(request.Term, replaced);
    }

    public static string BuildReplaceAllStatus(
        FindReplaceReplaceRequest request,
        int replacementCount,
        bool inSelection = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus(request.Term, replacementCount);
        return inSelection && replacementCount > 0
            ? status[..^1] + " in selection."
            : status;
    }

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

    public static FindReplaceMatch? FindNextMatch(
        TextDocument document,
        string? term,
        FindReplaceSearchOptions options,
        int fromBlock,
        int fromOffset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(term) || document.Blocks.Count == 0)
            return null;

        var startBlock = Math.Clamp(fromBlock, 0, document.Blocks.Count - 1);
        for (var step = 0; step < document.Blocks.Count; step++)
        {
            var blockIndex = (startBlock + step) % document.Blocks.Count;
            if (document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            var startAt = step == 0 ? Math.Clamp(fromOffset, 0, paragraph.PlainText.Length) : 0;
            var match = FindAll(paragraph.PlainText, term, options)
                .FirstOrDefault(item => item.Start >= startAt);
            if (match.Length > 0)
                return new FindReplaceMatch(blockIndex, match.Start, match.Length);
        }

        if (startBlock >= 0 && document.Blocks[startBlock] is Paragraph startParagraph)
        {
            var startAt = Math.Clamp(fromOffset, 0, startParagraph.PlainText.Length);
            var match = FindAll(startParagraph.PlainText, term, options)
                .FirstOrDefault(item => item.Start < startAt);
            if (match.Length > 0)
                return new FindReplaceMatch(startBlock, match.Start, match.Length);
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
            if (block is not Paragraph paragraph)
                continue;

            count += FindAll(paragraph.PlainText, term, effective).Count;
        }

        return count;
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
