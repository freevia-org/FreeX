namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Shared shell-facing Quick Analysis metadata. Renderers still own controls, focus, and icons; this keeps
/// group identity and title-resource routing in one portable place.
/// </summary>
public static class QuickAnalysisShellPlanner
{
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
