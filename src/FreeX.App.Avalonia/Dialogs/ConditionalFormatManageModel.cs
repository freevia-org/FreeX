using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Dialogs;

/// <summary>One row in the Manage Rules list: a rule plus a human-readable description.</summary>
public sealed record ConditionalFormatRuleListItem(ConditionalFormat Rule, string Description)
{
    public Guid Id => Rule.Id;

    /// <summary>The list shows the description directly (no item template needed).</summary>
    public override string ToString() => Description;
}

/// <summary>
/// Non-UI glue backing the Avalonia "Manage Rules" dialog. Filters the sheet's rules to a scope
/// (whole sheet or the current selection), describes them for the list, and maps edit/delete actions
/// onto the Core atomic <see cref="ReplaceAllConditionalFormatsCommand"/> so the whole edit is a
/// single undo step (mirroring the Windows host's manager). Pure (no UI), so it is unit testable.
/// </summary>
public static class ConditionalFormatManageModel
{
    /// <summary>
    /// The rules to show, in priority order. When <paramref name="selection"/> is supplied, only the
    /// rules whose range overlaps the selection are listed; otherwise every rule on the sheet shows.
    /// </summary>
    public static IReadOnlyList<ConditionalFormatRuleListItem> BuildList(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        return sheetRules
            .Where(rule => selection is not { } sel || RangesOverlap(rule.AppliesTo, sel))
            .OrderBy(rule => rule.Priority)
            .Select(rule => new ConditionalFormatRuleListItem(rule, Describe(rule)))
            .ToList();
    }

    /// <summary>
    /// The command that deletes a single rule by id: rebuilds the sheet's rule list without it and
    /// replaces all rules atomically. Returns <c>null</c> when the id is not present (nothing to do).
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildDeleteCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        if (sheetRules.All(rule => rule.Id != ruleId))
            return null;

        var remaining = sheetRules.Where(rule => rule.Id != ruleId).ToList();
        Reprioritize(remaining);
        return new ReplaceAllConditionalFormatsCommand(sheetId, remaining);
    }

    /// <summary>
    /// The command that commits an edited rule (matched by id) back into the sheet's rule list, then
    /// replaces all rules atomically. Returns <c>null</c> when the edited rule's id is not present.
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildEditCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        ConditionalFormat editedRule)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);
        ArgumentNullException.ThrowIfNull(editedRule);

        var index = IndexOf(sheetRules, editedRule.Id);
        if (index < 0)
            return null;

        var updated = sheetRules.ToList();
        updated[index] = editedRule;
        Reprioritize(updated);
        return new ReplaceAllConditionalFormatsCommand(sheetId, updated);
    }

    /// <summary>
    /// The command that duplicates a rule immediately below the original, then replaces all rules
    /// atomically. Returns <c>null</c> when the rule id is absent.
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildDuplicateCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId,
        Guid newId)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        var ordered = sheetRules.OrderBy(rule => rule.Priority).ToList();
        var index = IndexOf(ordered, ruleId);
        if (index < 0)
            return null;

        ordered.Insert(index + 1, CloneRule(ordered[index], newId));
        Reprioritize(ordered);
        return new ReplaceAllConditionalFormatsCommand(sheetId, ordered);
    }

    /// <summary>
    /// The command that moves a rule up or down in priority order (swapping it with its neighbour),
    /// then replaces all rules atomically. Returns <c>null</c> when the move is a no-op (rule absent,
    /// or already at the boundary in the requested direction).
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildMoveCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        var ordered = sheetRules.OrderBy(rule => rule.Priority).ToList();
        var index = IndexOf(ordered, ruleId);
        if (index < 0)
            return null;

        var target = direction == ConditionalFormatRuleMoveDirection.Up ? index - 1 : index + 1;
        if (target < 0 || target >= ordered.Count)
            return null;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        Reprioritize(ordered);
        return new ReplaceAllConditionalFormatsCommand(sheetId, ordered);
    }

    /// <summary>
    /// The command that changes a rule's applies-to range, then replaces all rules atomically.
    /// Returns <c>null</c> when the rule id is not present (nothing to do).
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildAppliesToCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId,
        GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        var index = IndexOf(sheetRules, ruleId);
        if (index < 0)
            return null;

        var updated = sheetRules.ToList();
        updated[index].AppliesTo = range;
        Reprioritize(updated);
        return new ReplaceAllConditionalFormatsCommand(sheetId, updated);
    }

    /// <summary>A concise one-line description of a rule for the manage list.</summary>
    public static string Describe(ConditionalFormat rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.RuleType switch
        {
            CfRuleType.CellValue => $"Cell Value {DescribeOperator(rule.Operator)} {DescribeOperands(rule)}".TrimEnd(),
            CfRuleType.Formula => $"Formula: ={rule.FormulaText}",
            CfRuleType.Top10 => rule.AboveAverage
                ? $"Top {rule.TopBottomRank}{(rule.TopBottomPercent ? "%" : "")}"
                : $"Bottom {rule.TopBottomRank}{(rule.TopBottomPercent ? "%" : "")}",
            CfRuleType.IconSet => $"Icon Set ({rule.IconSetStyle ?? ConditionalFormatIconSetCatalog.DefaultStyle})",
            CfRuleType.DataBar => "Data Bar",
            CfRuleType.ColorScale => rule.UseThreeColorScale ? "3-Color Scale" : "2-Color Scale",
            CfRuleType.AboveAverage => rule.AboveAverage ? "Above Average" : "Below Average",
            CfRuleType.DuplicateValues => "Duplicate Values",
            CfRuleType.UniqueValues => "Unique Values",
            CfRuleType.ContainsText => $"Text Contains \"{rule.TextRuleText}\"",
            CfRuleType.NotContainsText => $"Text Does Not Contain \"{rule.TextRuleText}\"",
            CfRuleType.BeginsWith => $"Text Begins With \"{rule.TextRuleText}\"",
            CfRuleType.EndsWith => $"Text Ends With \"{rule.TextRuleText}\"",
            CfRuleType.DateOccurring => $"Date Occurring ({rule.DateOccurringPeriod})",
            CfRuleType.Blanks => "Blanks",
            CfRuleType.NoBlanks => "No Blanks",
            CfRuleType.Errors => "Errors",
            CfRuleType.NoErrors => "No Errors",
            _ => rule.RuleType.ToString(),
        };
    }

    private static string DescribeOperator(CfOperator op) =>
        op switch
        {
            CfOperator.Equal => "=",
            CfOperator.NotEqual => "≠",
            CfOperator.GreaterThan => ">",
            CfOperator.GreaterThanOrEqual => "≥",
            CfOperator.LessThan => "<",
            CfOperator.LessThanOrEqual => "≤",
            CfOperator.Between => "between",
            CfOperator.NotBetween => "not between",
            _ => op.ToString(),
        };

    private static string DescribeOperands(ConditionalFormat rule) =>
        rule.Operator is CfOperator.Between or CfOperator.NotBetween
            ? $"{rule.Value1} and {rule.Value2}"
            : rule.Value1 ?? string.Empty;

    private static bool RangesOverlap(GridRange a, GridRange b)
    {
        if (a.Start.Sheet != b.Start.Sheet)
            return false;

        return a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row
            && a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
    }

    private static int IndexOf(IReadOnlyList<ConditionalFormat> rules, Guid ruleId)
    {
        for (var i = 0; i < rules.Count; i++)
            if (rules[i].Id == ruleId)
                return i;

        return -1;
    }

    private static void Reprioritize(IReadOnlyList<ConditionalFormat> rules)
    {
        for (var i = 0; i < rules.Count; i++)
            rules[i].Priority = i + 1;
    }

    private static ConditionalFormat CloneRule(ConditionalFormat source, Guid? id = null) =>
        source.Clone(id);
}
