namespace Free.Shared.Drawing;

public enum DrawingMlThemeColorKind
{
    Srgb,
    System,
    ScRgb,
    Hsl,
    Preset
}

public readonly record struct DrawingMlThemeColor(
    DrawingMlRgbColor ResolvedColor,
    DrawingMlThemeColorKind Kind,
    string? Value = null,
    string? FallbackValue = null,
    DrawingMlRgbColor? BaseColor = null);

public sealed class DrawingMlThemeColorScheme
{
    private readonly DrawingMlThemeColor?[] _slots = new DrawingMlThemeColor?[12];

    public string? Name { get; init; }

    public DrawingMlThemeColor? this[DrawingMlThemeColorSlot slot]
    {
        get => _slots[(int)slot];
        set => _slots[(int)slot] = value;
    }
}

public sealed record DrawingMlThemeFontScheme(
    string? MajorLatinTypeface,
    string? MinorLatinTypeface);

public sealed record DrawingMlTheme(
    string? Name,
    DrawingMlThemeColorScheme ColorScheme,
    DrawingMlThemeFontScheme FontScheme,
    string? FormatSchemeName,
    string? NativeColorSchemeXml,
    string? NativeFontSchemeXml,
    string? NativeFormatSchemeXml)
{
    public static DrawingMlTheme Empty { get; } = new(
        null,
        new DrawingMlThemeColorScheme(),
        new DrawingMlThemeFontScheme(null, null),
        null,
        null,
        null,
        null);
}
