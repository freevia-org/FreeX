using FreeX.Core.Model;
using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class ColorPickerPalettePlanner
{
    public static IReadOnlyList<ColorPickerSwatch> BuildDefaultSwatches() =>
        CellColorPalettePlanner.BuildDefaultSwatches().Select(ToHostSwatch).ToList();

    public static IReadOnlyList<ColorPickerThemeColumn> BuildThemePalette() =>
        CellColorPalettePlanner.BuildThemePalette()
            .Select(column => new ColorPickerThemeColumn(column.Name, column.Shades.Select(ToHostSwatch).ToList()))
            .ToList();

    public static IReadOnlyList<ColorPickerSwatch> BuildStandardSwatches() =>
        CellColorPalettePlanner.BuildStandardSwatches().Select(ToHostSwatch).ToList();

    public static IReadOnlyList<ColorPickerSwatch> BuildCustomSpectrumSwatches() =>
        CellColorPalettePlanner.BuildCustomSpectrumSwatches().Select(ToHostSwatch).ToList();

    public static CellColor ScaleColor(CellColor baseColor, double factor) =>
        CellColorPalettePlanner.ScaleColor(baseColor, factor);

    public static bool NeedsDarkForeground(CellColor color) =>
        CellColorPalettePlanner.NeedsDarkForeground(color);

    private static ColorPickerSwatch ToHostSwatch(CellColorSwatch swatch) =>
        new(swatch.Hex, swatch.Color);
}
