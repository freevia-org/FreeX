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
    public const string DialogAutomationId = "ErrorCheckingDialog";
    public const string IssuesAutomationId = "ErrorCheckingIssuesList";

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

    public const double Width = 720;
    public const double Height = 420;
    // WPF's 720x420 value is the outer dialog frame. Avalonia's borderless client
    // surface must use the same client rectangle so its content does not consume
    // the native frame reserve that WPF leaves empty in parity captures.
    public const double AvaloniaClientWidth = 704;
    public const double AvaloniaClientHeight = 383;
    public const double MinWidth = 540;
    public const double MinHeight = 240;
    public const double RootMargin = 10;
    public const double ActionPanelWidth = 180;
    public const double ButtonHeight = 26;
    public const double GoToButtonWidth = 80;
    public const double PreviousButtonWidth = 84;
    public const double NextButtonWidth = 80;
    public const double IgnoreButtonWidth = 104;
    public const double TraceButtonWidth = 96;
    public const double OptionsButtonWidth = 92;
    public const double CloseButtonWidth = 80;
    public const double SheetColumnWidth = 110;
    public const double CellColumnWidth = 70;
    public const double IssueColumnWidth = 80;
    public const double FormulaColumnWidth = 150;
    public const double DescriptionColumnWidth = 260;

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
