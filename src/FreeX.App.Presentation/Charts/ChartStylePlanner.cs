using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

public readonly record struct ChartSeriesPaint(CellColor FillColor, CellColor StrokeColor);

public readonly record struct ChartBarPaint(CellColor? FillColor, CellColor? StrokeColor, double StrokeThickness)
{
    public bool HasFill => FillColor is not null;
    public bool HasStroke => StrokeColor is not null && StrokeThickness > 0;
}

public readonly record struct ChartStyleInput(int? StyleId);

public sealed record ChartStyleGalleryOptionDescriptor(
    int? StyleId,
    string DisplayNameResourceKey,
    string PreviewLabelResourceKey,
    int? ResourceValue = null);

/// <summary>
/// UI-free style decisions shared by chart renderers. Hosts convert the returned colors into
/// their drawing primitives, but palette ordering and format precedence stay in one place.
/// </summary>
public static class ChartStylePlanner
{
    public const int MinStyleId = 1;
    public const int MaxStyleId = 48;

    private static readonly double[] AccentTintSchedule = [0.0, 0.4, -0.25, 0.6, -0.5];

    private static readonly WorkbookThemeColorSlot[] AccentSlots =
    [
        WorkbookThemeColorSlot.Accent1,
        WorkbookThemeColorSlot.Accent2,
        WorkbookThemeColorSlot.Accent3,
        WorkbookThemeColorSlot.Accent4,
        WorkbookThemeColorSlot.Accent5,
        WorkbookThemeColorSlot.Accent6,
    ];

    private static readonly ChartStyleGalleryOptionDescriptor[] StyleOptions = CreateStyleOptions();

    public static IReadOnlyList<ChartStyleGalleryOptionDescriptor> GetStyleOptions() => StyleOptions;

    public static ChartStyleInput Read(ChartModel chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return CreateResult(chart.ChartStyleId);
    }

    public static ChartStyleInput CreateResult(int? styleId) => new(NormalizeStyleId(styleId));

    public static int? NormalizeStyleId(int? styleId)
    {
        if (styleId is null)
            return null;

        return Math.Clamp(styleId.Value, MinStyleId, MaxStyleId);
    }

    public static int? ParseStyleId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return int.TryParse(text.Trim(), out var value) ? NormalizeStyleId(value) : null;
    }

    public static int NextStyleId(int? current)
    {
        var style = current ?? 0;
        return style >= 45 ? MinStyleId : style + 4;
    }

    public static int FindStyleOptionIndex(int? styleId)
    {
        var normalized = NormalizeStyleId(styleId);
        for (var index = 0; index < StyleOptions.Length; index++)
        {
            if (StyleOptions[index].StyleId == normalized)
                return index;
        }

        return 0;
    }

    public static ChartStyleGalleryOptionDescriptor GetStyleOption(int index) =>
        StyleOptions[Math.Clamp(index, 0, StyleOptions.Length - 1)];

    public static CellColor[] BuildExcelSeriesPalette(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var palette = new CellColor[AccentSlots.Length * AccentTintSchedule.Length];
        var index = 0;
        foreach (var tint in AccentTintSchedule)
        {
            foreach (var slot in AccentSlots)
            {
                palette[index++] = theme.ResolveColor(slot, tint);
            }
        }

        return palette;
    }

    public static CellColor GetPaletteColor(IReadOnlyList<CellColor> palette, int index)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0)
            throw new ArgumentException("Palette must contain at least one color.", nameof(palette));

        return palette[Math.Max(0, index) % palette.Count];
    }

    public static ChartSeriesFormat? FindSeriesFormat(ChartModel chart, int seriesIndex)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var formats = chart.SeriesFormats;
        for (var i = formats.Count - 1; i >= 0; i--)
        {
            var format = formats[i];
            if (format.SeriesIndex == seriesIndex)
                return format;
        }

        return null;
    }

    public static CellColor? ResolvePointFillColor(
        ChartModel chart,
        int seriesIndex,
        int pointIndex,
        WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(theme);

        var formats = chart.PointFillColors;
        for (var i = formats.Count - 1; i >= 0; i--)
        {
            var format = formats[i];
            if (format.SeriesIndex == seriesIndex && format.PointIndex == pointIndex)
                return format.ResolveFillColor(theme);
        }

        return null;
    }

    /// <summary>
    /// Resolves the per-point fill color for a bar/column series point when Excel's "Vary colors by
    /// point" (<c>c:varyColors</c>) is on and this is the chart's only plotted series -- the only
    /// shape Excel itself applies varyColors to for bar/column charts (a per-series legend still
    /// needs one color per series, so multi-series charts ignore the flag). An explicit
    /// per-point <c:dPt> fill (<see cref="ResolvePointFillColor"/>) always takes precedence over the
    /// palette-by-point-index cycling this method performs. Returns null when varyColors does not
    /// apply (flag unset, multi-series chart, or an explicit per-point override already exists), in
    /// which case the caller should fall back to its normal series-level/palette-by-series color.
    /// </summary>
    public static CellColor? ResolveVaryColorsPointFill(
        ChartModel chart,
        int seriesIndex,
        int pointIndex,
        int plottedSeriesCount,
        WorkbookTheme theme,
        IReadOnlyList<CellColor> palette)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(palette);

        if (ResolvePointFillColor(chart, seriesIndex, pointIndex, theme) is { } explicitFill)
            return explicitFill;

        if (chart.VaryColorsByPoint != true || plottedSeriesCount != 1)
            return null;

        return GetPaletteColor(palette, pointIndex);
    }

    public static ChartSeriesPaint ResolveSeriesPaint(
        ChartModel chart,
        int seriesIndex,
        WorkbookTheme theme,
        IReadOnlyList<CellColor> palette)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(theme);

        var format = FindSeriesFormat(chart, seriesIndex);
        var paletteColor = GetPaletteColor(palette, seriesIndex);
        var fill = format?.ResolveFillColor(theme)
            ?? format?.ResolveStrokeColor(theme)
            ?? paletteColor;
        var stroke = format?.ResolveStrokeColor(theme)
            ?? format?.ResolveFillColor(theme)
            ?? paletteColor;

        return new ChartSeriesPaint(fill, stroke);
    }

    public static ChartBarPaint ResolveBarPaint(
        ChartModel chart,
        int seriesIndex,
        WorkbookTheme theme,
        IReadOnlyList<CellColor> palette,
        double defaultStrokeThickness = 0.75)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(theme);

        var format = FindSeriesFormat(chart, seriesIndex);
        var paint = ResolveSeriesPaint(chart, seriesIndex, theme, palette);
        CellColor? fill = format?.NoFill == true ? null : paint.FillColor;

        if (format?.NoLine == true)
            return new ChartBarPaint(fill, null, 0);

        if (format is null)
            return new ChartBarPaint(fill, paint.StrokeColor, defaultStrokeThickness);

        if (HasExplicitStroke(format))
            return new ChartBarPaint(fill, paint.StrokeColor, format.StrokeThickness ?? defaultStrokeThickness);

        return new ChartBarPaint(fill, null, 0);
    }

    public static bool HasExplicitStroke(ChartSeriesFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return format.StrokeColor is not null
            || format.StrokeThemeColor is not null
            || format.StrokeThickness is not null;
    }

    private static ChartStyleGalleryOptionDescriptor[] CreateStyleOptions()
    {
        var options = new ChartStyleGalleryOptionDescriptor[MaxStyleId + 1];
        options[0] = new ChartStyleGalleryOptionDescriptor(
            null,
            "ChartStyle_AutomaticOption",
            "ChartStyle_AutomaticPreview");

        for (var styleId = MinStyleId; styleId <= MaxStyleId; styleId++)
        {
            options[styleId] = new ChartStyleGalleryOptionDescriptor(
                styleId,
                "ChartStyle_NumberedOption",
                "ChartStyle_NumberedPreview",
                styleId);
        }

        return options;
    }
}
