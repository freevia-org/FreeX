using System.Collections.Generic;
using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// Builds the neutral <see cref="StatusBarViewModel"/> readout from selection statistics, view
/// mode and zoom. Ports the formatting logic (including the number-reuse optimization) that used
/// to live in the WPF-coupled <c>StatusBarDisplayState</c>, but produces <c>bool</c> visibility
/// and plain strings so any shell can render it.
/// </summary>
public static class StatusBarDisplayModelBuilder
{
    /// <summary>Builds the "Ready" / cell-mode model with no aggregate readout.</summary>
    public static StatusBarViewModel Ready(
        StatusBarViewMode viewMode,
        int zoomPercent,
        string readyText) =>
        new(
            viewMode,
            zoomPercent,
            IsReadyVisible: true,
            ReadyText: readyText,
            AreStatsVisible: false,
            Readouts: StatusBarViewModel.NoReadouts);

    /// <summary>
    /// Builds the aggregate-stats model for a non-empty selection. Mirrors the previous
    /// <c>StatusBarDisplayState.Stats</c> behavior: Average/Sum/Min/Max appear only when numeric,
    /// Count/NumericalCount always appear, and equal numbers reuse the first formatted text.
    /// </summary>
    public static StatusBarViewModel Stats(
        StatusBarViewMode viewMode,
        int zoomPercent,
        WorkbookSelectionStats stats,
        IStatusBarTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        // Real Excel propagates an error cell in the selection into the Average/Sum/Min/Max
        // status-bar readouts (matching SUM/AVERAGE/MIN/MAX's own error-propagation over a plain
        // range reference) instead of silently excluding it from the computation. Count/Numerical
        // Count below are unaffected -- Excel keeps counting normally.
        var errorCode = stats.AggregateErrorCode;

        string? averageNumber;
        string? sumNumber;
        string? minNumber;
        string? maxNumber;
        if (errorCode is not null)
        {
            averageNumber = errorCode;
            sumNumber = errorCode;
            minNumber = errorCode;
            maxNumber = errorCode;
        }
        else
        {
            averageNumber = stats.Average.HasValue
                ? FormatNumber(stats.Average.Value)
                : null;
            sumNumber = stats.HasNumericalValues
                ? FormatNumberWithReuse(stats.Sum, stats.Average, averageNumber)
                : null;
            minNumber = stats.Min.HasValue
                ? FormatNumberWithReuse(stats.Min.Value, stats.Average, averageNumber, stats.Sum, sumNumber)
                : null;
            maxNumber = stats.Max.HasValue
                ? FormatNumberWithReuse(stats.Max.Value, stats.Average, averageNumber, stats.Sum, sumNumber, stats.Min, minNumber)
                : null;
        }

        var readouts = new List<StatusBarReadoutItem>(6)
        {
            Readout(StatusBarReadoutKind.Average, averageNumber, textProvider),
            CountReadout(StatusBarReadoutKind.Count, stats.Count, textProvider),
            CountReadout(StatusBarReadoutKind.NumericalCount, stats.NumericalCount, textProvider),
            Readout(StatusBarReadoutKind.Sum, sumNumber, textProvider),
            Readout(StatusBarReadoutKind.Minimum, minNumber, textProvider),
            Readout(StatusBarReadoutKind.Maximum, maxNumber, textProvider),
        };

        return new StatusBarViewModel(
            viewMode,
            zoomPercent,
            IsReadyVisible: false,
            ReadyText: "",
            AreStatsVisible: true,
            Readouts: readouts);
    }

    /// <summary>Formats a number the compact, Excel-like way used across the status bar.</summary>
    public static string FormatNumber(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
            return value.ToString("N0", CultureInfo.CurrentCulture);

        // A flat G10 doesn't just trim decimal precision -- once the integer part itself needs
        // 10+ significant digits, G10 rounds the *whole number*, silently turning e.g.
        // 1200000000.6 into "1200000001" (a 0.4 discrepancy dressed up as an exact integer).
        // Scale the precision to the value's magnitude so the integer part always survives
        // intact, while keeping the original compact 10-significant-digit behavior (and its
        // pinned "123456789.1234" -> "123456789.1" rounding) for the common case where the
        // integer part fits in fewer than 10 digits. Capped at double's ~15-digit round-trip
        // precision ceiling, mirroring the app's own General number formatter.
        var integerDigits = (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1;
        var significantDigits = Math.Clamp(integerDigits + 1, 10, 15);
        return value.ToString(
            "G" + significantDigits.ToString(CultureInfo.InvariantCulture),
            CultureInfo.CurrentCulture);
    }

    private static StatusBarReadoutItem Readout(
        StatusBarReadoutKind kind,
        string? formattedNumber,
        IStatusBarTextProvider textProvider)
    {
        var value = formattedNumber is not null
            ? string.Format(CultureInfo.CurrentCulture, textProvider.GetReadoutFormat(kind), formattedNumber)
            : "";
        return new StatusBarReadoutItem(kind, textProvider.GetReadoutLabel(kind), value, IsVisible: value.Length > 0);
    }

    private static StatusBarReadoutItem CountReadout(
        StatusBarReadoutKind kind,
        int count,
        IStatusBarTextProvider textProvider)
    {
        var value = string.Format(CultureInfo.CurrentCulture, textProvider.GetReadoutFormat(kind), count);
        return new StatusBarReadoutItem(kind, textProvider.GetReadoutLabel(kind), value, IsVisible: true);
    }

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

        return FormatNumber(value);
    }
}
