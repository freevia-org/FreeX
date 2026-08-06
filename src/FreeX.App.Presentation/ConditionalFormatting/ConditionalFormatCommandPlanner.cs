using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

public enum ConditionalFormatStateRefreshPolicy
{
    None,
    WorksheetVisualState
}

public sealed record ConditionalFormatStatusPlan(
    string ResourceKey,
    IReadOnlyList<string> Arguments)
{
    public ConditionalFormatStatusPlan(string resourceKey, params string[] arguments)
        : this(resourceKey, (IReadOnlyList<string>)arguments)
    {
    }
}

public sealed record ConditionalFormatCommandExecutionPlan(
    IWorkbookCommand Command,
    string CommandLabel,
    ConditionalFormatStatusPlan SuccessStatus,
    string FailureResourceKey,
    ConditionalFormatStateRefreshPolicy RefreshPolicy);

/// <summary>
/// Portable command planning for conditional-format quick actions and manager commits. Native hosts
/// own selection acquisition, dialogs, command execution, and repainting; this planner owns command
/// composition, grouped-sheet identity/range remapping, feedback keys, and refresh intent.
/// </summary>
public static class ConditionalFormatCommandPlanner
{
    public const string CommandLabel = "Conditional Formatting";
    public const string ClearRulesCommandLabel = "Clear Conditional Formatting";
    public const string ManageRulesCommandLabel = "Manage Conditional Formatting Rules";
    public const string FailureResourceKey = "InsertLoc_CfFailed";
    public const string InvalidAppliesToResourceKey = "InsertLoc_CfAppliesToInvalid";

    public static ConditionalFormatCommandExecutionPlan PlanApplyPreset(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        ConditionalFormatPreset preset,
        string? value = null)
    {
        var normalizedRanges = RequireRanges(ranges);
        var rule = ConditionalFormatPresetFactory.BuildRule(preset, normalizedRanges[0], value);
        return PlanApplyRule(
            targetSheetIds,
            normalizedRanges,
            rule,
            new ConditionalFormatStatusPlan(
                "InsertLoc_CfAppliedPreset",
                ConditionalFormatPresetFactory.DisplayName(preset),
                FormatStatusRange(normalizedRanges[0])));
    }

    public static ConditionalFormatCommandExecutionPlan PlanApplyIconSet(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        string iconSetStyle)
    {
        var normalizedRanges = RequireRanges(ranges);
        var rule = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput
            {
                RuleType = CfRuleType.IconSet,
                IconSetStyle = ConditionalFormatInputParser.BlankToNull(iconSetStyle)
                    ?? ConditionalFormatIconSetCatalog.DefaultStyle
            },
            normalizedRanges[0]);
        return PlanApplyRule(
            targetSheetIds,
            normalizedRanges,
            rule,
            new ConditionalFormatStatusPlan(
                "InsertLoc_CfAppliedIconSet",
                FormatStatusRange(normalizedRanges[0])));
    }

    public static ConditionalFormatCommandExecutionPlan PlanApplyHighlightGreaterThan(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        string? value)
    {
        var normalizedRanges = RequireRanges(ranges);
        var rule = ConditionalFormatPresetFactory.BuildRule(
            ConditionalFormatPreset.HighlightGreaterThan,
            normalizedRanges[0],
            value);
        return PlanApplyRule(
            targetSheetIds,
            normalizedRanges,
            rule,
            new ConditionalFormatStatusPlan(
                "InsertLoc_CfAppliedHighlight",
                FormatStatusRange(normalizedRanges[0])));
    }

    public static ConditionalFormatCommandExecutionPlan PlanApplyRule(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        ConditionalFormat rule)
    {
        var normalizedRanges = RequireRanges(ranges);
        return PlanApplyRule(
            targetSheetIds,
            normalizedRanges,
            rule,
            new ConditionalFormatStatusPlan(
                "InsertLoc_CfAppliedRule",
                FormatStatusRange(normalizedRanges[0])));
    }

    public static ConditionalFormatCommandExecutionPlan PlanClear(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges)
    {
        var targets = RequireTargets(targetSheetIds);
        var normalizedRanges = RequireRanges(ranges);
        var commands = new List<IWorkbookCommand>(targets.Count * normalizedRanges.Count);
        foreach (var sheetId in targets)
        {
            foreach (var range in normalizedRanges)
            {
                commands.Add(new ClearConditionalFormatsCommand(
                    sheetId,
                    GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId)));
            }
        }

        return CreatePlan(
            ToCommand(ClearRulesCommandLabel, commands),
            ClearRulesCommandLabel,
            new ConditionalFormatStatusPlan(
                "InsertLoc_CfCleared",
                FormatStatusRange(normalizedRanges[0])));
    }

    public static ConditionalFormatCommandExecutionPlan PlanReplaceAll(
        IReadOnlyList<SheetId> targetSheetIds,
        SheetId primarySheetId,
        IReadOnlyList<ConditionalFormat> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var targets = RequireTargets(targetSheetIds);
        var commands = new List<IWorkbookCommand>(targets.Count);
        foreach (var sheetId in targets)
        {
            var preserveIdentity = sheetId == primarySheetId;
            var remapped = rules
                .Select(rule => CloneForSheet(rule, sheetId, preserveIdentity))
                .ToList();
            commands.Add(new ReplaceAllConditionalFormatsCommand(sheetId, remapped));
        }

        return CreatePlan(
            ToCommand(ManageRulesCommandLabel, commands),
            ManageRulesCommandLabel,
            new ConditionalFormatStatusPlan("InsertLoc_CfManageRulesApplied"));
    }

    private static ConditionalFormatCommandExecutionPlan PlanApplyRule(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        ConditionalFormat rule,
        ConditionalFormatStatusPlan status)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var targets = RequireTargets(targetSheetIds);
        var commands = new List<IWorkbookCommand>(targets.Count * ranges.Count);
        var identityPreserved = false;
        foreach (var sheetId in targets)
        {
            foreach (var range in ranges)
            {
                var preserveIdentity = !identityPreserved
                    && sheetId == rule.AppliesTo.Start.Sheet;
                var clone = CloneForSheet(rule, sheetId, preserveIdentity);
                clone.AppliesTo = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
                commands.Add(new ApplyConditionalFormatCommand(sheetId, clone));
                identityPreserved |= preserveIdentity;
            }
        }

        return CreatePlan(ToCommand(CommandLabel, commands), CommandLabel, status);
    }

    private static ConditionalFormatCommandExecutionPlan CreatePlan(
        IWorkbookCommand command,
        string commandLabel,
        ConditionalFormatStatusPlan status) =>
        new(
            command,
            commandLabel,
            status,
            FailureResourceKey,
            ConditionalFormatStateRefreshPolicy.WorksheetVisualState);

    private static ConditionalFormat CloneForSheet(
        ConditionalFormat source,
        SheetId sheetId,
        bool preserveIdentity)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clone = source.Clone(preserveIdentity ? null : Guid.NewGuid());
        clone.AppliesTo = GroupedSheetRangePlanner.RemapRangeToSheet(source.AppliesTo, sheetId);
        clone.AdditionalRanges = source.AdditionalRanges is null
            ? null
            : source.AdditionalRanges
                .Select(range => GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId))
                .ToList();
        return clone;
    }

    private static IReadOnlyList<SheetId> RequireTargets(IReadOnlyList<SheetId> targetSheetIds)
    {
        ArgumentNullException.ThrowIfNull(targetSheetIds);

        var targets = targetSheetIds.Distinct().ToArray();
        if (targets.Length == 0)
            throw new ArgumentException("At least one target sheet is required.", nameof(targetSheetIds));
        return targets;
    }

    private static IReadOnlyList<GridRange> RequireRanges(IReadOnlyList<GridRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (ranges.Count == 0)
            throw new ArgumentException("At least one target range is required.", nameof(ranges));
        return ranges;
    }

    private static IWorkbookCommand ToCommand(
        string label,
        IReadOnlyList<IWorkbookCommand> commands) =>
        commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand(label, commands);

    private static string FormatStatusRange(GridRange range) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(
            range.Start,
            range.End,
            useR1C1ReferenceStyle: false);
}
