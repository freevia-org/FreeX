using System.Diagnostics.CodeAnalysis;

namespace Free.Shared.AppServices;

public enum FindReplaceValidationErrorKind
{
    SearchTermRequired
}

public enum FindReplacePolicyStatusKind
{
    None,
    NoMatches,
    Match,
    NoReplacements,
    Replacements
}

public sealed record FindReplaceNavigationPolicyPlan(
    bool HasMatch,
    int MatchIndex,
    string StatusText,
    FindReplacePolicyStatusKind StatusKind);

public sealed record FindReplaceReplacementPolicyStatus(
    string StatusText,
    FindReplacePolicyStatusKind StatusKind);

public static class FindReplaceDialogPolicy
{
    public const string SearchTermRequiredMessage = "Enter a search term.";
    public const string NoMatchesStatus = "No matches found.";
    public const string NoReplacementsStatus = "No replacements made.";

    public static bool CanRunWithQuery([NotNullWhen(true)] string? query) =>
        !IsSearchTermMissing(query);

    public static bool IsSearchTermMissing(string? query) =>
        string.IsNullOrEmpty(query);

    public static bool TryValidateSearchTerm(
        [NotNullWhen(true)] string? term,
        out FindReplaceValidationErrorKind? error)
    {
        if (IsSearchTermMissing(term))
        {
            error = FindReplaceValidationErrorKind.SearchTermRequired;
            return false;
        }

        error = null;
        return true;
    }

    public static string ValidationMessageFor(FindReplaceValidationErrorKind? error) =>
        error switch
        {
            FindReplaceValidationErrorKind.SearchTermRequired => SearchTermRequiredMessage,
            _ => SearchTermRequiredMessage
        };

    public static string BuildFindStatus(string term, bool found)
    {
        ArgumentNullException.ThrowIfNull(term);
        return found ? string.Empty : BuildNotFoundStatus(term);
    }

    public static string BuildReplaceStatus(string term, bool replaced)
    {
        ArgumentNullException.ThrowIfNull(term);
        return replaced ? string.Empty : BuildNotFoundStatus(term);
    }

    public static string BuildReplaceAllOccurrenceStatus(string term, int replacementCount)
    {
        ArgumentNullException.ThrowIfNull(term);
        return replacementCount == 0
            ? BuildNotFoundStatus(term)
            : $"Replaced {replacementCount} occurrence{(replacementCount == 1 ? "" : "s")}.";
    }

    public static int ReplacementTargetIndex(int currentMatchIndex, int matchCount)
    {
        if (matchCount <= 0)
            return -1;

        return currentMatchIndex >= 0 && currentMatchIndex < matchCount
            ? currentMatchIndex
            : 0;
    }

    public static FindReplaceNavigationPolicyPlan Navigate(
        int currentMatchIndex,
        int matchCount,
        int direction)
    {
        if (matchCount <= 0)
            return new FindReplaceNavigationPolicyPlan(false, -1, NoMatchesStatus, FindReplacePolicyStatusKind.NoMatches);

        var nextIndex = (currentMatchIndex + direction + matchCount) % matchCount;
        return new FindReplaceNavigationPolicyPlan(
            true,
            nextIndex,
            $"Match {nextIndex + 1} of {matchCount}",
            FindReplacePolicyStatusKind.Match);
    }

    public static FindReplaceReplacementPolicyStatus BuildReplacementStatus(int replacementCount) =>
        replacementCount == 0
            ? new FindReplaceReplacementPolicyStatus(NoReplacementsStatus, FindReplacePolicyStatusKind.NoReplacements)
            : new FindReplaceReplacementPolicyStatus(
                $"{replacementCount} replacement(s) made.",
                FindReplacePolicyStatusKind.Replacements);

    public static string BuildNotFoundStatus(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return $"\"{term}\" not found.";
    }
}
