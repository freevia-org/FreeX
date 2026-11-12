using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Proofing;

/// <summary>
/// The grammar section of the editor's right-click menu, as Word shows it over a blue double
/// underline: the rule name, the suggested corrections, Ignore Once and "Don't Check for This Issue".
/// Renderers append their ordinary editing commands after it.
/// </summary>
public static class ProofingContextMenuPlanner
{
    public const string GrammarRuleHeader = "freew.context.editor.grammar.rule";
    public const string GrammarReplacementPrefix = "freew.context.editor.grammar.replace.";
    public const string GrammarIgnoreOnce = "freew.context.editor.grammar.ignore-once";
    public const string GrammarIgnoreRule = "freew.context.editor.grammar.ignore-rule";

    public static RibbonMenu BuildGrammar(
        ProofingReviewIssue issue,
        bool canEdit,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.Kind != ProofingReviewIssueKind.Grammar)
            return RibbonMenu.Empty;

        var rule = GrammarRuleTextCatalog.Resolve(issue.RuleId, getText);
        var items = new List<RibbonMenuItem>();
        if (rule is not null)
        {
            // An inert caption naming the issue, like the greyed rule name at the top of Word's menu.
            items.Add(new RibbonMenuItem(rule.Title, new RibbonCommandId(GrammarRuleHeader)) { IsEnabled = false });
        }

        for (var index = 0; index < issue.Suggestions.Count; index++)
        {
            items.Add(new RibbonMenuItem(
                ProofingReviewPanePlanner.SuggestionLabel(issue, issue.Suggestions[index], getText),
                new RibbonCommandId(GrammarReplacementPrefix + index))
            {
                IsEnabled = canEdit,
            });
        }

        if (items.Count > 0)
            items.Add(RibbonMenuItem.Separator());

        items.Add(new RibbonMenuItem(
            ProofingReviewPanePlanner.IgnoreOnceLabel(getText),
            new RibbonCommandId(GrammarIgnoreOnce)));
        items.Add(new RibbonMenuItem(
            ProofingReviewPanePlanner.IgnoreRuleLabel(getText),
            new RibbonCommandId(GrammarIgnoreRule))
        {
            IsEnabled = !string.IsNullOrEmpty(issue.RuleId),
        });
        items.Add(RibbonMenuItem.Separator());
        return new RibbonMenu(items);
    }

    /// <summary>The suggestion index encoded in a grammar replacement command id.</summary>
    public static bool TryParseReplacement(RibbonCommandId commandId, out int index)
    {
        index = -1;
        var value = commandId.Value;
        return value.StartsWith(GrammarReplacementPrefix, StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(GrammarReplacementPrefix.Length), out index)
            && index >= 0;
    }
}
