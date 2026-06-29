using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

public enum QuickAnalysisShellOpenDecision
{
    Open,
    ShowSelectRangeIssue,
    ShowNoSuggestionsIssue
}

/// <summary>
/// Shared shell-open policy for Quick Analysis. Native hosts still own the popup/dialog/status controls;
/// this keeps request-status interpretation consistent between platform shells.
/// </summary>
public sealed record QuickAnalysisShellOpenPlan(
    QuickAnalysisShellOpenDecision Decision,
    GridRange? Selection,
    QuickAnalysisShellPlan ShellPlan)
{
    public bool CanOpen =>
        Decision == QuickAnalysisShellOpenDecision.Open &&
        Selection is not null &&
        !ShellPlan.IsEmpty;
}

public static class QuickAnalysisShellOpenPlanner
{
    public static QuickAnalysisShellOpenPlan Plan(QuickAnalysisShellRequestPlan request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CanOpen)
        {
            return new QuickAnalysisShellOpenPlan(
                QuickAnalysisShellOpenDecision.Open,
                request.Selection,
                request.ShellPlan);
        }

        return new QuickAnalysisShellOpenPlan(
            request.Status == QuickAnalysisShellRequestStatus.NoSuggestions
                ? QuickAnalysisShellOpenDecision.ShowNoSuggestionsIssue
                : QuickAnalysisShellOpenDecision.ShowSelectRangeIssue,
            request.Selection,
            QuickAnalysisShellPlan.Empty);
    }
}
