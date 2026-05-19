using System.Globalization;
using Freexcel.Core.Model;
using IOPath = System.IO.Path;

namespace Freexcel.App.Host;

public static class DrawingInputParser
{
    public static string GetImageContentType(string fileName) =>
        IOPath.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "image/png"
        };

    public static string FormatCropPercent(double value) =>
        Math.Round(value * 100, 1).ToString("0.#", CultureInfo.CurrentCulture);

    public static bool TryParseCropPercent(string text, out double value)
    {
        value = 0;
        text = text.Trim().TrimEnd('%');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            return false;
        }

        if (!double.IsFinite(percent) || percent < 0 || percent >= 100)
            return false;

        value = percent / 100.0;
        return true;
    }

    public static bool TryParseRgbColor(string input, out CellColor color)
    {
        var parts = input.Split(',');
        if (parts.Length == 3 &&
            byte.TryParse(parts[0].Trim(), out var r) &&
            byte.TryParse(parts[1].Trim(), out var g) &&
            byte.TryParse(parts[2].Trim(), out var b))
        {
            color = new CellColor(r, g, b);
            return true;
        }

        color = default;
        return false;
    }
}
