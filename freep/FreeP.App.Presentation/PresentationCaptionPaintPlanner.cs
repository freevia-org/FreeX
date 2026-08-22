using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

public readonly record struct PresentationCaptionPaint(
    byte Alpha,
    byte Red,
    byte Green,
    byte Blue);

public static class PresentationCaptionPaintPlanner
{
    public static PresentationCaptionPaint? Resolve(
        string? colorHex,
        double? opacity,
        bool fallbackToWhite)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            if (opacity is null || !fallbackToWhite)
                return null;

            return new PresentationCaptionPaint(ResolveAlpha(opacity), 0xFF, 0xFF, 0xFF);
        }

        if (!RgbColorTextCodec.TryParse(
                colorHex,
                RgbColorTextProfile.CaptionPayload,
                out var color))
        {
            return null;
        }

        return new PresentationCaptionPaint(
            ResolveAlpha(opacity),
            color.R,
            color.G,
            color.B);
    }

    private static byte ResolveAlpha(double? opacity) => opacity is { } value
        ? (byte)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue)
        : byte.MaxValue;
}
