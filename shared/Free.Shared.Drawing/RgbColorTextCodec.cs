using System.Globalization;

namespace Free.Shared.Drawing;

/// <summary>
/// Selects the accepted text grammar for an sRGB color.
/// </summary>
public enum RgbColorTextProfile
{
    /// <summary>Six hexadecimal digits after trimming whitespace and any leading hash characters.</summary>
    DrawingMl,

    /// <summary>Six hexadecimal digits, optionally prefixed by one hash, after trimming whitespace.</summary>
    TrimmedHashOrBare,

    /// <summary>A six-character playback token whose trimmed digits retain integer-parser zero padding.</summary>
    PlaybackSixCharacter,

    /// <summary>A normalized six-digit caption payload with no hash prefix.</summary>
    CaptionPayload,

    /// <summary>Ink input accepting three, six, or alpha-prefixed eight digits plus hash or 0x prefixes.</summary>
    FlexibleInk,
}

/// <summary>
/// Parses renderer-neutral sRGB text while keeping each persisted or authored grammar explicit.
/// </summary>
public static class RgbColorTextCodec
{
    public static bool TryParse(
        string? text,
        RgbColorTextProfile profile,
        out DrawingMlRgbColor color)
    {
        color = default;
        if (!TryNormalize(text, profile, out var normalized))
            return false;

        if (!byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new DrawingMlRgbColor(red, green, blue);
        return true;
    }

    private static bool TryNormalize(
        string? text,
        RgbColorTextProfile profile,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        normalized = profile switch
        {
            RgbColorTextProfile.DrawingMl => text.Trim().TrimStart('#'),
            RgbColorTextProfile.TrimmedHashOrBare => RemoveOptionalHash(text.Trim()),
            RgbColorTextProfile.CaptionPayload => text,
            RgbColorTextProfile.PlaybackSixCharacter => NormalizePlayback(text),
            RgbColorTextProfile.FlexibleInk => NormalizeInk(text),
            _ => string.Empty,
        };

        return normalized.Length == 6;
    }

    private static string RemoveOptionalHash(string text) =>
        text.StartsWith('#') ? text[1..] : text;

    private static string NormalizePlayback(string text)
    {
        if (text.Length != 6)
            return string.Empty;
        return text.Trim().PadLeft(6, '0');
    }

    private static string NormalizeInk(string text)
    {
        var normalized = text.Trim().TrimStart('#');
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        if (normalized.Length == 3)
            normalized = string.Concat(normalized.Select(character => new string(character, 2)));
        if (normalized.Length == 8)
            normalized = normalized[2..];
        return normalized;
    }
}
