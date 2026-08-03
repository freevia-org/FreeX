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
/// Non-UI glue backing the Avalonia "Manage Rules" dialog. Filters a rule list to a scope (whole
/// sheet or the current selection), describes them for the list, and edits a working copy in
/// memory. The dialog seeds the working copy from the live sheet with <see cref="CloneAll"/> at
/// open time and only commits it back via a single atomic
/// <see cref="ReplaceAllConditionalFormatsCommand"/> when OK/Apply is clicked — Cancel simply
/// discards the working copy, mirroring the Windows host's manager (which edits a private
/// <c>ObservableCollection&lt;ConditionalFormat&gt;</c> and only pushes it to the real workbook on
/// commit). Pure (no UI), so it is unit testable.
/// </summary>
public static class ConditionalFormatManageModel
{
    /// <summary>Deep-clones every rule, in priority order, to seed the dialog's working copy.</summary>
    public static List<ConditionalFormat> CloneAll(IReadOnlyList<ConditionalFormat> sheetRules)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        return sheetRules.OrderBy(rule => rule.Priority).Select(rule => rule.Clone()).ToList();
    }

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
            .Where(rule => selection is not { } sel || rule.AllRanges.Any(range => RangesOverlap(range, sel)))
            .OrderBy(rule => rule.Priority)
            .Select(rule => new ConditionalFormatRuleListItem(rule, Describe(rule)))
            .ToList();
    }

    /// <summary>
    /// Appends a newly built rule to a rule list (e.g. the Manage Rules dialog's working copy),
    /// returning the reprioritized result.
    /// </summary>
    public static List<ConditionalFormat> AddToWorkingCopy(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat newRule)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(newRule);

        var updated = rules.ToList();
        updated.Add(newRule);
        Reprioritize(updated);
        return updated;
    }

    /// <summary>
    /// Deletes a single rule by id from a rule list (e.g. the Manage Rules dialog's working copy),
    /// returning the reprioritized remainder. Returns <c>null</c> when the id is not present
    /// (nothing to do).
    /// </summary>
    public static List<ConditionalFormat>? DeleteFromWorkingCopy(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.All(rule => rule.Id != ruleId))
            return null;

        var remaining = rules.Where(rule => rule.Id != ruleId).ToList();
        Reprioritize(remaining);
        return remaining;
    }

    /// <summary>
    /// The command that deletes a single rule by id: rebuilds the sheet's rule list without it and
    /// replaces all rules atomically. Returns <c>null</c> when the id is not present (nothing to do).
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildDeleteCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId) =>
        DeleteFromWorkingCopy(sheetRules, ruleId) is { } remaining
            ? new ReplaceAllConditionalFormatsCommand(sheetId, remaining)
            : null;

    /// <summary>
    /// Replaces an edited rule (matched by id) in place within a rule list, returning the
    /// reprioritized result. Returns <c>null</c> when the edited rule's id is not present.
    /// </summary>
    public static List<ConditionalFormat>? ReplaceInWorkingCopy(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat editedRule)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(editedRule);

        var index = IndexOf(rules, editedRule.Id);
        if (index < 0)
            return null;

        var updated = rules.ToList();
        updated[index] = editedRule;
        Reprioritize(updated);
        return updated;
    }

    /// <summary>
    /// The command that commits an edited rule (matched by id) back into the sheet's rule list, then
    /// replaces all rules atomically. Returns <c>null</c> when the edited rule's id is not present.
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildEditCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        ConditionalFormat editedRule) =>
        ReplaceInWorkingCopy(sheetRules, editedRule) is { } updated
            ? new ReplaceAllConditionalFormatsCommand(sheetId, updated)
            : null;

    /// <summary>
    /// Duplicates a rule immediately below the original within a rule list, returning the
    /// reprioritized result. Returns <c>null</c> when the rule id is absent.
    /// </summary>
    public static List<ConditionalFormat>? DuplicateInWorkingCopy(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        Guid newId)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var ordered = rules.OrderBy(rule => rule.Priority).ToList();
        var index = IndexOf(ordered, ruleId);
        if (index < 0)
            return null;

        ordered.Insert(index + 1, CloneRule(ordered[index], newId));
        Reprioritize(ordered);
        return ordered;
    }

    /// <summary>
    /// The command that duplicates a rule immediately below the original, then replaces all rules
    /// atomically. Returns <c>null</c> when the rule id is absent.
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildDuplicateCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId,
        Guid newId) =>
        DuplicateInWorkingCopy(sheetRules, ruleId, newId) is { } ordered
            ? new ReplaceAllConditionalFormatsCommand(sheetId, ordered)
            : null;

    /// <summary>
    /// Moves a rule up or down (swapping it with its neighbour) within a rule list, returning the
    /// reprioritized result. Returns <c>null</c> when the move is a no-op (rule absent, or already at
    /// the boundary in the requested direction).
    /// </summary>
    /// <param name="rules">The full (unfiltered) working copy.</param>
    /// <param name="scope">
    /// The same scope filter passed to <see cref="BuildList"/> for the currently displayed list, or
    /// <c>null</c> to show/move within every rule. "Neighbour" is computed within this filtered
    /// subset — matching what the user actually sees in the dialog's list — not within the full
    /// unfiltered priority order, so a hidden rule (one whose range doesn't overlap <paramref
    /// name="scope"/>) is never silently swapped in front of/behind the moved rule. Only the two
    /// swapped rules' priorities change; every other rule (including hidden ones) keeps its relative
    /// order untouched.
    /// </param>
    public static List<ConditionalFormat>? MoveInWorkingCopy(
        IReadOnlyList<ConditionalFormat> rules,
        GridRange? scope,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var ordered = rules.OrderBy(rule => rule.Priority).ToList();
        var visible = scope is not { } sel
            ? ordered
            : ordered.Where(rule => rule.AllRanges.Any(range => RangesOverlap(range, sel))).ToList();

        var visibleIndex = IndexOf(visible, ruleId);
        if (visibleIndex < 0)
            return null;

        var visibleTarget = direction == ConditionalFormatRuleMoveDirection.Up ? visibleIndex - 1 : visibleIndex + 1;
        if (visibleTarget < 0 || visibleTarget >= visible.Count)
            return null;

        var targetId = visible[visibleTarget].Id;
        var index = IndexOf(ordered, ruleId);
        var target = IndexOf(ordered, targetId);

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        Reprioritize(ordered);
        return ordered;
    }

    /// <summary>
    /// The command that moves a rule up or down (swapping it with its displayed-scope neighbour, see
    /// <see cref="MoveInWorkingCopy"/>), then replaces all rules atomically. Returns <c>null</c> when
    /// the move is a no-op (rule absent, or already at the boundary in the requested direction).
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildMoveCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? scope,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction) =>
        MoveInWorkingCopy(sheetRules, scope, ruleId, direction) is { } ordered
            ? new ReplaceAllConditionalFormatsCommand(sheetId, ordered)
            : null;

    /// <summary>
    /// Changes a rule's applies-to range within a rule list, returning the reprioritized result. The
    /// changed rule is cloned rather than mutated in place, so the rule instance the caller passed in
    /// (e.g. still referenced by a stale list-item snapshot) is left untouched. Returns <c>null</c>
    /// when the rule id is not present (nothing to do).
    /// </summary>
    public static List<ConditionalFormat>? ApplyRangeInWorkingCopy(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        GridRange range)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var index = IndexOf(rules, ruleId);
        if (index < 0)
            return null;

        var updated = rules.ToList();
        var changed = updated[index].Clone();
        changed.AppliesTo = range;
        // The Applies-To editor only ever supplies a single resolved range (it has no UI for
        // multi-area applies-to), so a stale AdditionalRanges from the original rule — copied
        // verbatim by Clone() — must be dropped here, or the rule keeps silently applying to a
        // second area that the user never re-selected and can't even see in the edit box.
        // Mirrors the WPF host's ManageConditionalFormatsDialog Applies-To LostFocus handler,
        // which clears AdditionalRanges the same way when a new single-range text is committed.
        changed.AdditionalRanges = null;
        updated[index] = changed;
        Reprioritize(updated);
        return updated;
    }

    /// <summary>
    /// The command that changes a rule's applies-to range, then replaces all rules atomically.
    /// Returns <c>null</c> when the rule id is not present (nothing to do).
    /// </summary>
    public static ReplaceAllConditionalFormatsCommand? BuildAppliesToCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat> sheetRules,
        Guid ruleId,
        GridRange range) =>
        ApplyRangeInWorkingCopy(sheetRules, ruleId, range) is { } updated
            ? new ReplaceAllConditionalFormatsCommand(sheetId, updated)
            : null;

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
