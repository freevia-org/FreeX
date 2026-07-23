using Avalonia;
using Free.Shared.Shell;

namespace FreeW.App.Avalonia;

internal readonly record struct FreeWAvaloniaWindowTile(
    PixelPoint Position,
    double Width,
    double Height);

/// <summary>
/// Converts neutral shared-shell rectangles into Avalonia's pixel-position/DIP-size boundary shape.
/// It deliberately owns no tiling policy; <see cref="ArrangeAllLayoutPlanner"/> supplies the rectangles.
/// </summary>
internal static class FreeWAvaloniaWindowBoundsTranslator
{
    public static IReadOnlyList<FreeWAvaloniaWindowTile> Translate(
        PixelRect workingArea,
        double scaling,
        IReadOnlyList<ShellRect> bounds)
    {
        if (bounds.Count == 0)
            return [];

        var dipScaling = scaling > 0 ? scaling : 1;
        var tiles = new FreeWAvaloniaWindowTile[bounds.Count];
        for (var index = 0; index < bounds.Count; index++)
        {
            var bound = bounds[index];
            var left = ToPixel(bound.X, dipScaling);
            var right = ToPixel(bound.X + bound.Width, dipScaling);
            var top = ToPixel(bound.Y, dipScaling);
            var bottom = ToPixel(bound.Y + bound.Height, dipScaling);

            tiles[index] = new FreeWAvaloniaWindowTile(
                new PixelPoint(workingArea.X + left, workingArea.Y + top),
                Math.Max(0, right - left) / dipScaling,
                Math.Max(0, bottom - top) / dipScaling);
        }

        return tiles;
    }

    private static int ToPixel(double dip, double scaling) =>
        (int)Math.Round(dip * scaling, MidpointRounding.AwayFromZero);
}
