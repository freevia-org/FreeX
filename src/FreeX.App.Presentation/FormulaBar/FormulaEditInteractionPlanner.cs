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

    /// <summary>
    /// Whether plain (unmodified) arrow keys should commit the inline editor's text and move the
    /// active cell, vs. leaving the arrow key to move the caret within the text.
    /// </summary>
    /// <param name="enteredViaEditKey">
    /// True when this inline-edit session was opened via F2 or a double-click -- real Excel's
    /// "Edit" mode, where the caret lands in existing content and arrow keys reposition it without
    /// committing. False (the default) covers "Enter" mode, opened by typing a fresh character over
    /// the current selection, where arrow keys commit the freshly-typed, non-formula content and
    /// move on (R78-render-inplace-editor-5-1: before this parameter existed, F2 on a non-formula
    /// cell was indistinguishable from typing a fresh character, so arrows always committed and the
    /// user could never reposition the caret to fix existing text).
    /// </param>
    public static bool ShouldCommitInlineArrows(string? text, bool pointMode, bool enteredViaEditKey = false) =>
        !enteredViaEditKey && !IsFormulaText(text) && !IsRangeEntryActive(text, pointMode);

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
