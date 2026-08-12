using System.Windows.Media;
using Free.Shared.Drawing;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Converts shared sRGB values to WPF colors while retaining native WPF token support at the renderer edge.
/// </summary>
internal static class WpfRgbColorAdapter
{
    public static Color FromDrawingColor(DrawingMlRgbColor color) =>
        Color.FromRgb(color.R, color.G, color.B);

    public static bool TryParseDrawingMl(string? token, out Color color)
    {
        if (DrawingMlRgbColor.TryParseHexRgb(token, out var parsed))
        {
            color = FromDrawingColor(parsed);
            return true;
        }

        color = default;
        return false;
    }

    public static Color ParseDrawingMlOrDefault(string? token, Color fallback) =>
        TryParseDrawingMl(token, out var color) ? color : fallback;

    public static bool TryParseColorToken(string? token, out Color color)
    {
        if (TryParseDrawingMl(token, out color))
            return true;

        color = default;
        if (!IsWpfSpecificColorToken(token))
            return false;

        try
        {
            if (ColorConverter.ConvertFromString(token!.Trim()) is not Color parsed)
                return false;

            color = parsed;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException)
        {
            return false;
        }
    }

    public static Color ParseColorTokenOrDefault(string? token, Color fallback) =>
        TryParseColorToken(token, out var color) ? color : fallback;

    public static Color ParseColorToken(string? token)
    {
        if (TryParseColorToken(token, out var color))
            return color;

        throw new FormatException($"'{token}' is not a valid WPF color token.");
    }

    public static string ToHexRgb(Color color) =>
        new DrawingMlRgbColor(color.R, color.G, color.B).ToString();

    private static bool IsWpfSpecificColorToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        if (!trimmed.StartsWith('#'))
            return true;

        return trimmed.Length is 4 or 5 or 9;
    }
}
