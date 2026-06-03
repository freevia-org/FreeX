using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.Model;

/// <summary>
/// Workbook-level theme definition used by Excel-style colors, fonts, and effects.
/// </summary>
public sealed record WorkbookTheme(
    string Name,
    string MajorFontName,
    string MinorFontName,
    string EffectsName,
    IReadOnlyDictionary<WorkbookThemeColorSlot, CellColor> Colors,
    string? NativeColorSchemeXml = null,
    string? NativeFontSchemeXml = null,
    string? NativeFormatSchemeXml = null,
    string? NativeThemeSupplementXml = null,
    IReadOnlyList<WorkbookThemeAlternateColorScheme> AlternateColorSchemes = null!,
    bool HasObjectDefaults = false,
    WorkbookThemeObjectDefaults? ObjectDefaults = null,
    WorkbookThemeEffectDefaults? EffectDefaults = null)
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
        new("Office", "Aptos Display", "Aptos", "Office", OfficeColors, AlternateColorSchemes: []);

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
            MinorFontName = string.IsNullOrWhiteSpace(minorFontName) ? Office.MinorFontName : minorFontName.Trim(),
            NativeFontSchemeXml = null
        };

    public WorkbookTheme WithEffects(string effectsName)
    {
        var normalizedEffectsName = string.IsNullOrWhiteSpace(effectsName) ? Office.EffectsName : effectsName.Trim();
        var renamedFormatSchemeXml = RenameNativeFormatScheme(NativeFormatSchemeXml, normalizedEffectsName);
        return this with
        {
            EffectsName = normalizedEffectsName,
            NativeFormatSchemeXml = renamedFormatSchemeXml,
            EffectDefaults = ReadFormatSchemeEffectDefaults(renamedFormatSchemeXml)
        };
    }

    public WorkbookTheme WithNativeFormatSchemeXml(string? formatSchemeXml) =>
        this with
        {
            NativeFormatSchemeXml = string.IsNullOrWhiteSpace(formatSchemeXml) ? null : formatSchemeXml.Trim(),
            EffectDefaults = ReadFormatSchemeEffectDefaults(formatSchemeXml)
        };

    public WorkbookTheme WithNativeColorSchemeXml(string? colorSchemeXml) =>
        this with
        {
            NativeColorSchemeXml = string.IsNullOrWhiteSpace(colorSchemeXml) ? null : colorSchemeXml.Trim()
        };

    public WorkbookTheme WithNativeFontSchemeXml(string? fontSchemeXml) =>
        this with
        {
            NativeFontSchemeXml = string.IsNullOrWhiteSpace(fontSchemeXml) ? null : fontSchemeXml.Trim()
        };

    public WorkbookTheme WithNativeThemeSupplementXml(string? themeSupplementXml) =>
        this with
        {
            NativeThemeSupplementXml = string.IsNullOrWhiteSpace(themeSupplementXml) ? null : themeSupplementXml.Trim()
        };

    public WorkbookTheme WithSupplementalMetadata(
        IReadOnlyList<WorkbookThemeAlternateColorScheme>? alternateColorSchemes,
        bool hasObjectDefaults,
        WorkbookThemeObjectDefaults? objectDefaults = null) =>
        this with
        {
            AlternateColorSchemes = alternateColorSchemes?.ToArray() ?? [],
            HasObjectDefaults = hasObjectDefaults || objectDefaults is not null,
            ObjectDefaults = objectDefaults
        };

    public WorkbookTheme WithColor(WorkbookThemeColorSlot slot, CellColor color)
    {
        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>(Colors)
        {
            [slot] = color
        };
        return this with { Colors = colors, NativeColorSchemeXml = null };
    }

    private static byte ApplyTint(byte channel, double tint)
    {
        var value = tint < 0
            ? channel * (1.0 + tint)
            : channel + ((255 - channel) * tint);
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    private static string? RenameNativeFormatScheme(string? formatSchemeXml, string effectsName)
    {
        if (string.IsNullOrWhiteSpace(formatSchemeXml))
            return null;

        try
        {
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var formatScheme = XElement.Parse(formatSchemeXml);
            if (formatScheme.Name != drawingNs + "fmtScheme")
                return null;

            var renamedFormatScheme = new XElement(formatScheme);
            renamedFormatScheme.SetAttributeValue("name", effectsName);
            return renamedFormatScheme.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return null;
        }
    }

    private static WorkbookThemeEffectDefaults? ReadFormatSchemeEffectDefaults(string? formatSchemeXml)
    {
        if (string.IsNullOrWhiteSpace(formatSchemeXml))
            return null;

        try
        {
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var formatScheme = XElement.Parse(formatSchemeXml);
            if (formatScheme.Name != drawingNs + "fmtScheme")
                return null;

            var effectStyleList = formatScheme.Element(drawingNs + "effectStyleLst");
            if (effectStyleList is null)
                return null;

            XElement? shadowEffect = null;
            XElement? glowEffect = null;
            foreach (var effectStyle in effectStyleList.Elements(drawingNs + "effectStyle"))
            {
                shadowEffect = FindThemeShadow(effectStyle, drawingNs);
                glowEffect = FindThemeGlow(effectStyle, drawingNs);
                if (shadowEffect is not null || glowEffect is not null)
                    break;
            }

            if (shadowEffect is null && glowEffect is null)
                return null;

            var shadowOpacity = 0d;
            var offsetX = 0d;
            var offsetY = 0d;
            if (shadowEffect is not null)
            {
                shadowOpacity = ReadEffectOpacity(shadowEffect, drawingNs);
                var distancePixels = ReadPositiveCoordinatePixels(shadowEffect.Attribute("dist")?.Value);
                var directionRadians = ReadAngleRadians(shadowEffect.Attribute("dir")?.Value);
                offsetX = CleanZero(Math.Round(Math.Cos(directionRadians) * distancePixels, 3));
                offsetY = CleanZero(Math.Round(Math.Sin(directionRadians) * distancePixels, 3));
            }

            var glowOpacity = glowEffect is null ? 0d : ReadEffectOpacity(glowEffect, drawingNs);
            var glowRadius = glowEffect is null ? 0d : ReadPositiveCoordinatePixels(glowEffect.Attribute("rad")?.Value);
            var glowColor = glowEffect is null ? null : ReadEffectSrgbColor(glowEffect, drawingNs);

            return new WorkbookThemeEffectDefaults(
                shadowOpacity,
                offsetX,
                offsetY,
                glowOpacity,
                glowRadius,
                glowColor);
        }
        catch
        {
            return null;
        }
    }

    private static XElement? FindThemeShadow(XElement effectStyle, XNamespace drawingNs) =>
        effectStyle
            .Element(drawingNs + "effectLst")?
            .Elements()
            .FirstOrDefault(effect => IsThemeShadow(effect, drawingNs))
        ?? effectStyle
            .Element(drawingNs + "effectDag")?
            .Descendants()
            .FirstOrDefault(effect => IsThemeShadow(effect, drawingNs));

    private static bool IsThemeShadow(XElement effect, XNamespace drawingNs) =>
        effect.Name == drawingNs + "outerShdw" ||
        effect.Name == drawingNs + "prstShdw";

    private static XElement? FindThemeGlow(XElement effectStyle, XNamespace drawingNs) =>
        effectStyle
            .Element(drawingNs + "effectLst")?
            .Elements()
            .FirstOrDefault(effect => effect.Name == drawingNs + "glow")
        ?? effectStyle
            .Element(drawingNs + "effectDag")?
            .Descendants()
            .FirstOrDefault(effect => effect.Name == drawingNs + "glow");

    private static double ReadEffectOpacity(XElement effect, XNamespace drawingNs)
    {
        var alphaText = effect
            .Elements()
            .Select(color => color.Element(drawingNs + "alpha")?.Attribute("val")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return int.TryParse(alphaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var alpha)
            ? Math.Clamp(alpha / 100000d, 0, 1)
            : 1;
    }

    private static CellColor? ReadEffectSrgbColor(XElement effect, XNamespace drawingNs)
    {
        var value = effect
            .Elements(drawingNs + "srgbClr")
            .Select(color => color.Attribute("val")?.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        if (value is not { Length: 6 } ||
            !byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return null;
        }

        return new CellColor(red, green, blue);
    }

    private static double ReadPositiveCoordinatePixels(string? coordinateText)
    {
        if (!double.TryParse(
                coordinateText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var coordinate) ||
            coordinate <= 0)
        {
            return 0;
        }

        const double emusPerInch = 914400d;
        const double pixelsPerInch = 96d;
        return coordinate / emusPerInch * pixelsPerInch;
    }

    private static double ReadAngleRadians(string? angleText)
    {
        if (!double.TryParse(
                angleText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var angle))
        {
            return 0;
        }

        return angle / 60000d * Math.PI / 180d;
    }

    private static double CleanZero(double value) =>
        Math.Abs(value) < 0.0005 ? 0 : value;
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

public sealed record WorkbookThemeAlternateColorScheme(
    string Name,
    IReadOnlyDictionary<WorkbookThemeColorSlot, CellColor> Colors,
    string? NativeColorSchemeXml = null)
{
    public CellColor? GetColor(WorkbookThemeColorSlot slot) =>
        Colors.TryGetValue(slot, out var color)
            ? color
            : null;
}

public sealed record WorkbookThemeObjectDefaults(
    WorkbookThemeShapeObjectDefault? Shape = null,
    WorkbookThemeLineObjectDefault? Line = null,
    WorkbookThemeTextObjectDefault? Text = null,
    string? NativeObjectDefaultsXml = null)
{
    public bool HasModeledDefaults => Shape is not null || Line is not null || Text is not null;
}

public sealed record WorkbookThemeShapeObjectDefault(
    WorkbookThemeColorReference? FillThemeColor = null,
    CellColor? FillColor = null,
    WorkbookThemeColorReference? OutlineThemeColor = null,
    CellColor? OutlineColor = null,
    double? OutlineWidthPoints = null);

public sealed record WorkbookThemeLineObjectDefault(
    WorkbookThemeColorReference? StrokeThemeColor = null,
    CellColor? StrokeColor = null,
    double? StrokeWidthPoints = null);

public sealed record WorkbookThemeTextObjectDefault(
    WorkbookThemeColorReference? TextThemeColor = null,
    CellColor? TextColor = null,
    string? Typeface = null);

public sealed record WorkbookThemeEffectDefaults(
    double ShadowOpacity = 0,
    double ShadowOffsetX = 0,
    double ShadowOffsetY = 0,
    double GlowOpacity = 0,
    double GlowRadius = 0,
    CellColor? GlowColor = null)
{
    public bool HasShadow => ShadowOpacity > 0;
    public bool HasGlow => GlowOpacity > 0 && GlowRadius > 0;
    public bool HasAnyEffect => HasShadow || HasGlow;
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
