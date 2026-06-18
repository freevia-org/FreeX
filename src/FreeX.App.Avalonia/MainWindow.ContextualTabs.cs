using System.Collections.Generic;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>
/// Phase-1 wiring for the Help tab and the contextual ribbon tabs (Chart/Picture/Shape/Table/Pivot).
/// The contextual tabs render as shells on selection (driven by <see cref="AvaloniaRibbonContextSource"/>);
/// most of their commands are honest "not yet available" status reports, and the few tractable ones reuse
/// existing shell handlers. The command ids here mirror those declared in
/// <see cref="Ribbon.AvaloniaRibbonHost"/>'s definition.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Central map of Help/contextual-tab command id → handler, merged into the ribbon's ExtraCommands so
    /// every Phase-1 button does something honest. Real handlers reuse existing shell behavior; the rest
    /// report a clearly-labeled "not yet available" status (no silent no-ops, no invented behavior).
    /// </summary>
    private IReadOnlyDictionary<string, Action> BuildContextualTabCommands()
    {
        return new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            // --- Help tab (always visible): About is real; the rest report honestly. ---
            ["help.about"] = () => _ = ShowAboutDialogAsync(),
            ["help.helpOnline"] = () => _ = OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online"),
            ["help.feedback"] = () => _ = OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, "Send Feedback"),
            ["help.checkUpdates"] = () => _ = OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, "Check for Updates"),

            // --- Chart Design / Chart Format (chart.selected) — Phase 1 shells. ---
            ["chartDesign.changeType"] = () => ReportContextualNotYetAvailable("Change Chart Type"),
            ["chartDesign.selectData"] = () => ReportContextualNotYetAvailable("Select Data"),
            ["chartFormat.shapeFill"] = () => ReportContextualNotYetAvailable("Chart Shape Fill"),
            ["chartFormat.shapeOutline"] = () => ReportContextualNotYetAvailable("Chart Shape Outline"),

            // --- Picture Format (picture.selected) — Phase 1 shells. ---
            ["pictureFormat.bringForward"] = () => ReportContextualNotYetAvailable("Bring Forward"),
            ["pictureFormat.sendBackward"] = () => ReportContextualNotYetAvailable("Send Backward"),
            ["pictureFormat.altText"] = () => ReportContextualNotYetAvailable("Alt Text"),

            // --- Shape Format (shape.selected) — Phase 1 shells. ---
            ["shapeFormat.shapeFill"] = () => ReportContextualNotYetAvailable("Shape Fill"),
            ["shapeFormat.shapeOutline"] = () => ReportContextualNotYetAvailable("Shape Outline"),
            ["shapeFormat.bringForward"] = () => ReportContextualNotYetAvailable("Bring Forward"),
            ["shapeFormat.sendBackward"] = () => ReportContextualNotYetAvailable("Send Backward"),

            // --- Table Design (table.active) — Phase 1 shells. ---
            ["tableDesign.convertToRange"] = () => ReportContextualNotYetAvailable("Convert to Range"),
            ["tableDesign.removeDuplicates"] = () => _ = ShowRemoveDuplicatesDialogAsync(),

            // --- PivotTable Analyze / Design (pivot.active) — Phase 1 shells. ---
            ["pivotAnalyze.refresh"] = () => ReportContextualNotYetAvailable("Refresh PivotTable"),
            ["pivotDesign.grandTotals"] = () => ReportContextualNotYetAvailable("Grand Totals"),
            ["pivotDesign.reportLayout"] = () => ReportContextualNotYetAvailable("Report Layout"),
        };
    }

    /// <summary>Reports that a contextual-tab command is a Phase-1 shell, on the status bar.</summary>
    private void ReportContextualNotYetAvailable(string commandLabel)
        => RefreshShell($"{commandLabel} is not yet available.");
}
