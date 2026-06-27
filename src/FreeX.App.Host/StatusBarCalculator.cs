using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Status-bar adapter over the shared, platform-neutral selection-stats pipeline. Aggregate
/// math is delegated to <see cref="WorkbookSelectionStatsCalculator"/> (the single shared
/// implementation, also used by the Avalonia app); this type keeps the host's <see cref="Stats"/>
/// shape (consumed by <c>StatusBarStatsCache</c>) plus the WPF-only ready-status helper.
/// </summary>
public static class StatusBarCalculator
{
    public readonly record struct Stats(double Sum, int Count, int NumericalCount, double? Average, double? Min, double? Max);

    public static Stats Calculate(Sheet sheet, GridRange range) =>
        ToStats(WorkbookSelectionStatsCalculator.Calculate(sheet, range));

    internal static Stats ToStats(WorkbookSelectionStats stats) =>
        new(stats.Sum, stats.Count, stats.NumericalCount, stats.Average, stats.Min, stats.Max);

    internal static WorkbookSelectionStats ToShared(Stats stats) =>
        new(stats.Sum, stats.Count, stats.NumericalCount, stats.Average, stats.Min, stats.Max);

    public static string FormatNumber(double value) =>
        StatusBarDisplayModelBuilder.FormatNumber(value);

    public static string GetReadyStatusText(Sheet sheet, CellAddress activeCell) =>
        StatusBarReadyTextPlanner.BuildReadyText(sheet, activeCell, UiText.Get("MainWindow_Text_Ready"));
}
