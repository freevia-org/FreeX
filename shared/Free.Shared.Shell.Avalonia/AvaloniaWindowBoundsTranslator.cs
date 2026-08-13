using Avalonia;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia;

public readonly record struct AvaloniaWindowTile(
    PixelPoint Position,
    double Width,
    double Height);

/// <summary>
/// Converts work-area-relative shell rectangles into Avalonia pixel positions and DIP sizes.
/// Tiling and minimum-size policy remain with the caller.
/// </summary>
public static class AvaloniaWindowBoundsTranslator
{
    public static AvaloniaWindowTile Translate(
        PixelRect workingArea,
        double scaling,
        ShellRect bounds)
    {
        var dipScaling = NormalizeScaling(scaling);
        return TranslateNormalized(workingArea, dipScaling, bounds);
    }

    public static IReadOnlyList<AvaloniaWindowTile> Translate(
        PixelRect workingArea,
        double scaling,
        IReadOnlyList<ShellRect> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        if (bounds.Count == 0)
            return [];

        var dipScaling = NormalizeScaling(scaling);
        var tiles = new AvaloniaWindowTile[bounds.Count];
        for (var index = 0; index < bounds.Count; index++)
            tiles[index] = TranslateNormalized(workingArea, dipScaling, bounds[index]);

        return tiles;
    }

    public static double PixelsToDips(double pixels, double scaling) =>
        pixels / NormalizeScaling(scaling);

    public static double NormalizeScaling(double scaling) =>
        double.IsFinite(scaling) && scaling > 0 ? scaling : 1;

    private static AvaloniaWindowTile TranslateNormalized(
        PixelRect workingArea,
        double scaling,
        ShellRect bounds)
    {
        var left = ToPixelEdge(bounds.X, scaling);
        var right = ToPixelEdge(bounds.X + bounds.Width, scaling);
        var top = ToPixelEdge(bounds.Y, scaling);
        var bottom = ToPixelEdge(bounds.Y + bounds.Height, scaling);

        return new AvaloniaWindowTile(
            new PixelPoint(workingArea.X + left, workingArea.Y + top),
            Math.Max(0, right - left) / scaling,
            Math.Max(0, bottom - top) / scaling);
    }

    private static int ToPixelEdge(double dips, double scaling) =>
        (int)Math.Round(dips * scaling, MidpointRounding.AwayFromZero);
}
