using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

public sealed record QuickAnalysisPreviewPresentationPlan(
    GridRange? Range,
    QuickAnalysisPreviewVisualKind Visual,
    string? StatusText,
    bool ShouldResetStatus)
{
    public bool IsVisible => Range is not null && Visual != QuickAnalysisPreviewVisualKind.None;
}

/// <summary>
/// Owns the renderer-neutral Quick Analysis shell lifecycle. Native hosts retain popup controls, focus,
/// status controls, dialogs, and visual aftermath while this session owns support, selection, operation
/// dispatch, preview, and status state.
/// </summary>
public sealed class QuickAnalysisShellSession
{
    private bool _preserveOpenIssueStatus;

    public QuickAnalysisShellOpenPlan PlanOpen(
        Sheet? sheet,
        GridRange? selection,
        QuickAnalysisShellCapabilities capabilities)
    {
        var request = QuickAnalysisShellRequestPlanner.Build(sheet, selection, capabilities);
        var openPlan = QuickAnalysisShellOpenPlanner.Plan(request);
        _preserveOpenIssueStatus = !openPlan.CanOpen;
        return openPlan;
    }

    public QuickAnalysisHostOperation? PlanSelection(QuickAnalysisShellItemPlan item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.IsEnabled)
            return null;

        _preserveOpenIssueStatus = false;
        return QuickAnalysisHostOperationPlanner.Plan(item);
    }

    public QuickAnalysisShellItemPlan? FindOpenItem(
        Sheet? sheet,
        GridRange? selection,
        QuickAnalysisShellCapabilities capabilities,
        string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        return PlanOpen(sheet, selection, capabilities)
            .ShellPlan
            .AllItems()
            .SingleOrDefault(item => string.Equals(item.Id, itemId, StringComparison.Ordinal));
    }

    public async Task<bool> ExecuteSelectionAsync(
        QuickAnalysisShellItemPlan item,
        QuickAnalysisOperationHandlers handlers)
    {
        var operation = PlanSelection(item);
        if (operation is null)
            return false;

        await QuickAnalysisOperationExecutor.ExecuteAsync(operation, handlers);
        return true;
    }

    public QuickAnalysisPreviewPresentationPlan PlanPreview(QuickAnalysisShellItemPlan item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.IsEnabled)
            return PlanPreviewClear();

        _preserveOpenIssueStatus = false;
        var preview = item.HoverPreview;
        return new QuickAnalysisPreviewPresentationPlan(
            preview.Range,
            preview.PreviewVisual.Kind,
            preview.StatusText,
            ShouldResetStatus: false);
    }

    public QuickAnalysisPreviewPresentationPlan PlanPreviewClear(bool requestStatusReset = true) =>
        new(
            Range: null,
            QuickAnalysisPreviewVisualKind.None,
            StatusText: null,
            ShouldResetStatus: requestStatusReset && !_preserveOpenIssueStatus);
}
