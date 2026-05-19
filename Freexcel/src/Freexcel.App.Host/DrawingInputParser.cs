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

    public static bool TryParseSize(string input, out double width, out double height)
    {
        width = 0;
        height = 0;

        var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !TryParseFiniteDouble(parts[0], out width) ||
            !TryParseFiniteDouble(parts[1], out height))
        {
            width = 0;
            height = 0;
            return false;
        }

        return true;
    }

    public static bool TryParseRotationDegrees(string input, out double rotation)
    {
        if (TryParseFiniteDouble(input, out var parsed))
        {
            rotation = parsed;
            return true;
        }

        rotation = 0;
        return false;
    }

    public static bool TryParseCropPercents(
        string input,
        out double left,
        out double top,
        out double right,
        out double bottom)
    {
        left = 0;
        top = 0;
        right = 0;
        bottom = 0;

        var parts = input.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !TryParseCropPercent(parts[0], out left) ||
            !TryParseCropPercent(parts[1], out top) ||
            !TryParseCropPercent(parts[2], out right) ||
            !TryParseCropPercent(parts[3], out bottom) ||
            left + right >= 1 ||
            top + bottom >= 1)
        {
            left = 0;
            top = 0;
            right = 0;
            bottom = 0;
            return false;
        }

        return true;
    }

    public static bool TryParseGradientColors(string input, out CellColor startColor, out CellColor endColor)
    {
        startColor = default;
        endColor = default;

        var parts = input.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
               TryParseRgbColor(parts[0], out startColor) &&
               TryParseRgbColor(parts[1], out endColor);
    }

    public static string FormatPictureCellText(ScalarValue value) =>
        value switch
        {
            BlankValue => "",
            NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            TextValue text => text.Value,
            ErrorValue error => error.Code,
            _ => value.ToString() ?? ""
        };

    private static bool TryParseFiniteDouble(string input, out double value)
    {
        if (!double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
            !double.TryParse(input.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        if (double.IsFinite(value))
            return true;

        value = 0;
        return false;
    }
}
