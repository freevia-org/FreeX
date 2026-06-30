using System.Globalization;

namespace Free.Shared.Drawing;

/// <summary>
/// sRGB color used by shared DrawingML color transforms.
/// </summary>
public readonly record struct DrawingMlRgbColor(byte R, byte G, byte B)
{
    public static DrawingMlRgbColor FromRgb(int rgb) =>
        new(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));

    public static bool TryParseHexRgb(string? text, out DrawingMlRgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().TrimStart('#');
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new DrawingMlRgbColor(r, g, b);
        return true;
    }

    public string ToHexRgb() => $"{R:X2}{G:X2}{B:X2}";

    public static readonly DrawingMlRgbColor Black = new(0, 0, 0);
    public static readonly DrawingMlRgbColor White = new(0xFF, 0xFF, 0xFF);

    public override string ToString() => $"#{ToHexRgb()}";
}
