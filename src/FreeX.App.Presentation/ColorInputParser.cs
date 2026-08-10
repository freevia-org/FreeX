using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public enum RgbTripletTextProfile
{
    CellEditor,
    ConditionalFormatting,
    DrawingInteraction
}

public static class ColorInputParser
{
    public static bool TryParseOptionalHexColor(string text, out CellColor? color)
    {
        color = null;
        var normalized = text.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryParseHexColor(normalized, out color);
    }

    public static bool TryParseColorText(string text, out CellColor color)
    {
        color = default;
        if (TryParseHexColor(text, out var hexColor) && hexColor is { } parsedHex)
        {
            color = parsedHex;
            return true;
        }

        if (TryParseRgbColorText(text, out var rgbColor))
        {
            color = rgbColor;
            return true;
        }

        return false;
    }

    public static bool TryParseRgbColorText(string text, out CellColor color)
        => TryParseRgbColorText(text, RgbTripletTextProfile.CellEditor, out color);

    public static bool TryParseRgbColorText(
        string? text,
        RgbTripletTextProfile profile,
        out CellColor color)
    {
        color = default;
        if (!TryParseRgbComponents(text, profile, out var r, out var g, out var b))
            return false;

        color = new CellColor(r, g, b);
        return true;
    }

    public static bool TryParseRgbColorText(
        string? text,
        RgbTripletTextProfile profile,
        out RgbColor color)
    {
        color = default;
        if (!TryParseRgbComponents(text, profile, out var r, out var g, out var b))
            return false;

        color = new RgbColor(r, g, b);
        return true;
    }

    public static string FormatRgbColor(CellColor color) =>
        FormatRgbComponents(color.R, color.G, color.B);

    public static string FormatRgbColor(RgbColor color) =>
        FormatRgbComponents(color.R, color.G, color.B);

    public static bool TryParseHexColor(string text, out CellColor? color)
    {
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length == 6 &&
            byte.TryParse(normalized[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            color = new CellColor(r, g, b);
            return true;
        }

        color = null;
        return false;
    }

    public static string FormatHexColor(CellColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseRgbComponents(
        string? text,
        RgbTripletTextProfile profile,
        out byte r,
        out byte g,
        out byte b)
    {
        r = 0;
        g = 0;
        b = 0;

        var componentCulture = profile switch
        {
            RgbTripletTextProfile.CellEditor or
            RgbTripletTextProfile.ConditionalFormatting => CultureInfo.InvariantCulture,
            RgbTripletTextProfile.DrawingInteraction => CultureInfo.CurrentCulture,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };

        if (text is null)
        {
            if (profile == RgbTripletTextProfile.ConditionalFormatting)
                return false;

            throw new NullReferenceException();
        }

        var parts = text.Trim().Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && byte.TryParse(parts[0], NumberStyles.Integer, componentCulture, out r)
            && byte.TryParse(parts[1], NumberStyles.Integer, componentCulture, out g)
            && byte.TryParse(parts[2], NumberStyles.Integer, componentCulture, out b);
    }

    private static string FormatRgbComponents(byte r, byte g, byte b) =>
        $"{r},{g},{b}";
}
