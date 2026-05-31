using System.Windows;

namespace FreeX.App.Host;

internal sealed record StatusBarDisplayState(
    Visibility ReadyVisibility,
    Visibility StatsVisibility,
    string ReadyText,
    string AverageText,
    string CountText,
    string NumericalCountText,
    string SumText,
    string MinText,
    string MaxText)
{
    public static StatusBarDisplayState Ready(string text) =>
        new(Visibility.Visible, Visibility.Collapsed, text, "", "", "", "", "", "");

    public static StatusBarDisplayState Stats(StatusBarCalculator.Stats stats) =>
        new(
            Visibility.Collapsed,
            Visibility.Visible,
            "",
            stats.Average.HasValue ? $"Average: {StatusBarCalculator.FormatNumber(stats.Average.Value)}" : "",
            $"Count: {stats.Count}",
            $"Numerical Count: {stats.NumericalCount}",
            stats.NumericalCount > 0 ? $"Sum: {StatusBarCalculator.FormatNumber(stats.Sum)}" : "",
            stats.Min.HasValue ? $"Min: {StatusBarCalculator.FormatNumber(stats.Min.Value)}" : "",
            stats.Max.HasValue ? $"Max: {StatusBarCalculator.FormatNumber(stats.Max.Value)}" : "");
}
