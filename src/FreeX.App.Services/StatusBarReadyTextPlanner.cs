using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Shared ready/status text policy for the workbook status bar. Renderers supply localized
/// fallback text; this planner overlays the active-cell data-validation input prompt when
/// Excel would surface it in the status area.
/// </summary>
public static class StatusBarReadyTextPlanner
{
    public static string NormalizeTransientReadyText(string? status, IStatusBarTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        return NormalizeTransientReadyText(status, textProvider.GetReadyText());
    }

    public static string NormalizeTransientReadyText(string? status, string fallbackReadyText)
    {
        ArgumentNullException.ThrowIfNull(fallbackReadyText);

        if (string.IsNullOrWhiteSpace(status))
            return fallbackReadyText;

        return status.StartsWith("Showing ", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Hiding ", StringComparison.OrdinalIgnoreCase)
                ? fallbackReadyText
                : status;
    }

    public static string BuildReadyText(
        Sheet sheet,
        CellAddress activeCell,
        IStatusBarTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        return BuildReadyText(sheet, activeCell, textProvider.GetReadyText());
    }

    public static string BuildReadyText(Sheet sheet, CellAddress activeCell, string fallbackReadyText)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(fallbackReadyText);

        return FormatInputPrompt(
            DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, activeCell),
            fallbackReadyText);
    }

    public static string FormatInputPrompt(
        DataValidationService.InputPrompt? prompt,
        string fallbackReadyText)
    {
        ArgumentNullException.ThrowIfNull(fallbackReadyText);

        if (prompt is not { } inputPrompt)
            return fallbackReadyText;

        if (inputPrompt.Title.Length == 0)
            return inputPrompt.Message;

        if (inputPrompt.Message.Length == 0)
            return inputPrompt.Title;

        return $"{inputPrompt.Title}: {inputPrompt.Message}";
    }
}
