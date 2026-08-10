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

    public static bool TryParseHexRgb(string? text, out DrawingMlRgbColor color) =>
        RgbColorTextCodec.TryParse(text, RgbColorTextProfile.DrawingMl, out color);

    public string ToHexRgb() => $"{R:X2}{G:X2}{B:X2}";

    public static readonly DrawingMlRgbColor Black = new(0, 0, 0);
    public static readonly DrawingMlRgbColor White = new(0xFF, 0xFF, 0xFF);

    public override string ToString() => $"#{ToHexRgb()}";
}
