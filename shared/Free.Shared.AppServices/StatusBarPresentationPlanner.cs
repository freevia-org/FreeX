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
