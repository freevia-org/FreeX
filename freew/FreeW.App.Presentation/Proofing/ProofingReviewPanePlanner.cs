using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Proofing;

public sealed record ProofingReviewPaneAction(string Label, bool IsVisible, bool IsEnabled);

/// <summary>Everything a renderer needs to draw the Spelling &amp; Grammar pane for one session state.</summary>
public sealed record ProofingReviewPanePlan(
    string Title,
    bool ShowIssue,
    string Heading,
    string IssueText,
    string Explanation,
    string SuggestionsLabel,
    IReadOnlyList<string> Suggestions,
    string StatusText,
    ProofingReviewPaneAction Change,
    ProofingReviewPaneAction ChangeAll,
    ProofingReviewPaneAction IgnoreOnce,
    ProofingReviewPaneAction IgnoreAll,
    ProofingReviewPaneAction AddToDictionary,
    ProofingReviewPaneAction Close);

/// <summary>
/// Localized, renderer-neutral projection of <see cref="ProofingReviewSession"/>. WPF and Avalonia panes
/// only lay these strings and enablement flags out; they decide nothing themselves.
/// </summary>
public static class ProofingReviewPanePlanner
{
    private static readonly ResourceTextDescriptor Title = new("Proofing_Review_Title", "Spelling & Grammar");
    private static readonly ResourceTextDescriptor SpellingHeading = new("Proofing_Review_SpellingHeading", "Not in Dictionary");
    private static readonly ResourceTextDescriptor SpellingExplanation = new("Proofing_Review_SpellingExplanation", "This word was not found in the dictionary.");
    private static readonly ResourceTextDescriptor GrammarHeading = new("Proofing_Review_GrammarHeading", "Grammar");
    private static readonly ResourceTextDescriptor SuggestionsLabel = new("Proofing_Review_SuggestionsLabel", "Suggestions");
    private static readonly ResourceTextDescriptor NoSuggestions = new("Proofing_Review_NoSuggestions", "(No suggestions)");
    private static readonly ResourceTextDescriptor Change = new("Proofing_Review_Change", "Change");
    private static readonly ResourceTextDescriptor ChangeAll = new("Proofing_Review_ChangeAll", "Change All");
    private static readonly ResourceTextDescriptor IgnoreOnce = new("Proofing_Review_IgnoreOnce", "Ignore Once");
    private static readonly ResourceTextDescriptor IgnoreAll = new("Proofing_Review_IgnoreAll", "Ignore All");
    private static readonly ResourceTextDescriptor IgnoreRule = new("Proofing_Review_IgnoreRule", "Don't Check for This Issue");
    private static readonly ResourceTextDescriptor AddToDictionary = new("Proofing_Review_AddToDictionary", "Add to Dictionary");
    private static readonly ResourceTextDescriptor Close = new("Proofing_Review_Close", "Close");
    private static readonly ResourceTextDescriptor Complete = new("Proofing_Review_Complete", "Spelling and grammar check is complete.");
    private static readonly ResourceTextDescriptor Idle = new("Proofing_Review_Idle", "Press F7 to check spelling and grammar.");
    private static readonly ResourceTextDescriptor ChangeFailed = new("Proofing_Review_ChangeFailed", "This change can't be made because the text is protected or has changed.");
    private static readonly ResourceTextDescriptor DeleteFix = new("Proofing_Review_DeleteFix", "Delete");

    private static readonly ResourceTextDescriptor[] PaneTexts =
    [
        Title, SpellingHeading, SpellingExplanation, GrammarHeading, SuggestionsLabel, NoSuggestions, Change,
        ChangeAll, IgnoreOnce, IgnoreAll, IgnoreRule, AddToDictionary, Close, Complete, Idle, ChangeFailed,
        DeleteFix,
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        PaneTexts.Select(text => text.ResourceKey)
            .Concat(GrammarRuleTextCatalog.RequiredResourceKeys)
            .ToArray();

    /// <summary>Word's completion message, shown when the walk-through has visited every issue.</summary>
    public static string CompleteMessage(Func<string, string?>? getText = null) => Complete.Resolve(getText);

    public static ProofingReviewPanePlan Build(
        ProofingReviewSession session,
        Func<string, string?>? getText = null,
        bool lastChangeFailed = false)
    {
        ArgumentNullException.ThrowIfNull(session);

        var issue = session.Current;
        var reviewing = session.IsReviewing && issue is not null;
        var isSpelling = reviewing && issue!.Kind == ProofingReviewIssueKind.Spelling;
        var suggestions = reviewing
            ? issue!.Suggestions.Select(suggestion => SuggestionLabel(issue, suggestion, getText)).ToArray()
            : [];

        string heading;
        string explanation;
        if (!reviewing)
        {
            heading = string.Empty;
            explanation = string.Empty;
        }
        else if (isSpelling)
        {
            heading = SpellingHeading.Resolve(getText);
            explanation = SpellingExplanation.Resolve(getText);
        }
        else
        {
            var rule = GrammarRuleTextCatalog.Resolve(issue!.RuleId, getText);
            heading = rule?.Title ?? GrammarHeading.Resolve(getText);
            explanation = rule?.Explanation ?? string.Empty;
        }

        var status = session.Status switch
        {
            ProofingReviewStatus.Completed => Complete.Resolve(getText),
            ProofingReviewStatus.Idle => Idle.Resolve(getText),
            _ when lastChangeFailed => ChangeFailed.Resolve(getText),
            _ when suggestions.Length == 0 => NoSuggestions.Resolve(getText),
            _ => string.Empty,
        };

        return new ProofingReviewPanePlan(
            Title.Resolve(getText),
            reviewing,
            heading,
            reviewing ? issue!.Text : string.Empty,
            explanation,
            SuggestionsLabel.Resolve(getText),
            suggestions,
            status,
            new ProofingReviewPaneAction(Change.Resolve(getText), reviewing, session.CanChange),
            new ProofingReviewPaneAction(ChangeAll.Resolve(getText), isSpelling, session.CanChangeAll),
            new ProofingReviewPaneAction(IgnoreOnce.Resolve(getText), reviewing, session.CanIgnoreOnce),
            new ProofingReviewPaneAction(
                (isSpelling || !reviewing ? IgnoreAll : IgnoreRule).Resolve(getText),
                reviewing,
                session.CanIgnoreAll),
            new ProofingReviewPaneAction(AddToDictionary.Resolve(getText), isSpelling, session.CanAddToDictionary),
            new ProofingReviewPaneAction(Close.Resolve(getText), true, true));
    }

    /// <summary>The text shown for one suggestion; a deletion has no replacement text to show.</summary>
    public static string SuggestionLabel(
        ProofingReviewIssue issue,
        ProofingReviewSuggestion suggestion,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(suggestion);
        if (suggestion.Replacement.Length > 0)
            return suggestion.Replacement;

        return GrammarRuleTextCatalog.Resolve(issue.RuleId, getText)?.DeleteFix
            ?? DeleteFix.Resolve(getText);
    }
}

public sealed record GrammarRuleText(string RuleId, string Title, string Explanation, string? DeleteFix);

/// <summary>Localized title and explanation of every deterministic grammar rule.</summary>
public static class GrammarRuleTextCatalog
{
    private sealed record Entry(
        string RuleId,
        ResourceTextDescriptor Title,
        ResourceTextDescriptor Explanation,
        ResourceTextDescriptor? DeleteFix = null);

    private static readonly Entry[] Entries =
    [
        new(GrammarRuleIds.RepeatedWord,
            new("Grammar_RepeatedWord_Title", "Repeated Word"),
            new("Grammar_RepeatedWord_Explanation", "The same word appears twice in a row. Consider deleting the repetition."),
            new("Grammar_RepeatedWord_Fix", "Delete Repeated Word")),
        new(GrammarRuleIds.SentenceCapitalization,
            new("Grammar_SentenceCapitalization_Title", "Capitalization"),
            new("Grammar_SentenceCapitalization_Explanation", "A sentence should begin with a capital letter.")),
        new(GrammarRuleIds.MultipleSpaces,
            new("Grammar_MultipleSpaces_Title", "Extra Space"),
            new("Grammar_MultipleSpaces_Explanation", "There is more than one space between these words. Consider using a single space.")),
        new(GrammarRuleIds.SpaceBeforePunctuation,
            new("Grammar_SpaceBeforePunctuation_Title", "Space Before Punctuation"),
            new("Grammar_SpaceBeforePunctuation_Explanation", "Punctuation marks follow the preceding word without a space.")),
        new(GrammarRuleIds.MissingSpaceAfterPunctuation,
            new("Grammar_MissingSpaceAfterPunctuation_Title", "Missing Space"),
            new("Grammar_MissingSpaceAfterPunctuation_Explanation", "A space is expected after this punctuation mark.")),
        new(GrammarRuleIds.IndefiniteArticle,
            new("Grammar_IndefiniteArticle_Title", "A or An"),
            new("Grammar_IndefiniteArticle_Explanation", "Use \"a\" before a consonant sound and \"an\" before a vowel sound.")),
        new(GrammarRuleIds.RepeatedPunctuation,
            new("Grammar_RepeatedPunctuation_Title", "Repeated Punctuation"),
            new("Grammar_RepeatedPunctuation_Explanation", "This punctuation mark is repeated. Consider using it once.")),
        new(GrammarRuleIds.UnpairedBracket,
            new("Grammar_UnpairedBracket_Title", "Unpaired Bracket"),
            new("Grammar_UnpairedBracket_Explanation", "This bracket has no matching opening or closing bracket in the paragraph.")),
        new(GrammarRuleIds.UnpairedQuote,
            new("Grammar_UnpairedQuote_Title", "Unpaired Quotation Mark"),
            new("Grammar_UnpairedQuote_Explanation", "This quotation mark has no matching partner in the paragraph.")),
        new(GrammarRuleIds.CommonConfusion,
            new("Grammar_CommonConfusion_Title", "Commonly Confused Words"),
            new("Grammar_CommonConfusion_Explanation", "This phrase is often written by mistake. Consider the suggested wording.")),
    ];

    public static IReadOnlyList<string> RuleIds => Entries.Select(entry => entry.RuleId).ToArray();

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Entries
            .SelectMany(entry => new[] { entry.Title, entry.Explanation, entry.DeleteFix })
            .Where(descriptor => descriptor is not null)
            .Select(descriptor => descriptor!.ResourceKey)
            .ToArray();

    public static GrammarRuleText? Resolve(string? ruleId, Func<string, string?>? getText = null)
    {
        var entry = Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.RuleId, ruleId, StringComparison.Ordinal));
        return entry is null
            ? null
            : new GrammarRuleText(
                entry.RuleId,
                entry.Title.Resolve(getText),
                entry.Explanation.Resolve(getText),
                entry.DeleteFix?.Resolve(getText));
    }
}
