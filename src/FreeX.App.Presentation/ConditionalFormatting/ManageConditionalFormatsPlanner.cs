using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Portable rule-list planner for the conditional-format manager. Shells own the dialog chrome and
/// range-picking UI; this planner owns the app-neutral edits to rule order, priority, identity, and
/// filtered-scope merge behavior so every shell can share one contract.
/// </summary>
public static class ManageConditionalFormatsPlanner
{
    public static IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection,
        bool filterToSelection,
        IReadOnlyList<ConditionalFormat> editedRules)
    {
        if (!filterToSelection || selection is null)
            return Reprioritize(editedRules);

        var result = new List<ConditionalFormat>();
        var matchingRuleCount = sheetRules.Count(rule => RangesOverlap(rule.AppliesTo, selection.Value));
        var editedRuleIndex = 0;

        foreach (var rule in sheetRules)
        {
            if (!RangesOverlap(rule.AppliesTo, selection.Value))
            {
                result.Add(rule);
                continue;
            }

            matchingRuleCount--;

            if (editedRuleIndex < editedRules.Count)
                result.Add(editedRules[editedRuleIndex++]);

            if (matchingRuleCount == 0)
            {
                while (editedRuleIndex < editedRules.Count)
                    result.Add(editedRules[editedRuleIndex++]);
            }
        }

        while (editedRuleIndex < editedRules.Count)
            result.Add(editedRules[editedRuleIndex++]);

        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> DuplicateRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        Guid? newId = null)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, ruleId);
        if (index < 0)
            return result;

        result.Insert(index + 1, CloneWithPriority(result[index], index + 2, newId ?? Guid.NewGuid()));
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> ReplaceRule(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat editedRule)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, editedRule.Id);
        if (index < 0)
            return result;

        result[index] = CloneWithPriority(editedRule, index + 1);
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> DeleteRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId)
    {
        return Reprioritize(rules.Where(rule => rule.Id != ruleId).ToList());
    }

    public static IReadOnlyList<ConditionalFormat> MoveRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, ruleId);
        if (index < 0)
            return result;

        var target = direction == ConditionalFormatRuleMoveDirection.Up ? index - 1 : index + 1;
        if (target < 0 || target >= result.Count)
            return result;

        (result[index], result[target]) = (result[target], result[index]);
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> ApplyRuleRange(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        GridRange range)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, ruleId);
        if (index < 0)
            return result;

        var updated = CloneWithPriority(result[index], index + 1);
        updated.AppliesTo = range;
        result[index] = updated;
        return result;
    }

    public static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        rules.Select((rule, index) => CloneWithPriority(rule, index + 1)).ToList();

    public static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null)
    {
        var cf = src.Clone(id);
        cf.Priority = priority;
        return cf;
    }

    public static bool RangesOverlap(GridRange a, GridRange b)
    {
        if (a.Start.Sheet != b.Start.Sheet)
            return false;

        return a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row
            && a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
    }

    private static int FindRuleIndex(IReadOnlyList<ConditionalFormat> rules, Guid ruleId)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].Id == ruleId)
                return i;
        }

        return -1;
    }
}
