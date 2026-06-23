namespace Free.Shared.AppServices;

public static class WorkbookProgressTextFormatter
{
    public static WorkbookProgressText FormatOpen(
        WorkbookOpenProgressUpdate update,
        Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(update);

        return FormatOpen(
            WorkbookProgressPresentationPlanner.ToOpenProgressStep(update.Phase),
            update.Elapsed,
            update.Percent,
            getText);
    }

    public static WorkbookProgressText FormatOpen(
        string phase,
        TimeSpan elapsed,
        double? percent,
        Func<string, string> getText) =>
        FormatOpen(
            WorkbookProgressPresentationPlanner.ParseOpenProgressStep(phase),
            elapsed,
            percent,
            getText);

    public static WorkbookProgressText FormatOpen(
        WorkbookOpenProgressStep step,
        TimeSpan elapsed,
        double? percent,
        Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);

        var plan = WorkbookProgressPresentationPlanner.BuildOpenTextPlan(step, elapsed);
        return new WorkbookProgressText(
            getText(plan.TitleResourceKey),
            getText(plan.DetailResourceKey),
            percent);
    }

    public static WorkbookProgressText FormatSave(
        WorkbookSaveProgressUpdate update,
        Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(update);

        return FormatSave(
            WorkbookProgressPresentationPlanner.ToSaveProgressStep(update.Phase),
            update.Elapsed,
            update.Percent,
            getText);
    }

    public static WorkbookProgressText FormatSave(
        string phase,
        TimeSpan elapsed,
        double? percent,
        Func<string, string> getText) =>
        FormatSave(
            WorkbookProgressPresentationPlanner.ParseSaveProgressStep(phase),
            elapsed,
            percent,
            getText);

    public static WorkbookProgressText FormatSave(
        WorkbookSaveProgressStep step,
        TimeSpan elapsed,
        double? percent,
        Func<string, string> getText)
    {
        ArgumentNullException.ThrowIfNull(getText);

        var plan = WorkbookProgressPresentationPlanner.BuildSaveTextPlan(step, elapsed);
        return new WorkbookProgressText(
            getText(plan.TitleResourceKey),
            getText(plan.DetailResourceKey),
            percent);
    }
}

public sealed record WorkbookProgressText(string Title, string Detail, double? Percent);
