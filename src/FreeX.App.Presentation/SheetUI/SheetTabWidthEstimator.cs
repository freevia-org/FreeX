namespace FreeX.App.Presentation.SheetUI;

/// <summary>
/// Per-renderer glyph/padding metrics for <see cref="SheetTabWidthEstimator"/>. The estimate is a
/// pre-layout guess used to decide sheet-tab strip overflow and chrome geometry before the real
/// controls have been measured, so the constants are tuned to each host's own tab template
/// (font size, padding, whether a protected-sheet indicator glyph is drawn).
/// </summary>
/// <param name="BaseWidth">Fixed padding/chrome width added regardless of the tab name.</param>
/// <param name="CharacterWidth">Average advance width charged per name character.</param>
/// <param name="ProtectedIndicatorWidth">Extra width when the sheet is protected (0 when the host draws no indicator).</param>
/// <param name="MinimumWidth">Lower clamp.</param>
/// <param name="MaximumWidth">Upper clamp; <see cref="double.PositiveInfinity"/> for hosts that do not cap.</param>
/// <param name="TreatEmptyNameAsSingleCharacter">
/// When true an empty name is charged one character (the Avalonia strip's historic behaviour).
/// </param>
public readonly record struct SheetTabWidthMetrics(
    double BaseWidth,
    double CharacterWidth,
    double ProtectedIndicatorWidth,
    double MinimumWidth,
    double MaximumWidth,
    bool TreatEmptyNameAsSingleCharacter);

/// <summary>
/// Neutral owner of the sheet-tab width estimate that both renderers previously duplicated
/// (WPF MainWindow.SheetTabs.cs, Avalonia MainWindow.cs).
///
/// The two copies used genuinely DIFFERENT formulas -- they are calibrated against different tab
/// templates, and converging them onto one set of numbers would visibly re-lay-out one host's tab
/// strip. So the arithmetic is unified here while each host keeps its own calibration via
/// <see cref="SheetTabWidthMetrics"/> (<see cref="Wpf"/> / <see cref="Avalonia"/>).
/// </summary>
public static class SheetTabWidthEstimator
{
    /// <summary>WPF host calibration: <c>max(86, 54 + protectedIndicator + length * 7.5)</c>, uncapped.</summary>
    public static readonly SheetTabWidthMetrics Wpf = new(
        BaseWidth: 54,
        CharacterWidth: 7.5,
        ProtectedIndicatorWidth: 16,
        MinimumWidth: 86,
        MaximumWidth: double.PositiveInfinity,
        TreatEmptyNameAsSingleCharacter: false);

    /// <summary>Avalonia host calibration: <c>clamp(20 + max(1, length) * 6.6, 60, 168)</c>, no protected indicator.</summary>
    public static readonly SheetTabWidthMetrics Avalonia = new(
        BaseWidth: 20,
        CharacterWidth: 6.6,
        ProtectedIndicatorWidth: 0,
        MinimumWidth: 60,
        MaximumWidth: 168,
        TreatEmptyNameAsSingleCharacter: true);

    public static double Estimate(string? tabName, bool isProtected, SheetTabWidthMetrics metrics)
    {
        var length = tabName?.Length ?? 0;
        if (metrics.TreatEmptyNameAsSingleCharacter)
            length = Math.Max(1, length);

        var width = metrics.BaseWidth
            + (isProtected ? metrics.ProtectedIndicatorWidth : 0)
            + length * metrics.CharacterWidth;

        var minimum = metrics.MinimumWidth;
        var maximum = Math.Max(minimum, metrics.MaximumWidth);
        return Math.Clamp(width, minimum, maximum);
    }

    public static double Estimate(string? tabName, SheetTabWidthMetrics metrics) =>
        Estimate(tabName, isProtected: false, metrics);
}
