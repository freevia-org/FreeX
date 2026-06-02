using System.Globalization;
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

    public static StatusBarDisplayState Stats(StatusBarCalculator.Stats stats)
    {
        var averageNumber = stats.Average.HasValue
            ? StatusBarCalculator.FormatNumber(stats.Average.Value)
            : null;
        var sumNumber = stats.NumericalCount > 0
            ? FormatNumberWithReuse(stats.Sum, stats.Average, averageNumber)
            : null;
        var minNumber = stats.Min.HasValue
            ? FormatNumberWithReuse(stats.Min.Value, stats.Average, averageNumber, stats.Sum, sumNumber)
            : null;
        var maxNumber = stats.Max.HasValue
            ? FormatNumberWithReuse(stats.Max.Value, stats.Average, averageNumber, stats.Sum, sumNumber, stats.Min, minNumber)
            : null;

        return new(
            Visibility.Collapsed,
            Visibility.Visible,
            "",
            averageNumber is not null ? FormatStatusText("StatusBar_AverageFormat", averageNumber) : "",
            FormatStatusText("StatusBar_CountFormat", stats.Count),
            FormatStatusText("StatusBar_NumericalCountFormat", stats.NumericalCount),
            sumNumber is not null ? FormatStatusText("StatusBar_SumFormat", sumNumber) : "",
            minNumber is not null ? FormatStatusText("StatusBar_MinFormat", minNumber) : "",
            maxNumber is not null ? FormatStatusText("StatusBar_MaxFormat", maxNumber) : "");
    }

    private static string FormatStatusText(string key, object? value) =>
        string.Format(CultureInfo.CurrentCulture, UiText.Get(key), value);

    private static string FormatNumberWithReuse(
        double value,
        double? firstValue,
        string? firstText,
        double? secondValue = null,
        string? secondText = null,
        double? thirdValue = null,
        string? thirdText = null)
    {
        if (firstText is not null && firstValue.HasValue && value.Equals(firstValue.Value))
            return firstText;
        if (secondText is not null && secondValue.HasValue && value.Equals(secondValue.Value))
            return secondText;
        if (thirdText is not null && thirdValue.HasValue && value.Equals(thirdValue.Value))
            return thirdText;

        return StatusBarCalculator.FormatNumber(value);
    }
}
