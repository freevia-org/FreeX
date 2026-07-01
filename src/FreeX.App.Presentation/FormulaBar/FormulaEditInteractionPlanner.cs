using Free.Shared.AppServices;

namespace FreeX.App.Presentation.FormulaBar;

public enum FormulaEditStatusBarMode
{
    Enter,
    Edit,
    Point
}

public readonly record struct FormulaEditStatusBarPlan(
    FormulaEditStatusBarMode Mode,
    string ResourceKey);

public readonly record struct FormulaEditTextChangePlan(
    bool StartsPointMode,
    FormulaEditStatusBarPlan? StatusBarPlan);

public readonly record struct FormulaTypedEntryPlan(
    bool PointMode,
    FormulaEditStatusBarPlan StatusBarPlan);

public readonly record struct FormulaPointModeTogglePlan(
    bool PointMode,
    bool ClearReferenceSpan,
    bool Handled,
    FormulaEditStatusBarPlan StatusBarPlan);

public static class FormulaEditInteractionPlanner
{
    public static bool IsFormulaText(string? text) =>
        !string.IsNullOrEmpty(text) && text.StartsWith("=", StringComparison.Ordinal);

    public static bool ShouldStartPointModeFromTypedText(string? text) =>
        text == "=";

    public static bool IsRangeEntryActive(string? text, bool pointMode) =>
        pointMode && IsFormulaText(text);

    public static bool ShouldCommitInlineArrows(string? text, bool pointMode) =>
        !IsFormulaText(text) && !IsRangeEntryActive(text, pointMode);

    public static bool TogglePointMode(string? text, bool pointMode) =>
        IsFormulaText(text) ? !pointMode : pointMode;

    public static FormulaEditStatusBarPlan BuildStatusBarPlan(FormulaEditStatusBarMode mode) =>
        new(mode, mode switch
        {
            FormulaEditStatusBarMode.Enter => StatusBarTextResourceKeys.EnterMode,
            FormulaEditStatusBarMode.Edit => StatusBarTextResourceKeys.EditMode,
            FormulaEditStatusBarMode.Point => StatusBarTextResourceKeys.PointMode,
            _ => StatusBarTextResourceKeys.EditMode
        });

    public static FormulaEditStatusBarPlan BuildEditStatusBarPlan(bool pointMode) =>
        BuildStatusBarPlan(pointMode ? FormulaEditStatusBarMode.Point : FormulaEditStatusBarMode.Edit);

    public static FormulaEditStatusBarPlan BuildEnterStatusBarPlan() =>
        BuildStatusBarPlan(FormulaEditStatusBarMode.Enter);

    public static FormulaEditTextChangePlan BuildTextChangePlan(string? text)
    {
        var startsPointMode = ShouldStartPointModeFromTypedText(text);
        return new FormulaEditTextChangePlan(
            startsPointMode,
            startsPointMode ? BuildEnterStatusBarPlan() : null);
    }

    public static FormulaTypedEntryPlan BuildTypedEntryPlan(string? text) =>
        new(ShouldStartPointModeFromTypedText(text), BuildEnterStatusBarPlan());

    public static FormulaPointModeTogglePlan BuildPointModeTogglePlan(string? text, bool pointMode)
    {
        var nextPointMode = TogglePointMode(text, pointMode);
        return new FormulaPointModeTogglePlan(
            nextPointMode,
            ClearReferenceSpan: !nextPointMode,
            Handled: IsFormulaText(text),
            StatusBarPlan: BuildEditStatusBarPlan(nextPointMode));
    }
}
