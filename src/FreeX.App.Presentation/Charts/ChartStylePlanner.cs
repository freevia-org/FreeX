using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

public readonly record struct ChartSeriesPaint(CellColor FillColor, CellColor StrokeColor);

public readonly record struct ChartBarPaint(CellColor? FillColor, CellColor? StrokeColor, double StrokeThickness)
{
    public bool HasFill => FillColor is not null;
    public bool HasStroke => StrokeColor is not null && StrokeThickness > 0;
}

/// <summary>
/// UI-free style decisions shared by chart renderers. Hosts convert the returned colors into
/// their drawing primitives, but palette ordering and format precedence stay in one place.
/// </summary>
public static class ChartStylePlanner
{
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
}
