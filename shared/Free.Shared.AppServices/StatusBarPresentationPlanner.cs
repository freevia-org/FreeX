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

    public static string ReadoutValue(StatusBarViewModel model, StatusBarReadoutKind kind)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.FindReadout(kind)?.Value ?? string.Empty;
    }
}
