using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Shared shell-facing Quick Analysis metadata. Renderers still own controls, focus, and icons; this keeps
/// group identity and title-resource routing in one portable place.
/// </summary>
public static class QuickAnalysisShellPlanner
{
    public static QuickAnalysisShellPlan BuildMenuPlan(
        QuickAnalysisDisplayModel displayModel,
        QuickAnalysisShellCapabilities capabilities,
        GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(displayModel);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (displayModel.IsEmpty)
            return QuickAnalysisShellPlan.Empty;

        var groups = new List<QuickAnalysisShellGroupPlan>();
        foreach (var group in displayModel.Groups)
        {
            var items = new List<QuickAnalysisShellItemPlan>();
            foreach (var item in group.Items)
            {
                items.Add(new QuickAnalysisShellItemPlan(
                    item.Id,
                    item.Group,
                    item.Label,
                    item.PreviewText,
                    QuickAnalysisPreviewIconPlanner.Plan(item.PreviewVisual),
                    QuickAnalysisShellActionPlanner.Plan(item, capabilities),
                    QuickAnalysisPlanner.BuildHoverPreview(selection, item),
                    $"QuickAnalysis_{item.Id}"));
            }

            groups.Add(new QuickAnalysisShellGroupPlan(
                group.Group,
                GroupTitleResourceKey(group.Group),
                GroupTitleFallback(group.Group),
                items));
        }

        return groups.Count == 0 ? QuickAnalysisShellPlan.Empty : new QuickAnalysisShellPlan(groups);
    }

    public static IReadOnlyList<QuickAnalysisOptionGroup> GroupOptions(IReadOnlyList<QuickAnalysisOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var groups = new List<QuickAnalysisOptionGroup>();
        List<QuickAnalysisOption>? currentOptions = null;
        foreach (var option in options)
        {
            if (groups.Count == 0 || groups[^1].Group != option.Group)
            {
                currentOptions = [];
                groups.Add(new QuickAnalysisOptionGroup(option.Group, currentOptions));
            }

            currentOptions!.Add(option);
        }

        return groups;
    }

    public static string GroupTitleResourceKey(QuickAnalysisGroup group) =>
        group switch
        {
            QuickAnalysisGroup.Formatting => "TableLoc_QaGroupFormatting",
            QuickAnalysisGroup.Charts => "TableLoc_QaGroupCharts",
            QuickAnalysisGroup.Totals => "TableLoc_QaGroupTotals",
            QuickAnalysisGroup.Tables => "TableLoc_QaGroupTables",
            QuickAnalysisGroup.Sparklines => "TableLoc_QaGroupSparklines",
            _ => group.ToString()
        };

    public static string GroupTitleFallback(QuickAnalysisGroup group) =>
        group switch
        {
            QuickAnalysisGroup.Formatting => "Formatting",
            QuickAnalysisGroup.Charts => "Charts",
            QuickAnalysisGroup.Totals => "Totals",
            QuickAnalysisGroup.Tables => "Tables",
            QuickAnalysisGroup.Sparklines => "Sparklines",
            _ => group.ToString()
        };
}

public sealed record QuickAnalysisOptionGroup(QuickAnalysisGroup Group, IReadOnlyList<QuickAnalysisOption> Options);

public sealed record QuickAnalysisShellPlan(IReadOnlyList<QuickAnalysisShellGroupPlan> Groups)
{
    public static QuickAnalysisShellPlan Empty { get; } = new([]);

    public bool IsEmpty => Groups.Count == 0;

    public IEnumerable<QuickAnalysisShellItemPlan> AllItems()
    {
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
                yield return item;
        }
    }
}

public sealed record QuickAnalysisShellGroupPlan(
    QuickAnalysisGroup Group,
    string TitleResourceKey,
    string TitleFallback,
    IReadOnlyList<QuickAnalysisShellItemPlan> Items);

public sealed record QuickAnalysisShellItemPlan(
    string Id,
    QuickAnalysisGroup Group,
    string Label,
    string ToolTip,
    QuickAnalysisPreviewIconPlan PreviewIcon,
    QuickAnalysisShellAction Action,
    QuickAnalysisDisplayHoverPreview HoverPreview,
    string AutomationId)
{
    public bool IsSupported => Action.Kind != QuickAnalysisShellActionKind.Deferred;

    public bool IsEnabled => IsSupported || !string.IsNullOrWhiteSpace(Action.DeferredNote);
}
