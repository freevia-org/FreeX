using System.Collections.Generic;

namespace Free.Shared.AppServices;

/// <summary>
/// Platform-neutral model of the spreadsheet status bar / footer. Carries the active
/// view mode, the zoom percent, and the selection-aggregate readout as a list of
/// <see cref="StatusBarReadoutItem"/>s with <c>bool</c> visibility (no <c>System.Windows</c>
/// types), so it can be rendered by any shell (WPF today, Avalonia / FreeW later).
/// </summary>
/// <param name="ViewMode">Active worksheet view mode for the view-shortcut toggles.</param>
/// <param name="ZoomPercent">Current zoom level, in percent (e.g. 100 for 100%).</param>
/// <param name="IsReadyVisible">True when the "Ready" / cell-mode prompt is shown instead of stats.</param>
/// <param name="ReadyText">The ready / cell-mode prompt text (empty when stats are shown).</param>
/// <param name="AreStatsVisible">True when the aggregate-stats readout is shown instead of the ready prompt.</param>
/// <param name="Readouts">The aggregate readout items (Average, Count, … Max) in display order.</param>
public sealed record StatusBarViewModel(
    StatusBarViewMode ViewMode,
    int ZoomPercent,
    bool IsReadyVisible,
    string ReadyText,
    bool AreStatsVisible,
    IReadOnlyList<StatusBarReadoutItem> Readouts)
{
    /// <summary>An empty readout list, reused for the ready state to avoid allocations.</summary>
    public static readonly IReadOnlyList<StatusBarReadoutItem> NoReadouts = [];

    /// <summary>Looks up a readout item by kind, or <c>null</c> when not present.</summary>
    public StatusBarReadoutItem? FindReadout(StatusBarReadoutKind kind)
    {
        for (var index = 0; index < Readouts.Count; index++)
        {
            if (Readouts[index].Kind == kind)
                return Readouts[index];
        }

        return null;
    }
}
