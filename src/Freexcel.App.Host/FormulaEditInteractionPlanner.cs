namespace Freexcel.App.Host;

public static class FormulaEditInteractionPlanner
{
    public static bool IsFormulaText(string? text) =>
        !string.IsNullOrEmpty(text) && text.StartsWith("=", StringComparison.Ordinal);

    public static bool ShouldStartPointModeFromTypedText(string text) =>
        text == "=";

    public static bool IsRangeEntryActive(string? text, bool pointMode) =>
        pointMode && IsFormulaText(text);

    public static bool ShouldCommitInlineArrows(string? text, bool pointMode) =>
        !IsFormulaText(text) && !IsRangeEntryActive(text, pointMode);

    public static bool TogglePointMode(string? text, bool pointMode) =>
        IsFormulaText(text) ? !pointMode : pointMode;
}
