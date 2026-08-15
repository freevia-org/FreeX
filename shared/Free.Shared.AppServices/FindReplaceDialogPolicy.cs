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

public sealed record FindReplacePolicyTextDescriptor(
    ResourceTextDescriptor SearchTermRequired,
    ResourceTextDescriptor NoMatches,
    ResourceTextDescriptor NoReplacements,
    ResourceTextDescriptor NotFoundFormat,
    ResourceTextDescriptor MatchFormat,
    ResourceTextDescriptor ReplacedOccurrencesFormat,
    ResourceTextDescriptor ReplacementsMadeFormat);

public sealed record FindReplacePolicyTextSpec(
    string SearchTermRequired,
    string NoMatches,
    string NoReplacements,
    string NotFoundFormat,
    string MatchFormat,
    string ReplacedOccurrencesFormat,
    string ReplacementsMadeFormat)
{
    public static FindReplacePolicyTextSpec NeutralEnglish { get; } = new(
        "Enter a search term.",
        "No matches found.",
        "No replacements made.",
        "\"{0}\" not found.",
        "Match {0} of {1}",
        "Replaced {0} occurrence{1}.",
        "{0} replacement(s) made.");

    public static FindReplacePolicyTextSpec FromDescriptor(
        FindReplacePolicyTextDescriptor descriptor,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new FindReplacePolicyTextSpec(
            descriptor.SearchTermRequired.Resolve(getText),
            descriptor.NoMatches.Resolve(getText),
            descriptor.NoReplacements.Resolve(getText),
            descriptor.NotFoundFormat.Resolve(getText),
            descriptor.MatchFormat.Resolve(getText),
            descriptor.ReplacedOccurrencesFormat.Resolve(getText),
            descriptor.ReplacementsMadeFormat.Resolve(getText));
    }
}

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

    public static string ValidationMessageFor(
        FindReplaceValidationErrorKind? error,
        FindReplacePolicyTextSpec? text = null) =>
        error switch
        {
            FindReplaceValidationErrorKind.SearchTermRequired => EffectiveText(text).SearchTermRequired,
            _ => EffectiveText(text).SearchTermRequired
        };

    public static string BuildFindStatus(
        string term,
        bool found,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(term);
        return found ? string.Empty : BuildNotFoundStatus(term, text);
    }

    public static string BuildReplaceStatus(
        string term,
        bool replaced,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(term);
        return replaced ? string.Empty : BuildNotFoundStatus(term, text);
    }

    public static string BuildReplaceAllOccurrenceStatus(
        string term,
        int replacementCount,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(term);
        var effectiveText = EffectiveText(text);
        return replacementCount == 0
            ? BuildNotFoundStatus(term, effectiveText)
            : string.Format(
                effectiveText.ReplacedOccurrencesFormat,
                replacementCount,
                replacementCount == 1 ? "" : "s");
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
        int direction,
        FindReplacePolicyTextSpec? text = null)
    {
        var effectiveText = EffectiveText(text);
        if (matchCount <= 0)
            return new FindReplaceNavigationPolicyPlan(false, -1, effectiveText.NoMatches, FindReplacePolicyStatusKind.NoMatches);

        var nextIndex = (currentMatchIndex + direction + matchCount) % matchCount;
        return new FindReplaceNavigationPolicyPlan(
            true,
            nextIndex,
            string.Format(effectiveText.MatchFormat, nextIndex + 1, matchCount),
            FindReplacePolicyStatusKind.Match);
    }

    public static FindReplaceReplacementPolicyStatus BuildReplacementStatus(
        int replacementCount,
        FindReplacePolicyTextSpec? text = null)
    {
        var effectiveText = EffectiveText(text);
        return replacementCount == 0
            ? new FindReplaceReplacementPolicyStatus(effectiveText.NoReplacements, FindReplacePolicyStatusKind.NoReplacements)
            : new FindReplaceReplacementPolicyStatus(
                string.Format(effectiveText.ReplacementsMadeFormat, replacementCount),
                FindReplacePolicyStatusKind.Replacements);
    }

    public static string BuildNotFoundStatus(
        string term,
        FindReplacePolicyTextSpec? text = null)
    {
        ArgumentNullException.ThrowIfNull(term);
        return string.Format(EffectiveText(text).NotFoundFormat, term);
    }

    private static FindReplacePolicyTextSpec EffectiveText(FindReplacePolicyTextSpec? text) =>
        text ?? FindReplacePolicyTextSpec.NeutralEnglish;
}
