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

    /// <summary>
    /// Returns the theme font name for the given font scheme, or null when the scheme is None.
    /// </summary>
    public string? ResolveSchemeFontName(CellFontScheme scheme) => scheme switch
    {
        CellFontScheme.Minor => MinorFontName,
        CellFontScheme.Major => MajorFontName,
        _ => null,
    };

    public CellColor GetColor(WorkbookThemeColorSlot slot) =>
        Colors.TryGetValue(slot, out var color)
            ? color
            : OfficeColors[slot];

    public CellColor ResolveColor(WorkbookThemeColorSlot slot, double tint = 0)
    {
        var color = GetColor(slot);
        return WorkbookThemeTint.Apply(color, tint);
    }

    public WorkbookTheme WithName(string name) =>
        this with { Name = string.IsNullOrWhiteSpace(name) ? Office.Name : name.Trim() };

    public WorkbookTheme WithFonts(string majorFontName, string minorFontName)
    {
        var normalizedMajor = string.IsNullOrWhiteSpace(majorFontName) ? Office.MajorFontName : majorFontName.Trim();
        var normalizedMinor = string.IsNullOrWhiteSpace(minorFontName) ? Office.MinorFontName : minorFontName.Trim();

        // Mirror WithEffects/RenameNativeFormatScheme: rather than discarding the source fontScheme XML
        // (which would drop its East-Asian <a:ea>/complex-script <a:cs> typefaces and re-emit them empty),
        // patch only the major/minor <a:latin> typefaces in place and preserve everything else.
        return this with
        {
            MajorFontName = normalizedMajor,
            MinorFontName = normalizedMinor,
            NativeFontSchemeXml = DrawingMlThemeXml
                .TryPatchNativeFontScheme(NativeFontSchemeXml, normalizedMajor, normalizedMinor)?
                .ToString(SaveOptions.DisableFormatting)
        };
    }

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
        return this with
        {
            Colors = colors,
            NativeColorSchemeXml = PatchNativeColorScheme(NativeColorSchemeXml, slot, color)
        };
    }

    // Mirror WithFonts/WithEffects: patch only the changed slot's <a:srgbClr> in place rather than
    // discarding the whole native clrScheme XML. Discarding it would make the writer regenerate all
    // 12 slots from scratch, converting untouched sysClr entries (e.g. dk1/lt1 bound to
    // windowText/window) into baked srgbClr values and dropping the clrScheme "name" attribute.
    private static string? PatchNativeColorScheme(string? colorSchemeXml, WorkbookThemeColorSlot slot, CellColor color)
    {
        var colorScheme = TryParseColorScheme(colorSchemeXml);
        if (colorScheme is null)
            return null;

        var elementName = ColorSlotElementName(slot);
        if (elementName is null)
            return colorScheme.ToString(SaveOptions.DisableFormatting);

        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var patchedColorScheme = new XElement(colorScheme);
        var newSlotElement = new XElement(drawingNs + elementName,
            new XElement(drawingNs + "srgbClr",
                new XAttribute("val", new DrawingMlRgbColor(color.R, color.G, color.B).ToHexRgb())));

        var existingSlotElement = patchedColorScheme.Element(drawingNs + elementName);
        if (existingSlotElement is not null)
            existingSlotElement.ReplaceWith(newSlotElement);
        else
            patchedColorScheme.Add(newSlotElement);

        return patchedColorScheme.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement? TryParseColorScheme(string? colorSchemeXml)
    {
        if (string.IsNullOrWhiteSpace(colorSchemeXml))
            return null;

        try
        {
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var colorScheme = XElement.Parse(colorSchemeXml);
            return colorScheme.Name == drawingNs + "clrScheme" ? colorScheme : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ColorSlotElementName(WorkbookThemeColorSlot slot) => slot switch
    {
        WorkbookThemeColorSlot.Dark1 => "dk1",
        WorkbookThemeColorSlot.Light1 => "lt1",
        WorkbookThemeColorSlot.Dark2 => "dk2",
        WorkbookThemeColorSlot.Light2 => "lt2",
        WorkbookThemeColorSlot.Accent1 => "accent1",
        WorkbookThemeColorSlot.Accent2 => "accent2",
        WorkbookThemeColorSlot.Accent3 => "accent3",
        WorkbookThemeColorSlot.Accent4 => "accent4",
        WorkbookThemeColorSlot.Accent5 => "accent5",
        WorkbookThemeColorSlot.Accent6 => "accent6",
        WorkbookThemeColorSlot.Hyperlink => "hlink",
        WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
        _ => null
    };

    private static string? RenameNativeFormatScheme(string? formatSchemeXml, string effectsName)
    {
        var formatScheme = TryParseFormatScheme(formatSchemeXml);
        if (formatScheme is null)
            return null;

        var renamedFormatScheme = new XElement(formatScheme);
        renamedFormatScheme.SetAttributeValue("name", effectsName);
        return renamedFormatScheme.ToString(SaveOptions.DisableFormatting);
    }

    private static WorkbookThemeEffectDefaults? ReadFormatSchemeEffectDefaults(string? formatSchemeXml)
    {
        var formatScheme = TryParseFormatScheme(formatSchemeXml);
        if (formatScheme is null)
            return null;

        try
        {
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var effectStyleList = formatScheme.Element(drawingNs + "effectStyleLst");
            if (effectStyleList is null)
                return null;

            XElement? shadowEffect = null;
            XElement? glowEffect = null;
            (double Opacity, double OffsetX, double OffsetY, double BlurRadius)? innerShadow = null;
            var softEdgeRadius = 0d;
            var hasBevel = false;
            var hasThreeDRotation = false;
            foreach (var effectStyle in effectStyleList.Elements(drawingNs + "effectStyle"))
            {
                var candidateShadow = FindThemeShadow(effectStyle, drawingNs);
                var candidateGlow = FindThemeGlow(effectStyle, drawingNs);
                var candidateInnerShadow = ReadThemeInnerShadow(effectStyle, drawingNs);
                var candidateSoftEdgeRadius = ReadPositiveCoordinatePixels(
                    FindThemeSoftEdge(effectStyle, drawingNs)?.Attribute("rad")?.Value);
                var candidateHasBevel = HasThemeBevel(effectStyle, drawingNs);
                var candidateHasThreeDRotation = HasThemeThreeDRotation(effectStyle, drawingNs);
                if (candidateShadow is not null ||
                    candidateGlow is not null ||
                    candidateInnerShadow is not null ||
                    candidateSoftEdgeRadius > 0 ||
                    candidateHasBevel ||
                    candidateHasThreeDRotation)
                {
                    shadowEffect = candidateShadow;
                    glowEffect = candidateGlow;
                    innerShadow = candidateInnerShadow;
                    softEdgeRadius = candidateSoftEdgeRadius;
                    hasBevel = candidateHasBevel;
                    hasThreeDRotation = candidateHasThreeDRotation;
                    break;
                }
            }

            if (shadowEffect is null &&
                glowEffect is null &&
                innerShadow is null &&
                softEdgeRadius <= 0 &&
                !hasBevel &&
                !hasThreeDRotation)
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
                glowColor,
                softEdgeRadius,
                innerShadow?.Opacity ?? 0,
                innerShadow?.OffsetX ?? 0,
                innerShadow?.OffsetY ?? 0,
                innerShadow?.BlurRadius ?? 0,
                hasBevel,
                hasThreeDRotation);
        }
        catch
        {
            return null;
        }
    }

    private static XElement? TryParseFormatScheme(string? formatSchemeXml)
    {
        if (string.IsNullOrWhiteSpace(formatSchemeXml))
            return null;

        try
        {
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var formatScheme = XElement.Parse(formatSchemeXml);
            return formatScheme.Name == drawingNs + "fmtScheme" ? formatScheme : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasThemeBevel(XElement effectStyle, XNamespace drawingNs) =>
        effectStyle
            .Element(drawingNs + "sp3d")?
            .Element(drawingNs + "bevelT") is not null;

    private static bool HasThemeThreeDRotation(XElement effectStyle, XNamespace drawingNs) =>
        effectStyle
            .Element(drawingNs + "scene3d")?
            .Element(drawingNs + "camera") is not null;

    private static XElement? FindThemeShadow(XElement effectStyle, XNamespace drawingNs) =>
        FindThemeEffect(effectStyle, drawingNs, drawingNs + "outerShdw", drawingNs + "prstShdw");

    private static XElement? FindThemeGlow(XElement effectStyle, XNamespace drawingNs) =>
        FindThemeEffectByName(effectStyle, drawingNs, "glow");

    private static XElement? FindThemeSoftEdge(XElement effectStyle, XNamespace drawingNs) =>
        FindThemeEffectByName(effectStyle, drawingNs, "softEdge");

    private static XElement? FindThemeEffectByName(
        XElement effectStyle,
        XNamespace drawingNs,
        string localName) =>
        FindThemeEffect(effectStyle, drawingNs, drawingNs + localName);

    private static XElement? FindThemeEffect(
        XElement effectStyle,
        XNamespace drawingNs,
        XName primaryName,
        XName? secondaryName = null)
    {
        var effectList = effectStyle.Element(drawingNs + "effectLst");
        if (effectList is not null)
        {
            foreach (var effect in effectList.Elements())
            {
                if (ThemeEffectNameMatches(effect.Name, primaryName, secondaryName))
                    return effect;
            }
        }

        var effectDag = effectStyle.Element(drawingNs + "effectDag");
        if (effectDag is not null)
        {
            foreach (var effect in effectDag.Descendants())
            {
                if (ThemeEffectNameMatches(effect.Name, primaryName, secondaryName))
                    return effect;
            }
        }

        return null;
    }

    private static bool ThemeEffectNameMatches(XName effectName, XName primaryName, XName? secondaryName) =>
        effectName == primaryName ||
        (secondaryName is not null && effectName == secondaryName);

    private static (double Opacity, double OffsetX, double OffsetY, double BlurRadius)? ReadThemeInnerShadow(
        XElement effectStyle,
        XNamespace drawingNs)
    {
        foreach (var effect in FindThemeInnerShadows(effectStyle, drawingNs))
        {
            var opacity = ReadPositiveEffectOpacity(effect, drawingNs);
            if (opacity is null)
                continue;

            var distancePixels = ReadPositiveCoordinatePixels(effect.Attribute("dist")?.Value);
            var directionRadians = ReadAngleRadians(effect.Attribute("dir")?.Value);
            var offsetX = CleanZero(Math.Round(Math.Cos(directionRadians) * distancePixels, 3));
            var offsetY = CleanZero(Math.Round(Math.Sin(directionRadians) * distancePixels, 3));
            var blurRadius = ReadPositiveCoordinatePixels(effect.Attribute("blurRad")?.Value);

            return (opacity.Value, offsetX, offsetY, blurRadius);
        }

        return null;
    }

    private static IEnumerable<XElement> FindThemeInnerShadows(XElement effectStyle, XNamespace drawingNs)
    {
        var effectList = effectStyle.Element(drawingNs + "effectLst");
        if (effectList is not null)
        {
            foreach (var effect in effectList.Elements())
            {
                if (effect.Name == drawingNs + "innerShdw")
                    yield return effect;
            }
        }

        var effectDag = effectStyle.Element(drawingNs + "effectDag");
        if (effectDag is not null)
        {
            foreach (var effect in effectDag.Descendants())
            {
                if (effect.Name == drawingNs + "innerShdw")
                    yield return effect;
            }
        }
    }

    private static double? ReadPositiveEffectOpacity(XElement effect, XNamespace drawingNs)
    {
        var opacity = ReadEffectOpacityOrNull(effect, drawingNs);
        return opacity is > 0 ? opacity : null;
    }

    private static double ReadEffectOpacity(XElement effect, XNamespace drawingNs) =>
        ReadEffectOpacityOrNull(effect, drawingNs) ?? 1;

    private static double? ReadEffectOpacityOrNull(XElement effect, XNamespace drawingNs)
    {
        string? alphaText = null;
        foreach (var color in effect.Elements())
        {
            var value = color.Element(drawingNs + "alpha")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                alphaText = value;
                break;
            }
        }

        return int.TryParse(alphaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var alpha)
            ? Math.Clamp(alpha / 100000d, 0, 1)
            : null;
    }

    private static CellColor? ReadEffectSrgbColor(XElement effect, XNamespace drawingNs)
    {
        string? value = null;
        foreach (var color in effect.Elements(drawingNs + "srgbClr"))
        {
            var candidate = color.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                break;
            }
        }

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

        return DrawingMlCoordinateUnits.EmuToPixels(coordinate);
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

        return DrawingMlCoordinateUnits.AngleToRadians(angle);
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
    CellColor? GlowColor = null,
    double SoftEdgeRadius = 0,
    double InnerShadowOpacity = 0,
    double InnerShadowOffsetX = 0,
    double InnerShadowOffsetY = 0,
    double InnerShadowBlurRadius = 0,
    bool HasBevel = false,
    bool HasThreeDRotation = false)
{
    public bool HasShadow => ShadowOpacity > 0;
    public bool HasGlow => GlowOpacity > 0 && GlowRadius > 0;
    public bool HasSoftEdge => SoftEdgeRadius > 0;
    public bool HasInnerShadow => InnerShadowOpacity > 0;
    public bool HasAnyEffect => HasShadow || HasGlow || HasSoftEdge || HasInnerShadow || HasBevel || HasThreeDRotation;
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
