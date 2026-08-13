using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

public enum QuickAnalysisShellRequestStatus
{
    Ready,
    MissingSelection,
    MissingSheet,
    UnsupportedSelection,
    NoSuggestions
}

/// <summary>
/// Shared host-entry planner for Quick Analysis. Native shells still own focus, popups, and status text;
/// this keeps the selection-to-display-to-shell-plan decision in one portable place.
/// </summary>
public sealed record QuickAnalysisShellRequestPlan(
    QuickAnalysisShellRequestStatus Status,
    GridRange? Selection,
    QuickAnalysisSelectionDescription? SelectionDescription,
    QuickAnalysisDisplayModel DisplayModel,
    QuickAnalysisShellPlan ShellPlan)
{
    public static QuickAnalysisShellRequestPlan Empty(QuickAnalysisShellRequestStatus status, GridRange? selection = null) =>
        new(status, selection, null, QuickAnalysisDisplayModel.Empty, QuickAnalysisShellPlan.Empty);

    public bool CanOpen => Status == QuickAnalysisShellRequestStatus.Ready && !ShellPlan.IsEmpty;
}

public static class QuickAnalysisShellRequestPlanner
{
    public static QuickAnalysisShellRequestPlan Build(
        Sheet? sheet,
        GridRange? selection,
        QuickAnalysisShellCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (selection is null)
            return QuickAnalysisShellRequestPlan.Empty(QuickAnalysisShellRequestStatus.MissingSelection);

        var range = selection.Value;
        if (sheet is null)
            return QuickAnalysisShellRequestPlan.Empty(QuickAnalysisShellRequestStatus.MissingSheet, range);

        var interpretation = QuickAnalysisSelectionInterpreter.Interpret(sheet, range);
        if (!interpretation.IsEligible || interpretation.Description is not { } description)
            return QuickAnalysisShellRequestPlan.Empty(QuickAnalysisShellRequestStatus.UnsupportedSelection, range);

        var displayModel = QuickAnalysisModelBuilder.Build(description).ToDisplayModel();
        if (displayModel.IsEmpty)
        {
            return new QuickAnalysisShellRequestPlan(
                QuickAnalysisShellRequestStatus.NoSuggestions,
                range,
                description,
                displayModel,
                QuickAnalysisShellPlan.Empty);
        }

        var shellPlan = QuickAnalysisShellPlanner.BuildMenuPlan(displayModel, capabilities, range);
        return new QuickAnalysisShellRequestPlan(
            QuickAnalysisShellRequestStatus.Ready,
            range,
            description,
            displayModel,
            shellPlan);
    }
}
