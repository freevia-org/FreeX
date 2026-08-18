using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

public enum ManageConditionalFormatsWorkingCopyPolicy
{
    FullSheet,
    CurrentScope
}

public sealed record ManageConditionalFormatRuleProjection(
    ConditionalFormat Rule,
    ManageConditionalFormatRuleDescription Description)
{
    public Guid Id => Rule.Id;
}

/// <summary>
/// Portable working-copy lifecycle for the conditional-format manager. Renderers own controls,
/// focus, and native events; this session owns the rule snapshot, scope projection, mutations, and
/// final atomic replacement.
/// </summary>
public sealed class ManageConditionalFormatsSession
{
    private List<ConditionalFormat> _sourceRules;
    private List<ConditionalFormat> _workingRules;

    public ManageConditionalFormatsSession(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? scope,
        ManageConditionalFormatsWorkingCopyPolicy workingCopyPolicy)
    {
        ArgumentNullException.ThrowIfNull(sheetRules);

        WorkingCopyPolicy = workingCopyPolicy;
        Scope = scope;
        _sourceRules = CloneInPriorityOrder(sheetRules);
        _workingRules = CreateWorkingCopy();
    }

    public ManageConditionalFormatsWorkingCopyPolicy WorkingCopyPolicy { get; }

    public GridRange? Scope { get; private set; }

    public IReadOnlyList<ConditionalFormat> WorkingRules => _workingRules;

    public IReadOnlyList<ConditionalFormat> VisibleRules =>
        WorkingCopyPolicy == ManageConditionalFormatsWorkingCopyPolicy.CurrentScope
            ? _workingRules
            : FilterToScope(_workingRules, Scope);

    public IReadOnlyList<ManageConditionalFormatRuleProjection> BuildProjection() =>
        VisibleRules
            .Select(rule => new ManageConditionalFormatRuleProjection(
                rule,
                ManageConditionalFormatsPlanner.DescribeRule(rule)))
            .ToList();

    /// <summary>
    /// Changes the displayed scope. Matches Excel's Rules Manager: switching "Show formatting rules
    /// for" never discards edits made earlier in the same dialog session -- only Cancel (or closing
    /// the dialog without committing) does that. Current-scope sessions therefore fold any pending
    /// (not-yet-applied) edits back into the accumulated snapshot -- using the same scope-merge logic
    /// as <see cref="BuildResultRules"/> -- before re-deriving the working copy for the new scope, so
    /// edits made under an earlier scope survive any number of further scope switches. Full-sheet
    /// sessions already retain all buffered edits and only change their projection.
    /// </summary>
    public void SetScope(GridRange? scope, IReadOnlyList<ConditionalFormat>? currentSheetRules = null)
    {
        if (WorkingCopyPolicy == ManageConditionalFormatsWorkingCopyPolicy.CurrentScope)
            _sourceRules = BuildResultRules(ReconcileWithTrackedSnapshot(currentSheetRules)).ToList();

        Scope = scope;

        if (WorkingCopyPolicy != ManageConditionalFormatsWorkingCopyPolicy.CurrentScope)
            return;

        _workingRules = CreateWorkingCopy();
    }

    /// <summary>
    /// Folding pending edits back into the tracked snapshot must never regress to the caller's raw
    /// live-sheet rules -- the caller (e.g. the WPF dialog) supplies the *unedited* live sheet on
    /// every scope change, so blindly using it as the merge base would silently discard edits made
    /// under an earlier scope (the exact data-loss bug this fold-in exists to prevent). Only adopt
    /// the caller's rules as the new baseline when the rule set actually changed underneath the
    /// session (e.g. the live sheet gained/lost rules via the dialog's own Apply button, or an
    /// external command) -- detected by comparing rule identity, not values -- otherwise keep
    /// merging onto the session's own accumulated snapshot.
    /// </summary>
    private List<ConditionalFormat>? ReconcileWithTrackedSnapshot(IReadOnlyList<ConditionalFormat>? currentSheetRules)
    {
        if (currentSheetRules is null)
            return null;

        var trackedIds = new HashSet<Guid>(_sourceRules.Select(rule => rule.Id));
        var unchanged = currentSheetRules.Count == trackedIds.Count
            && currentSheetRules.All(rule => trackedIds.Contains(rule.Id));
        return unchanged ? null : currentSheetRules.ToList();
    }

    public void Add(ConditionalFormat newRule)
    {
        ArgumentNullException.ThrowIfNull(newRule);

        _workingRules = ManageConditionalFormatsPlanner.AddRule(_workingRules, newRule).ToList();
    }

    public bool Replace(ConditionalFormat editedRule)
    {
        ArgumentNullException.ThrowIfNull(editedRule);
        if (FindRule(editedRule.Id) is null)
            return false;

        _workingRules = ManageConditionalFormatsPlanner.ReplaceRule(_workingRules, editedRule).ToList();
        return true;
    }

    public bool Delete(Guid ruleId)
    {
        if (FindRule(ruleId) is null)
            return false;

        _workingRules = ManageConditionalFormatsPlanner.DeleteRule(_workingRules, ruleId).ToList();
        return true;
    }

    public bool Duplicate(Guid ruleId, Guid newId)
    {
        if (FindRule(ruleId) is null)
            return false;

        _workingRules = ManageConditionalFormatsPlanner.DuplicateRule(_workingRules, ruleId, newId).ToList();
        return true;
    }

    public bool Move(Guid ruleId, ConditionalFormatRuleMoveDirection direction)
    {
        var visible = VisibleRules;
        var index = FindRuleIndex(visible, ruleId);
        if (index < 0)
            return false;

        var target = direction == ConditionalFormatRuleMoveDirection.Up ? index - 1 : index + 1;
        if (target < 0 || target >= visible.Count)
            return false;

        var moveScope = WorkingCopyPolicy == ManageConditionalFormatsWorkingCopyPolicy.FullSheet
            ? Scope
            : null;
        _workingRules = ManageConditionalFormatsPlanner.MoveRule(
            _workingRules,
            moveScope,
            ruleId,
            direction).ToList();
        return true;
    }

    public bool ApplyRange(Guid ruleId, GridRange range)
    {
        if (FindRule(ruleId) is null)
            return false;

        _workingRules = ManageConditionalFormatsPlanner.ApplyRuleRange(_workingRules, ruleId, range).ToList();
        return true;
    }

    public bool SetStopIfTrue(Guid ruleId, bool value)
    {
        var rule = FindRule(ruleId);
        if (rule is null)
            return false;

        rule.StopIfTrue = value;
        return true;
    }

    public IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat>? currentSheetRules = null)
    {
        if (WorkingCopyPolicy == ManageConditionalFormatsWorkingCopyPolicy.FullSheet)
            return ManageConditionalFormatsPlanner.Reprioritize(_workingRules);

        var sourceRules = currentSheetRules ?? _sourceRules;
        return ManageConditionalFormatsPlanner.BuildResultRules(
            sourceRules,
            Scope,
            filterToSelection: Scope is not null,
            _workingRules);
    }

    public ReplaceAllConditionalFormatsCommand CreateApplyCommand(
        SheetId sheetId,
        IReadOnlyList<ConditionalFormat>? currentSheetRules = null) =>
        new(sheetId, BuildResultRules(currentSheetRules));

    public ConditionalFormatCommandExecutionPlan CreateApplyPlan(
        IReadOnlyList<SheetId> targetSheetIds,
        SheetId primarySheetId,
        IReadOnlyList<ConditionalFormat>? currentSheetRules = null) =>
        ConditionalFormatCommandPlanner.PlanReplaceAll(
            targetSheetIds,
            primarySheetId,
            BuildResultRules(currentSheetRules));

    private List<ConditionalFormat> CreateWorkingCopy()
    {
        var source = WorkingCopyPolicy == ManageConditionalFormatsWorkingCopyPolicy.FullSheet
            ? _sourceRules
            : FilterToScope(_sourceRules, Scope);
        return CloneInPriorityOrder(source);
    }

    private static List<ConditionalFormat> CloneInPriorityOrder(IReadOnlyList<ConditionalFormat> rules) =>
        rules
            .OrderBy(rule => rule.Priority)
            .Select((rule, index) => ManageConditionalFormatsPlanner.CloneWithPriority(rule, index + 1))
            .ToList();

    private static List<ConditionalFormat> FilterToScope(
        IReadOnlyList<ConditionalFormat> rules,
        GridRange? scope) =>
        rules
            .Where(rule => scope is not { } range
                || rule.AllRanges.Any(candidate => ManageConditionalFormatsPlanner.RangesOverlap(candidate, range)))
            .OrderBy(rule => rule.Priority)
            .ToList();

    private ConditionalFormat? FindRule(Guid ruleId) =>
        _workingRules.FirstOrDefault(rule => rule.Id == ruleId);

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
