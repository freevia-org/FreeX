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

public enum FindReplaceValidationError
{
    SearchTermRequired
}

public readonly record struct FindReplaceOptionChoice(
    FindReplaceOptionKind Kind,
    string Label);

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

public static class FindReplaceDialogPlanner
{
    public const string SearchTermRequiredMessage = FindReplaceDialogPolicy.SearchTermRequiredMessage;

    private static readonly FindReplaceOptionChoice[] OptionChoiceValues =
    [
        new(FindReplaceOptionKind.MatchCase, "Match case"),
        new(FindReplaceOptionKind.WholeWord, "Whole word"),
        new(FindReplaceOptionKind.UseWildcards, "Use wildcards  (* ? [ ] < >)")
    ];

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

    public static string BuildReplaceAllStatus(FindReplaceReplaceRequest request, int replacementCount)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindReplaceDialogPolicy.BuildReplaceAllOccurrenceStatus(request.Term, replacementCount);
    }

    public static bool DocumentContains(TextDocument document, FindReplaceSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CountMatches(document, request.Term, request.Options) > 0;
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

            count += TextSearch
                .FindAll(
                    paragraph.PlainText,
                    term,
                    effective.MatchCase,
                    effective.WholeWord,
                    effective.UseWildcards)
                .Count();
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
