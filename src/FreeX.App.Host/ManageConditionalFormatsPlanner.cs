using FreeX.Core.Model;
using ConditionalFormatRuleMoveDirection = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatRuleMoveDirection;
using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ManageConditionalFormatsPlanner;

namespace FreeX.App.Host;

public static class ManageConditionalFormatsPlanner
{
    public static IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection,
        bool filterToSelection,
        IReadOnlyList<ConditionalFormat> editedRules) =>
        PresentationPlanner.BuildResultRules(sheetRules, selection, filterToSelection, editedRules);

    public static IReadOnlyList<ConditionalFormat> DuplicateRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        Guid? newId = null) =>
        PresentationPlanner.DuplicateRule(rules, ruleId, newId);

    public static IReadOnlyList<ConditionalFormat> ReplaceRule(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat editedRule) =>
        PresentationPlanner.ReplaceRule(rules, editedRule);

    public static IReadOnlyList<ConditionalFormat> DeleteRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId) =>
        PresentationPlanner.DeleteRule(rules, ruleId);

    public static IReadOnlyList<ConditionalFormat> MoveRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction) =>
        PresentationPlanner.MoveRule(rules, ruleId, direction);

    public static IReadOnlyList<ConditionalFormat> ApplyRuleRange(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        GridRange range) =>
        PresentationPlanner.ApplyRuleRange(rules, ruleId, range);

    public static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        PresentationPlanner.Reprioritize(rules);

    public static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null) =>
        PresentationPlanner.CloneWithPriority(src, priority, id);

    public static bool RangesOverlap(GridRange a, GridRange b) =>
        PresentationPlanner.RangesOverlap(a, b);
}
