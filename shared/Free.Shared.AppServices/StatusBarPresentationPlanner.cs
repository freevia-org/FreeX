namespace Free.Shared.AppServices;

public sealed record StatusBarPresentationPlan(
    StatusBarVisibilityPlan Visibility,
    string ReadyText,
    int ZoomPercent,
    string AverageText,
    string CountText,
    string NumericalCountText,
    string SumText,
    string MinimumText,
    string MaximumText,
    string VisibleReadoutText,
    string AutomationText);

public enum StatusBarPresentationElement
{
    ReadyText,
    PageNumberText,
    StatsPanel,
    Average,
    Count,
    NumericalCount,
    Sum,
    Minimum,
    Maximum,
    ViewShortcuts,
    ZoomText,
    ZoomSlider,
    ZoomControls,
    InteractiveControls
}

public readonly record struct StatusBarElementVisibilityPlan(
    StatusBarPresentationElement Element,
    bool IsVisible);

public readonly record struct StatusBarReadoutPresentationPlan(
    StatusBarReadoutKind Kind,
    StatusBarPresentationElement Element,
    string Text,
    string AutomationId,
    string AutomationFallbackResourceKey);

public sealed record StatusBarRendererPlan(
    IReadOnlyList<StatusBarElementVisibilityPlan> VisibilityElements,
    string ReadyText,
    IReadOnlyList<StatusBarReadoutPresentationPlan> ReadoutElements,
    string StatsPanelAutomationText,
    string VisibleReadoutText,
    bool ReadyTextVisible,
    bool VisibleReadoutTextVisible,
    int ZoomPercent)
{
    public bool IsElementVisible(StatusBarPresentationElement element)
    {
        foreach (var entry in VisibilityElements)
        {
            if (entry.Element == element)
                return entry.IsVisible;
        }

        return false;
    }
}

public readonly record struct StatusBarAutomationElementState(
    StatusBarPresentationElement Element,
    string AutomationId,
    string Name,
    string HelpText,
    bool IsVisible);

public sealed record StatusBarAutomationSnapshot(
    IReadOnlyList<StatusBarAutomationElementState> Elements)
{
    public StatusBarAutomationElementState? Find(StatusBarPresentationElement element)
    {
        foreach (var entry in Elements)
        {
            if (entry.Element == element)
                return entry;
        }

        return null;
    }
}

public readonly record struct StatusBarAutomationChange(
    StatusBarAutomationElementState Current,
    string PreviousName,
    bool ShouldNotify);

/// <summary>
/// Owns the deterministic accessible-name state and change policy for status-bar statistics.
/// Renderers only apply the planned properties and translate notifications to their native peers.
/// </summary>
public static class StatusBarAutomationChangePlanner
{
    public const string StatsPanelAutomationId = "StatusStatsPanel";

    public static StatusBarAutomationSnapshot BuildSnapshot(
        StatusBarRendererPlan rendererPlan,
        Func<string, string> resolveResource,
        string statsPanelFallbackName)
    {
        ArgumentNullException.ThrowIfNull(rendererPlan);
        ArgumentNullException.ThrowIfNull(resolveResource);

        var elements = new List<StatusBarAutomationElementState>(rendererPlan.ReadoutElements.Count + 1);
        foreach (var readout in rendererPlan.ReadoutElements)
        {
            var isVisible = rendererPlan.IsElementVisible(readout.Element)
                && !string.IsNullOrWhiteSpace(readout.Text);
            var automationText = isVisible
                ? readout.Text
                : resolveResource(readout.AutomationFallbackResourceKey);
            elements.Add(new StatusBarAutomationElementState(
                readout.Element,
                readout.AutomationId,
                automationText,
                automationText,
                isVisible));
        }

        var panelText = string.IsNullOrWhiteSpace(rendererPlan.StatsPanelAutomationText)
            ? statsPanelFallbackName
            : rendererPlan.StatsPanelAutomationText;
        elements.Add(new StatusBarAutomationElementState(
            StatusBarPresentationElement.StatsPanel,
            StatsPanelAutomationId,
            panelText,
            panelText,
            rendererPlan.VisibleReadoutTextVisible));
        return new StatusBarAutomationSnapshot(elements);
    }

    public static IReadOnlyList<StatusBarAutomationChange> PlanChanges(
        StatusBarAutomationSnapshot? previous,
        StatusBarAutomationSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var changes = new List<StatusBarAutomationChange>(current.Elements.Count);
        foreach (var entry in current.Elements)
        {
            var old = previous?.Find(entry.Element);
            if (old == entry)
                continue;

            changes.Add(new StatusBarAutomationChange(
                entry,
                old?.Name ?? string.Empty,
                old is not null
                    && entry.IsVisible
                    && !string.Equals(old.Value.Name, entry.Name, StringComparison.Ordinal)));
        }

        return changes;
    }
}

public static class StatusBarPresentationPlanner
{
    public static StatusBarPresentationPlan Build(
        StatusBarViewModel model,
        StatusBarOptionVisibility optionVisibility,
        bool hasPageNumberText = false,
        string fallbackAutomationText = "")
    {
        ArgumentNullException.ThrowIfNull(model);

        var visibility = StatusBarVisibilityPlanner.Build(
            model,
            optionVisibility,
            hasPageNumberText,
            fallbackAutomationText);

        return new StatusBarPresentationPlan(
            visibility,
            model.ReadyText,
            model.ZoomPercent,
            ReadoutValue(model, StatusBarReadoutKind.Average),
            ReadoutValue(model, StatusBarReadoutKind.Count),
            ReadoutValue(model, StatusBarReadoutKind.NumericalCount),
            ReadoutValue(model, StatusBarReadoutKind.Sum),
            ReadoutValue(model, StatusBarReadoutKind.Minimum),
            ReadoutValue(model, StatusBarReadoutKind.Maximum),
            visibility.VisibleReadoutText,
            visibility.AutomationText);
    }

    public static StatusBarRendererPlan BuildRendererPlan(StatusBarPresentationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new StatusBarRendererPlan(
            BuildVisibilityElements(plan.Visibility),
            plan.ReadyText,
            BuildReadoutElements(plan),
            plan.AutomationText,
            plan.VisibleReadoutText,
            plan.Visibility.ReadyTextVisible && plan.VisibleReadoutText.Length == 0,
            plan.Visibility.StatsPanelVisible && plan.VisibleReadoutText.Length > 0,
            plan.ZoomPercent);
    }

    public static IReadOnlyList<StatusBarElementVisibilityPlan> BuildVisibilityElements(
        StatusBarVisibilityPlan visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);

        return
        [
            new(StatusBarPresentationElement.ReadyText, visibility.ReadyTextVisible),
            new(StatusBarPresentationElement.PageNumberText, visibility.PageNumberVisible),
            new(StatusBarPresentationElement.StatsPanel, visibility.StatsPanelVisible),
            new(StatusBarPresentationElement.Average, visibility.AverageVisible),
            new(StatusBarPresentationElement.Count, visibility.CountVisible),
            new(StatusBarPresentationElement.NumericalCount, visibility.NumericalCountVisible),
            new(StatusBarPresentationElement.Sum, visibility.SumVisible),
            new(StatusBarPresentationElement.Minimum, visibility.MinimumVisible),
            new(StatusBarPresentationElement.Maximum, visibility.MaximumVisible),
            new(StatusBarPresentationElement.ViewShortcuts, visibility.ViewShortcutsVisible),
            new(StatusBarPresentationElement.ZoomText, visibility.ZoomVisible),
            new(StatusBarPresentationElement.ZoomSlider, visibility.ZoomSliderVisible),
            new(StatusBarPresentationElement.ZoomControls, visibility.ZoomControlsVisible),
            new(StatusBarPresentationElement.InteractiveControls, visibility.InteractiveControlsVisible)
        ];
    }

    public static IReadOnlyList<StatusBarReadoutPresentationPlan> BuildReadoutElements(
        StatusBarPresentationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return
        [
            ReadoutElement(StatusBarReadoutKind.Average, StatusBarPresentationElement.Average, plan.AverageText),
            ReadoutElement(StatusBarReadoutKind.Count, StatusBarPresentationElement.Count, plan.CountText),
            ReadoutElement(StatusBarReadoutKind.NumericalCount, StatusBarPresentationElement.NumericalCount, plan.NumericalCountText),
            ReadoutElement(StatusBarReadoutKind.Sum, StatusBarPresentationElement.Sum, plan.SumText),
            ReadoutElement(StatusBarReadoutKind.Minimum, StatusBarPresentationElement.Minimum, plan.MinimumText),
            ReadoutElement(StatusBarReadoutKind.Maximum, StatusBarPresentationElement.Maximum, plan.MaximumText)
        ];
    }

    public static string ReadoutValue(StatusBarViewModel model, StatusBarReadoutKind kind)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.FindReadout(kind)?.Value ?? string.Empty;
    }

    public static string ReadoutAutomationId(StatusBarReadoutKind kind) => kind switch
    {
        StatusBarReadoutKind.Average => "StatusAvgText",
        StatusBarReadoutKind.Count => "StatusCountText",
        StatusBarReadoutKind.NumericalCount => "StatusNumericalCountText",
        StatusBarReadoutKind.Sum => "StatusSumText",
        StatusBarReadoutKind.Minimum => "StatusMinText",
        StatusBarReadoutKind.Maximum => "StatusMaxText",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static StatusBarReadoutPresentationPlan ReadoutElement(
        StatusBarReadoutKind kind,
        StatusBarPresentationElement element,
        string text) =>
        new(
            kind,
            element,
            text,
            ReadoutAutomationId(kind),
            StatusBarTextResourceKeys.ReadoutLabel(kind));
}
