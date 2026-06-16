namespace Free.Shared.Ribbon.Icons;

/// <summary>
/// A platform-neutral, immutable sRGB color (no WPF/Avalonia types). Renderers convert this
/// into their native brush/color when drawing an accented icon element.
/// </summary>
public readonly record struct RibbonIconColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Parses a <c>#RRGGBB</c> or <c>#AARRGGBB</c> hex string.</summary>
    public static RibbonIconColor FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var span = hex.AsSpan().TrimStart('#');
        if (span.Length == 6)
        {
            return new RibbonIconColor(
                Convert.ToByte(span[..2].ToString(), 16),
                Convert.ToByte(span.Slice(2, 2).ToString(), 16),
                Convert.ToByte(span.Slice(4, 2).ToString(), 16));
        }

        if (span.Length == 8)
        {
            return new RibbonIconColor(
                Convert.ToByte(span.Slice(2, 2).ToString(), 16),
                Convert.ToByte(span.Slice(4, 2).ToString(), 16),
                Convert.ToByte(span.Slice(6, 2).ToString(), 16),
                Convert.ToByte(span[..2].ToString(), 16));
        }

        throw new FormatException($"'{hex}' is not a #RRGGBB or #AARRGGBB color.");
    }
}

/// <summary>Maps a <see cref="RibbonCommandIconAccent"/> to a neutral color value.</summary>
public static class RibbonIconAccents
{
    /// <summary>
    /// Returns the accent color for the given accent, or <c>null</c> for <see cref="RibbonCommandIconAccent.None"/>
    /// (meaning: draw with the caller-supplied glyph color).
    /// </summary>
    public static RibbonIconColor? Resolve(RibbonCommandIconAccent accent) => accent switch
    {
        RibbonCommandIconAccent.None => null,
        RibbonCommandIconAccent.Green => RibbonIconColor.FromHex("#107C10"),
        RibbonCommandIconAccent.Chart => RibbonIconColor.FromHex("#217346"),
        RibbonCommandIconAccent.Data => RibbonIconColor.FromHex("#0F6CBD"),
        RibbonCommandIconAccent.Theme => RibbonIconColor.FromHex("#5C2D91"),
        RibbonCommandIconAccent.Fill => RibbonIconColor.FromHex("#D83B01"),
        RibbonCommandIconAccent.Color => RibbonIconColor.FromHex("#C239B3"),
        RibbonCommandIconAccent.Border => RibbonIconColor.FromHex("#605E5C"),
        RibbonCommandIconAccent.Warning => RibbonIconColor.FromHex("#D13438"),
        RibbonCommandIconAccent.Protect => RibbonIconColor.FromHex("#498205"),
        RibbonCommandIconAccent.Help => RibbonIconColor.FromHex("#0F6CBD"),
        _ => null,
    };
}
