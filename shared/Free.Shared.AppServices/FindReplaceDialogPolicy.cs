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

/// <summary>
/// The two-state open mode that every sister app's modeless Find &amp; Replace surface carries.
/// FreeX selects a Find/Replace <c>TabItem</c>, FreeW picks the field that receives initial focus,
/// and FreeP swaps the title plus the replacement row -- but the state itself, and the rule that a
/// live modeless surface can be re-activated into the other mode instead of opening a second
/// window, is the same framework-independent decision in all three.
/// </summary>
public enum FindReplaceOpenMode
{
    Find,
    Replace
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

    /// <summary>
    /// Projects a host's "open in replace mode" boolean onto the shared two-state open mode.
    /// FreeX passes its <c>replaceMode</c> constructor flag, FreeP its <c>showReplace</c> flag.
    /// </summary>
    public static FindReplaceOpenMode OpenModeFor(bool showReplace) =>
        showReplace ? FindReplaceOpenMode.Replace : FindReplaceOpenMode.Find;

    /// <summary>
    /// Whether the replacement field and the Replace / Replace All commands are offered at all.
    /// FreeX collapses both buttons off the Find tab; FreeP hides the replacement row and gates
    /// <c>CanReplace</c>/<c>CanReplaceAll</c> on the same answer.
    /// FreeW deliberately shows both fields in both modes, so it consumes the mode but not this rule.
    /// </summary>
    public static bool ShowsReplaceSurface(FindReplaceOpenMode mode) =>
        mode == FindReplaceOpenMode.Replace;

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
