namespace FreeX.App.Presentation.Filtering;

/// <summary>Reusable resource-key decisions for AutoFilter validation and command outcomes.</summary>
public static class WorksheetFilterMessagePlanner
{
    public static string GetPlanErrorResourceKey(WorksheetFilterMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Error switch
        {
            WorksheetFilterMutationError.InvalidCriteria => GetPromptErrorResourceKey(plan.PromptError),
            WorksheetFilterMutationError.SelectionRequired => "MainWindowMessage_FilterSelectAtLeastOneItem",
            _ => "MainWindowMessage_FilterUnsupportedCriterion"
        };
    }

    public static string GetPromptErrorResourceKey(FilterPromptPlanError error) =>
        error switch
        {
            FilterPromptPlanError.TopBottomSyntax => "FilterPrompt_ErrorTopBottomSyntax",
            FilterPromptPlanError.PercentageRange => "FilterPrompt_ErrorPercentageRange",
            FilterPromptPlanError.PositiveItemCount => "FilterPrompt_ErrorPositiveItemCount",
            FilterPromptPlanError.CompositeSyntax => "FilterPrompt_ErrorCompositeSyntax",
            FilterPromptPlanError.DateBetweenSyntax => "FilterPrompt_ErrorDateBetweenSyntax",
            FilterPromptPlanError.BetweenSyntax => "FilterPrompt_ErrorBetweenSyntax",
            FilterPromptPlanError.TextToMatch => "FilterPrompt_ErrorTextToMatch",
            FilterPromptPlanError.ComparisonNumber => "FilterPrompt_ErrorComparisonNumber",
            FilterPromptPlanError.DateFormat => "FilterPrompt_ErrorDateFormat",
            _ => "MainWindowMessage_FilterUnsupportedCriterion"
        };

    public static string GetCommandFailureResourceKey(WorksheetFilterMutationKind kind) =>
        kind is WorksheetFilterMutationKind.SortAscending or
            WorksheetFilterMutationKind.SortDescending or
            WorksheetFilterMutationKind.SortByColor
                ? "ShellLoc_SortFailed"
                : "ShellLoc_FilterFailed";

    public static string GetSuccessResourceKey(WorksheetFilterMutationKind kind) =>
        kind switch
        {
            WorksheetFilterMutationKind.ClearFilter => "ShellLoc_ClearedFilter",
            WorksheetFilterMutationKind.SortAscending => "ShellLoc_SortedAToZ",
            WorksheetFilterMutationKind.SortDescending => "ShellLoc_SortedZToA",
            WorksheetFilterMutationKind.SortByColor => "ShellLoc_SortedByColor",
            _ => "ShellLoc_AppliedFilter"
        };
}
