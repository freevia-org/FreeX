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
            stats.Average.HasValue ? UiText.Format("StatusBar_AverageFormat", StatusBarCalculator.FormatNumber(stats.Average.Value)) : "",
            UiText.Format("StatusBar_CountFormat", stats.Count),
            UiText.Format("StatusBar_NumericalCountFormat", stats.NumericalCount),
            stats.NumericalCount > 0 ? UiText.Format("StatusBar_SumFormat", StatusBarCalculator.FormatNumber(stats.Sum)) : "",
            stats.Min.HasValue ? UiText.Format("StatusBar_MinFormat", StatusBarCalculator.FormatNumber(stats.Min.Value)) : "",
            stats.Max.HasValue ? UiText.Format("StatusBar_MaxFormat", StatusBarCalculator.FormatNumber(stats.Max.Value)) : "");
}
