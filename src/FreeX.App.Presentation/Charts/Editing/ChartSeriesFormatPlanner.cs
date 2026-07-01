using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>The per-series fill/stroke/marker state read from a chart and edited back through the dialog.</summary>
public readonly record struct ChartSeriesFormatInput(
    int SeriesIndex,
    CellColor? FillColor,
    CellColor? StrokeColor,
    double? StrokeThickness,
    ChartMarkerStyle? MarkerStyle,
    double? MarkerSize,
    ChartLineDashStyle? DashStyle = null);

public enum ChartSeriesFormatDialogControlKind
{
    ComboBox,
    Color,
    Number,
}

public enum ChartSeriesFormatDialogFieldId
{
    Series,
    FillColor,
    StrokeColor,
    StrokeThickness,
    DashStyle,
    MarkerStyle,
    MarkerSize,
}

public sealed record ChartSeriesFormatDialogFieldDescriptor(
    ChartSeriesFormatDialogFieldId Id,
    ChartSeriesFormatDialogControlKind ControlKind,
    string LabelResourceKey,
    string AutomationId,
    string? HelpResourceKey = null);

public sealed record ChartSeriesFormatDialogSectionDescriptor(
    string HeaderResourceKey,
    IReadOnlyList<ChartSeriesFormatDialogFieldDescriptor> Fields,
    string? HelpResourceKey = null);

public enum ChartSeriesFormatParseIssue
{
    None,
    FillColor,
    StrokeColor,
    StrokeThickness,
    MarkerSize,
}

/// <summary>
/// Portable (no UI) planner for the "Format Series" editing dialog: per-series fill color, line (stroke)
/// color and width, dash style, and marker style/size. Reads the chosen series' current
/// <see cref="ChartSeriesFormat"/> and merges an edited <see cref="ChartSeriesFormatInput"/> back into the
/// chart's series-format list (replacing the matching entry or appending a new one), producing the
/// <see cref="ChartLayoutOptions"/> the shell hands to the Core <see cref="SetChartLayoutCommand"/>. Setting
/// an explicit fill color clears any theme-color reference so the explicit color wins. Reused across every
/// shell.
/// </summary>
public static class ChartSeriesFormatPlanner
{
    private static readonly ChartLineDashStyle[] DashStyleCatalog = Enum.GetValues<ChartLineDashStyle>();
    private static readonly ChartMarkerStyle[] MarkerStyleCatalog = Enum.GetValues<ChartMarkerStyle>();

    private static readonly ChartSeriesFormatDialogFieldDescriptor[] SeriesOptionFields =
    [
        new(ChartSeriesFormatDialogFieldId.Series, ChartSeriesFormatDialogControlKind.ComboBox, "ChartSeriesFormat_SeriesLabel", "ChartSeriesFormatSeriesCombo", "ChartSeriesFormat_SeriesHelpText"),
    ];

    private static readonly ChartSeriesFormatDialogFieldDescriptor[] FillLineFields =
    [
        new(ChartSeriesFormatDialogFieldId.FillColor, ChartSeriesFormatDialogControlKind.Color, "ChartSeriesFormat_FillColorLabel", "ChartSeriesFormatFillButton"),
        new(ChartSeriesFormatDialogFieldId.StrokeColor, ChartSeriesFormatDialogControlKind.Color, "ChartSeriesFormat_LineColorLabel", "ChartSeriesFormatLineButton"),
        new(ChartSeriesFormatDialogFieldId.StrokeThickness, ChartSeriesFormatDialogControlKind.Number, "ChartSeriesFormat_LineWidthLabel", "ChartSeriesFormatLineWidthBox", "ChartSeriesFormat_LineWidthHelpText"),
        new(ChartSeriesFormatDialogFieldId.DashStyle, ChartSeriesFormatDialogControlKind.ComboBox, "ChartSeriesFormat_DashStyleLabel", "ChartSeriesFormatDashStyleCombo"),
        new(ChartSeriesFormatDialogFieldId.MarkerStyle, ChartSeriesFormatDialogControlKind.ComboBox, "ChartSeriesFormat_MarkerLabel", "ChartSeriesFormatMarkerCombo"),
        new(ChartSeriesFormatDialogFieldId.MarkerSize, ChartSeriesFormatDialogControlKind.Number, "ChartSeriesFormat_MarkerSizeLabel", "ChartSeriesFormatMarkerSizeBox", "ChartSeriesFormat_MarkerSizeHelpText"),
    ];

    private static readonly ChartSeriesFormatDialogSectionDescriptor[] DialogSections =
    [
        new("ChartSeriesFormat_SeriesOptionsGroup", SeriesOptionFields, "ChartSeriesFormat_SeriesHelpText"),
        new("ChartDialog_FillLineGroup", FillLineFields),
    ];

    public static IReadOnlyList<ChartLineDashStyle> GetDashStyleChoices() => DashStyleCatalog;

    public static IReadOnlyList<ChartMarkerStyle> GetMarkerStyleChoices() => MarkerStyleCatalog;

    public static IReadOnlyList<ChartSeriesFormatDialogSectionDescriptor> GetDialogSections() => DialogSections;

    public static ChartSeriesFormatDialogSectionDescriptor GetSeriesOptionsSection() => DialogSections[0];

    public static ChartSeriesFormatDialogSectionDescriptor GetFillLineSection() => DialogSections[1];

    public static ChartSeriesFormatDialogFieldDescriptor GetDialogField(ChartSeriesFormatDialogFieldId id)
    {
        foreach (var section in DialogSections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Id == id)
                    return field;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, null);
    }

    /// <summary>True when the chart has an actual data series to format.</summary>
    public static bool HasDataSeries(ChartModel chart) => ChartTypeSupport.GetDataSeriesCount(chart) > 0;

    /// <summary>The number of data series the series picker should offer (at least one).</summary>
    public static int GetSeriesCount(ChartModel chart) => Math.Max(1, ChartTypeSupport.GetDataSeriesCount(chart));

    /// <summary>
    /// Chooses the initially selected series for a format dialog: use the first stored format's series index
    /// when present, clamped into the current data-series range, otherwise series 0.
    /// </summary>
    public static int GetDefaultSeriesIndex(ChartModel chart)
    {
        var seriesCount = GetSeriesCount(chart);
        var requested = chart.SeriesFormats.Count > 0 ? chart.SeriesFormats[0].SeriesIndex : 0;
        return Math.Clamp(requested, 0, seriesCount - 1);
    }

    /// <summary>Reads the default series-format dialog state for the chart.</summary>
    public static ChartSeriesFormatInput ReadDefault(ChartModel chart) => Read(chart, GetDefaultSeriesIndex(chart));

    /// <summary>
    /// Reads the chosen series' current format into the dialog input shape. <paramref name="seriesIndex"/> is
    /// clamped into <c>[0, seriesCount)</c>; a series with no stored format reads as all-null (inherits the
    /// palette default).
    /// </summary>
    public static ChartSeriesFormatInput Read(ChartModel chart, int seriesIndex)
    {
        var count = GetSeriesCount(chart);
        var index = Math.Clamp(seriesIndex, 0, count - 1);
        var format = FindSeriesFormat(chart, index);
        return new ChartSeriesFormatInput(
            index,
            format?.FillColor,
            format?.StrokeColor,
            format?.StrokeThickness,
            format?.MarkerStyle,
            format?.MarkerSize,
            format?.DashStyle);
    }

    /// <summary>Normalizes dialog result defaults before the host projects them back into UI result records.</summary>
    public static ChartSeriesFormatInput Normalize(ChartSeriesFormatInput input) =>
        input with { SeriesIndex = Math.Max(0, input.SeriesIndex) };

    /// <summary>
    /// Validates the edited series format. Returns null when valid, otherwise an English reason it is
    /// rejected (a non-positive line width or marker size). Null thickness/size means "inherit" and is valid.
    /// </summary>
    public static string? Validate(ChartSeriesFormatInput input)
    {
        if (input.StrokeThickness is { } thickness && thickness <= 0)
            return "The line width must be greater than zero.";

        if (input.MarkerSize is { } size && size <= 0)
            return "The marker size must be greater than zero.";

        return null;
    }

    public static bool TryParseDialogInput(
        int seriesIndex,
        string? fillColorText,
        string? strokeColorText,
        string? strokeThicknessText,
        ChartLineDashStyle? selectedDashStyle,
        ChartMarkerStyle? selectedMarkerStyle,
        string? markerSizeText,
        out ChartSeriesFormatInput input,
        out ChartSeriesFormatParseIssue issue)
    {
        input = default;

        if (!ColorInputParser.TryParseOptionalHexColor(fillColorText ?? string.Empty, out var fillColor))
        {
            issue = ChartSeriesFormatParseIssue.FillColor;
            return false;
        }

        if (!ColorInputParser.TryParseOptionalHexColor(strokeColorText ?? string.Empty, out var strokeColor))
        {
            issue = ChartSeriesFormatParseIssue.StrokeColor;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullablePositiveDouble(strokeThicknessText ?? string.Empty, out var strokeThickness))
        {
            issue = ChartSeriesFormatParseIssue.StrokeThickness;
            return false;
        }

        if (!ChartDialogValueParser.TryParseNullablePositiveDouble(markerSizeText ?? string.Empty, out var markerSize))
        {
            issue = ChartSeriesFormatParseIssue.MarkerSize;
            return false;
        }

        input = Normalize(new ChartSeriesFormatInput(
            seriesIndex,
            fillColor,
            strokeColor,
            strokeThickness,
            IsKnownMarkerStyle(selectedMarkerStyle) ? selectedMarkerStyle : null,
            markerSize,
            IsKnownDashStyle(selectedDashStyle) ? selectedDashStyle : null));
        issue = ChartSeriesFormatParseIssue.None;
        return true;
    }

    /// <summary>
    /// Merges the edited series format into the chart's series-format list and returns the
    /// <see cref="ChartLayoutOptions"/> delta. The entry for <see cref="ChartSeriesFormatInput.SeriesIndex"/>
    /// is replaced (or appended when absent); other series are preserved. An explicit fill color clears the
    /// fill theme-color reference so it takes effect.
    /// </summary>
    public static ChartLayoutOptions Plan(ChartModel chart, ChartSeriesFormatInput input)
    {
        var normalized = Normalize(input);
        var seriesIndex = normalized.SeriesIndex;
        var formats = new List<ChartSeriesFormat>(chart.SeriesFormats);
        var existingIndex = IndexOfSeriesFormat(formats, seriesIndex);
        var current = existingIndex >= 0 ? formats[existingIndex] : new ChartSeriesFormat(seriesIndex);

        var updated = current with
        {
            FillColor = normalized.FillColor,
            FillThemeColor = normalized.FillColor is null ? current.FillThemeColor : null,
            StrokeColor = normalized.StrokeColor,
            StrokeThemeColor = normalized.StrokeColor is null ? current.StrokeThemeColor : null,
            StrokeThickness = normalized.StrokeThickness,
            DashStyle = normalized.DashStyle,
            MarkerStyle = normalized.MarkerStyle,
            MarkerSize = normalized.MarkerSize,
        };

        if (existingIndex >= 0)
            formats[existingIndex] = updated;
        else
            formats.Add(updated);

        return new ChartLayoutOptions(SeriesFormats: formats);
    }

    private static ChartSeriesFormat? FindSeriesFormat(ChartModel chart, int seriesIndex)
    {
        foreach (var format in chart.SeriesFormats)
        {
            if (format.SeriesIndex == seriesIndex)
                return format;
        }

        return null;
    }

    private static int IndexOfSeriesFormat(IReadOnlyList<ChartSeriesFormat> formats, int seriesIndex)
    {
        for (var index = 0; index < formats.Count; index++)
        {
            if (formats[index].SeriesIndex == seriesIndex)
                return index;
        }

        return -1;
    }

    private static bool IsKnownDashStyle(ChartLineDashStyle? dashStyle) =>
        dashStyle is { } value && Enum.IsDefined(value);

    private static bool IsKnownMarkerStyle(ChartMarkerStyle? markerStyle) =>
        markerStyle is { } value && Enum.IsDefined(value);
}
