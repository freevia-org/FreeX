using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record ErrorCheckingCommandState(
    bool HasSelection,
    bool CanShowCalculationSteps,
    bool CanPrevious,
    bool CanNext);

public static class ErrorCheckingDialogPlanner
{
    public const string TitleKey = "ErrorChecking_Title";
    public const string HelpGroupHeaderKey = "ErrorChecking_HelpGroupHeader";
    public const string ActionIntroTextKey = "ErrorChecking_ActionIntroText";
    public const string HelpButtonKey = "ErrorChecking_HelpButton";
    public const string ShowCalculationStepsButtonKey = "ErrorChecking_ShowCalculationStepsButton";
    public const string IgnoreErrorButtonKey = "ErrorChecking_IgnoreErrorButton";
    public const string EditInFormulaBarButtonKey = "ErrorChecking_EditInFormulaBarButton";
    public const string GoToButtonKey = "ErrorChecking_GoToButton";
    public const string PreviousButtonKey = "ErrorChecking_PreviousButton";
    public const string NextButtonKey = "ErrorChecking_NextButton";
    public const string TraceErrorButtonKey = "ErrorChecking_TraceErrorButton";
    public const string OptionsButtonKey = "ErrorChecking_OptionsButton";
    public const string CloseButtonKey = "ErrorChecking_CloseButton";
    public const string IssuesAutomationNameKey = "ErrorChecking_IssuesAutomationName";
    public const string IssuesLabelKey = "ErrorChecking_IssuesLabel";
    public const string SheetColumnHeaderKey = "ErrorChecking_SheetColumnHeader";
    public const string CellColumnHeaderKey = "ErrorChecking_CellColumnHeader";
    public const string IssueColumnHeaderKey = "ErrorChecking_IssueColumnHeader";
    public const string FormulaColumnHeaderKey = "ErrorChecking_FormulaColumnHeader";
    public const string DescriptionColumnHeaderKey = "ErrorChecking_DescriptionColumnHeader";
    public const string IssueCountHeaderKey = "ErrorChecking_IssueCountHeader";
    public const string SelectedIssueHelpBodyKey = "ErrorChecking_SelectedIssueHelpBody";
    public const string NoSelectionHelpBodyKey = "ErrorChecking_NoSelectionHelpBody";
    public const string HelpTitleKey = "ErrorChecking_HelpTitle";
    public const string NoIssuesMessageKey = "MainWindowMessage_ErrorCheckingNoIssues";
    public const string NoIssuesTitleKey = "MainWindowMessage_ErrorCheckingTitle";

    public static ErrorCheckingCommandState CreateCommandState(
        int selectedIndex,
        int issueCount,
        FormulaErrorIssue? selectedIssue)
    {
        var hasSelection = selectedIndex >= 0 && selectedIndex < issueCount && selectedIssue is not null;
        return new ErrorCheckingCommandState(
            hasSelection,
            hasSelection && HasCalculationSteps(selectedIssue!),
            hasSelection && selectedIndex > 0,
            hasSelection && selectedIndex < issueCount - 1);
    }

    public static bool HasCalculationSteps(FormulaErrorIssue issue) =>
        !string.IsNullOrWhiteSpace(issue.FormulaText);

}
