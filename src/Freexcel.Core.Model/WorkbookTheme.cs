namespace Freexcel.Core.Model;

/// <summary>
/// Workbook-level theme definition used by Excel-style colors, fonts, and effects.
/// </summary>
public sealed record WorkbookTheme(
    string Name,
    string MajorFontName,
    string MinorFontName,
    string EffectsName,
    IReadOnlyDictionary<WorkbookThemeColorSlot, CellColor> Colors)
{
    private static readonly IReadOnlyDictionary<WorkbookThemeColorSlot, CellColor> OfficeColors =
        new Dictionary<WorkbookThemeColorSlot, CellColor>
        {
            [WorkbookThemeColorSlot.Dark1] = new(0, 0, 0),
            [WorkbookThemeColorSlot.Light1] = new(255, 255, 255),
            [WorkbookThemeColorSlot.Dark2] = new(68, 84, 106),
            [WorkbookThemeColorSlot.Light2] = new(231, 230, 230),
            [WorkbookThemeColorSlot.Accent1] = new(21, 96, 130),
            [WorkbookThemeColorSlot.Accent2] = new(233, 113, 50),
            [WorkbookThemeColorSlot.Accent3] = new(25, 107, 36),
            [WorkbookThemeColorSlot.Accent4] = new(15, 158, 213),
            [WorkbookThemeColorSlot.Accent5] = new(160, 43, 147),
            [WorkbookThemeColorSlot.Accent6] = new(78, 167, 46),
            [WorkbookThemeColorSlot.Hyperlink] = new(5, 99, 193),
            [WorkbookThemeColorSlot.FollowedHyperlink] = new(149, 79, 114)
        };

    public static WorkbookTheme Office { get; } =
        new("Office", "Aptos Display", "Aptos", "Office", OfficeColors);

    public CellColor GetColor(WorkbookThemeColorSlot slot) =>
        Colors.TryGetValue(slot, out var color)
            ? color
            : OfficeColors[slot];

    public CellColor ResolveColor(WorkbookThemeColorSlot slot, double tint = 0)
    {
        var color = GetColor(slot);
        if (Math.Abs(tint) < 0.000001)
            return color;

        return new CellColor(
            ApplyTint(color.R, tint),
            ApplyTint(color.G, tint),
            ApplyTint(color.B, tint));
    }

    public WorkbookTheme WithName(string name) =>
        this with { Name = string.IsNullOrWhiteSpace(name) ? Office.Name : name.Trim() };

    public WorkbookTheme WithFonts(string majorFontName, string minorFontName) =>
        this with
        {
            MajorFontName = string.IsNullOrWhiteSpace(majorFontName) ? Office.MajorFontName : majorFontName.Trim(),
            MinorFontName = string.IsNullOrWhiteSpace(minorFontName) ? Office.MinorFontName : minorFontName.Trim()
        };

    public WorkbookTheme WithEffects(string effectsName) =>
        this with { EffectsName = string.IsNullOrWhiteSpace(effectsName) ? Office.EffectsName : effectsName.Trim() };

    public WorkbookTheme WithColor(WorkbookThemeColorSlot slot, CellColor color)
    {
        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>(Colors)
        {
            [slot] = color
        };
        return this with { Colors = colors };
    }

    private static byte ApplyTint(byte channel, double tint)
    {
        var value = tint < 0
            ? channel * (1.0 + tint)
            : channel + ((255 - channel) * tint);
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }
}

public enum WorkbookThemeColorSlot
{
    Dark1,
    Light1,
    Dark2,
    Light2,
    Accent1,
    Accent2,
    Accent3,
    Accent4,
    Accent5,
    Accent6,
    Hyperlink,
    FollowedHyperlink
}

public readonly record struct WorkbookThemeColorReference(
    WorkbookThemeColorSlot Slot,
    double Tint = 0)
{
    public CellColor Resolve(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.ResolveColor(Slot, Tint);
    }
}
