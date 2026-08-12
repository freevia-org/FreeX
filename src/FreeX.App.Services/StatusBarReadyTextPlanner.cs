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

    /// <summary>
    /// R128-status-bar-calculate-indicator: calc-mode-aware overload used by shells (currently the
    /// Avalonia shell -- see <c>FreeXStatusBarRendererPlanner.NormalizeReadyText</c>) that render the default
    /// "ready" cell-mode text through this normalizer rather than a single production choke point like
    /// the WPF host's <c>StatusBarRefreshPlanner</c>. Resolves the fallback via
    /// <see cref="IStatusBarTextProvider.GetReadyText(bool, bool)"/> so a Manual-mode edit with a
    /// pending recalculation surfaces Excel's "Calculate" text instead of "Ready".
    /// </summary>
    public static string NormalizeTransientReadyText(
        string? status,
        IStatusBarTextProvider textProvider,
        bool isManualCalculationMode,
        bool hasPendingRecalculation)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        return NormalizeTransientReadyText(
            status,
            textProvider.GetReadyText(isManualCalculationMode, hasPendingRecalculation));
    }

    public static string NormalizeTransientReadyText(string? status, string fallbackReadyText)
    {
        ArgumentNullException.ThrowIfNull(fallbackReadyText);

        if (string.IsNullOrWhiteSpace(status))
            return fallbackReadyText;

        // R128-status-bar-calculate-indicator: "Ready" (exact, ordinal) is the literal placeholder
        // dozens of Avalonia MainWindow call sites pass for "no special transient status -- show
        // whatever the default cell-mode text should be" (mirroring the "Showing "/"Hiding " special
        // case below, which exists for the same reason). Routing it through fallbackReadyText lets the
        // calc-mode-aware overload above substitute "Calculate" without editing every one of those call
        // sites; for Automatic-mode workbooks fallbackReadyText is itself the localized "Ready" text, so
        // this is a no-op change for the common case.
        return status.StartsWith("Showing ", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Hiding ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Ready", StringComparison.Ordinal)
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

    /// <summary>
    /// R128-status-bar-calculate-indicator: calc-mode-aware overload feeding the WPF host's
    /// <c>StatusBarRefreshPlanner.Build</c> (the shell's production status-bar choke point). A
    /// data-validation input-message prompt still takes priority (matching <see cref="BuildReadyText"/>
    /// above and real Excel, which shows the input prompt over the cell-mode indicator), so the
    /// "Calculate" text only surfaces once the active cell has no prompt to show.
    /// </summary>
    public static string BuildReadyText(
        Sheet sheet,
        CellAddress activeCell,
        IStatusBarTextProvider textProvider,
        bool isManualCalculationMode,
        bool hasPendingRecalculation)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        return BuildReadyText(
            sheet,
            activeCell,
            textProvider.GetReadyText(isManualCalculationMode, hasPendingRecalculation));
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
