namespace Free.Shared.Theme;

/// <summary>
/// A portable ARGB color with no UI-framework dependency.
/// </summary>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    /// <summary>
    /// Parses a hex string in the form <c>#RRGGBB</c> (A=255) or <c>#AARRGGBB</c>.
    /// </summary>
    public static ThemeColor FromHex(string hex)
    {
        if (hex is null) throw new ArgumentNullException(nameof(hex));
        var s = hex.TrimStart('#');
        return s.Length switch
        {
            6 => new ThemeColor(
                A: 255,
                R: Convert.ToByte(s[..2], 16),
                G: Convert.ToByte(s[2..4], 16),
                B: Convert.ToByte(s[4..6], 16)),
            8 => new ThemeColor(
                A: Convert.ToByte(s[..2], 16),
                R: Convert.ToByte(s[2..4], 16),
                G: Convert.ToByte(s[4..6], 16),
                B: Convert.ToByte(s[6..8], 16)),
            _ => throw new FormatException($"Unsupported hex color format: '{hex}'. Expected #RRGGBB or #AARRGGBB.")
        };
    }

    /// <summary>
    /// Returns <c>#RRGGBB</c> when A == 255, otherwise <c>#AARRGGBB</c>.
    /// Always round-trips with <see cref="FromHex"/>.
    /// </summary>
    public string ToHex() =>
        A == 255
            ? $"#{R:X2}{G:X2}{B:X2}"
            : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
