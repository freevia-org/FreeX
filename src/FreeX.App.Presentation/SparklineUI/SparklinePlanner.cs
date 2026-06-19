using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SparklineUI;

/// <summary>
/// The outcome of validating the Insert Sparkline inputs: either both ranges parsed (carrying the
/// resolved data range + anchor cell) or which field is at fault.
/// </summary>
public enum SparklineInputValidation
{
    Valid,
    InvalidDataRange,
    InvalidLocation,
}

/// <summary>
/// The marker / point-emphasis flags a Sparkline edit can toggle, surfaced as a catalog so the shell
/// can render one checkbox per entry without hard-coding the list.
/// </summary>
public enum SparklinePointToggle
{
    Markers,
    HighPoint,
    LowPoint,
    FirstPoint,
    LastPoint,
    NegativePoints,
}

/// <summary>
/// Portable, framework-free decision logic behind the Sparkline insert / edit dialogs, shared by every
/// shell (Avalonia today, macOS by inheritance). It owns the sparkline type catalog, the data-range and
/// location parsing rules (single-sourced with the Core <see cref="SparklineRangeLimits"/> cap), the
/// marker / point flag catalog + projection, and the build of a <see cref="SparklineSettings"/> snapshot
/// the Core <see cref="ConfigureSparklineCommand"/> applies. No UI types, so the shells only wire
/// controls to it.
/// </summary>
public static class SparklinePlanner
{
    /// <summary>The sparkline kinds offered by the dialog, in display order.</summary>
    public static IReadOnlyList<SparklineKind> Kinds { get; } =
    [
        SparklineKind.Line,
        SparklineKind.Column,
        SparklineKind.WinLoss,
    ];

    /// <summary>The point-emphasis toggles offered by the edit dialog, in display order.</summary>
    public static IReadOnlyList<SparklinePointToggle> PointToggles { get; } =
    [
        SparklinePointToggle.Markers,
        SparklinePointToggle.HighPoint,
        SparklinePointToggle.LowPoint,
        SparklinePointToggle.FirstPoint,
        SparklinePointToggle.LastPoint,
        SparklinePointToggle.NegativePoints,
    ];

    /// <summary>The neutral display label for a sparkline kind (used as a localization-key suffix too).</summary>
    public static string KindKey(SparklineKind kind) =>
        kind switch
        {
            SparklineKind.Column => "Column",
            SparklineKind.WinLoss => "WinLoss",
            _ => "Line",
        };

    /// <summary>The neutral display label for a point toggle (used as a localization-key suffix too).</summary>
    public static string ToggleKey(SparklinePointToggle toggle) => toggle.ToString();

    /// <summary>
    /// Markers apply only to line sparklines; negative-point emphasis applies only to column / win-loss
    /// sparklines. Used to grey out toggles that do not affect the selected kind.
    /// </summary>
    public static bool IsToggleApplicable(SparklinePointToggle toggle, SparklineKind kind) =>
        toggle switch
        {
            SparklinePointToggle.Markers => kind == SparklineKind.Line,
            SparklinePointToggle.NegativePoints => kind != SparklineKind.Line,
            _ => true,
        };

    /// <summary>Reads the current state of a toggle off a settings snapshot.</summary>
    public static bool GetToggle(SparklineSettings settings, SparklinePointToggle toggle) =>
        toggle switch
        {
            SparklinePointToggle.Markers => settings.ShowMarkers,
            SparklinePointToggle.HighPoint => settings.ShowHighPoint,
            SparklinePointToggle.LowPoint => settings.ShowLowPoint,
            SparklinePointToggle.FirstPoint => settings.ShowFirstPoint,
            SparklinePointToggle.LastPoint => settings.ShowLastPoint,
            SparklinePointToggle.NegativePoints => settings.ShowNegativePoints,
            _ => false,
        };

    /// <summary>
    /// Validates the Insert Sparkline inputs, returning the resolved data range + anchor cell when both
    /// parse. The data range must be a real range within the supported cell cap; the location must be a
    /// single cell. Mirrors the Windows host's <c>SparklineInputParser</c> rules.
    /// </summary>
    public static SparklineInputValidation ValidateInsert(
        string dataRangeText,
        string locationText,
        SheetId sheetId,
        out GridRange dataRange,
        out CellAddress location)
    {
        dataRange = default;
        location = default;

        if (!TryParseDataRange(dataRangeText, sheetId, out dataRange))
            return SparklineInputValidation.InvalidDataRange;

        return CellAddress.TryParse((locationText ?? string.Empty).Trim(), sheetId, out location)
            ? SparklineInputValidation.Valid
            : SparklineInputValidation.InvalidLocation;
    }

    /// <summary>Parses a sparkline data range, rejecting ranges over the supported cell cap.</summary>
    public static bool TryParseDataRange(string? input, SheetId sheetId, out GridRange range)
    {
        range = default;
        try
        {
            var parsed = GridRange.Parse((input ?? string.Empty).Trim(), sheetId);
            if (!SparklineRangeLimits.IsSupportedDataRange(parsed))
                return false;

            range = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the settings snapshot the edit command applies, clearing flags that do not apply to the
    /// chosen kind so an edit never leaves a stale flag (e.g. markers on a column sparkline).
    /// </summary>
    public static SparklineSettings BuildSettings(
        SparklineKind kind,
        bool showMarkers,
        bool showHighPoint,
        bool showLowPoint,
        bool showFirstPoint,
        bool showLastPoint,
        bool showNegativePoints,
        CellColor? seriesColor)
    {
        var isLine = kind == SparklineKind.Line;
        return new SparklineSettings(
            kind,
            ShowMarkers: showMarkers && isLine,
            showHighPoint,
            showLowPoint,
            showFirstPoint,
            showLastPoint,
            ShowNegativePoints: showNegativePoints && !isLine,
            seriesColor);
    }
}
