using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Drawing;

namespace Free.Shared.Opc;

public static class DrawingMlThemeReader
{
    public const string DrawingNamespaceUri = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string ThemeRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

    public static DrawingMlTheme Read(XDocument themeXml)
    {
        var root = themeXml.Root;
        if (root is null)
            return DrawingMlTheme.Empty;

        XNamespace drawing = DrawingNamespaceUri;
        var elements = root.Element(drawing + "themeElements");
        var colorScheme = ReadColorScheme(elements?.Element(drawing + "clrScheme"));
        var fontScheme = elements?.Element(drawing + "fontScheme");
        var formatScheme = elements?.Element(drawing + "fmtScheme");

        return new DrawingMlTheme(
            root.Attribute("name")?.Value,
            colorScheme,
            new DrawingMlThemeFontScheme(
                ReadTypeface(fontScheme?.Element(drawing + "majorFont"), drawing),
                ReadTypeface(fontScheme?.Element(drawing + "minorFont"), drawing)),
            formatScheme?.Attribute("name")?.Value,
            elements?.Element(drawing + "clrScheme")?.ToString(SaveOptions.DisableFormatting),
            fontScheme?.ToString(SaveOptions.DisableFormatting),
            formatScheme?.ToString(SaveOptions.DisableFormatting));

    }

    public static DrawingMlThemeColorScheme ReadColorScheme(XElement? colorScheme)
    {
        var result = new DrawingMlThemeColorScheme { Name = colorScheme?.Attribute("name")?.Value };
        if (colorScheme is null)
            return result;

        XNamespace drawing = DrawingNamespaceUri;
        foreach (var (slot, elementName) in DrawingMlThemeColorSlotMapper.ColorSchemeElements)
        {
            if (ReadColor(colorScheme.Element(drawing + elementName)) is { } color)
                result[slot] = color;
        }

        return result;
    }

    public static DrawingMlThemeColor? ReadColor(XElement? colorContainer)
    {
        if (colorContainer is null)
            return null;

        XNamespace drawing = DrawingNamespaceUri;
        var srgb = colorContainer.Element(drawing + "srgbClr");
        if (srgb is not null && TryParseRgb(srgb.Attribute("val")?.Value, out var rgb))
            return Transform(srgb, rgb, DrawingMlThemeColorKind.Srgb, srgb.Attribute("val")?.Value);

        var sys = colorContainer.Element(drawing + "sysClr");
        if (sys is not null && TryParseRgb(sys.Attribute("lastClr")?.Value, out rgb))
            return Transform(sys, rgb, DrawingMlThemeColorKind.System, sys.Attribute("val")?.Value, sys.Attribute("lastClr")?.Value);

        var hsl = colorContainer.Element(drawing + "hslClr");
        if (hsl is not null && TryReadHsl(hsl, out rgb))
            return Transform(hsl, rgb, DrawingMlThemeColorKind.Hsl);

        var scrgb = colorContainer.Element(drawing + "scrgbClr");
        if (scrgb is not null && TryReadScRgb(scrgb, out rgb))
            return Transform(scrgb, rgb, DrawingMlThemeColorKind.ScRgb);

        var preset = colorContainer.Element(drawing + "prstClr");
        if (preset is not null && PresetColors.TryGetValue(preset.Attribute("val")?.Value?.Trim() ?? string.Empty, out var hex) && TryParseRgb(hex, out rgb))
            return Transform(preset, rgb, DrawingMlThemeColorKind.Preset, preset.Attribute("val")?.Value);

        return null;
    }

    public static string? ResolveThemePartPath(ZipArchive archive, string ownerPartPath, string? fallbackPartPath = null)
    {
        var ownerPath = OpcPathHelper.ToZipEntryPath(ownerPartPath);
        var ownerDirectory = OpcPathHelper.GetDirectoryName(ownerPath);
        var relsPath = OpcPathHelper.GetRelationshipPartPath(ownerPath);
        var relationship = OpcRelationships.Load(archive, relsPath)
            .FirstOrDefault(candidate =>
                !candidate.IsExternal &&
                !string.IsNullOrWhiteSpace(candidate.Target) &&
                (string.Equals(candidate.Type, ThemeRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                 candidate.Type.EndsWith("/theme", StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(relationship.Target))
        {
            var target = OpcPathHelper.UnescapeRelationshipPathSegments(relationship.Target);
            var resolved = OpcPathHelper.ResolveRelativeZipPath(ownerDirectory, target);
            if (archive.GetEntry(resolved) is not null)
                return resolved;
        }

        var fallback = string.IsNullOrWhiteSpace(fallbackPartPath)
            ? null
            : OpcPathHelper.ToZipEntryPath(fallbackPartPath);
        return fallback is not null && archive.GetEntry(fallback) is not null ? fallback : null;
    }

    public static DrawingMlTheme? TryReadThemePart(ZipArchive archive, string ownerPartPath, string? fallbackPartPath = null)
    {
        var path = ResolveThemePartPath(archive, ownerPartPath, fallbackPartPath);
        return path is null || OpcXml.TryLoadXml(archive, path) is not { } xml || xml.Root is null
            ? null
            : Read(xml);
    }

    private static DrawingMlThemeColor Transform(
        XElement colorElement,
        DrawingMlRgbColor color,
        DrawingMlThemeColorKind kind,
        string? value = null,
        string? fallbackValue = null)
    {
        var resolved = DrawingMlColorTransform.Apply(
            color,
            ReadFraction(colorElement, "lumMod", 1.0, 2.0),
            ReadFraction(colorElement, "lumOff", 0.0, 2.0),
            ReadFraction(colorElement, "tint", 1.0, 1.0),
            ReadFraction(colorElement, "shade", 1.0, 1.0));
        return new DrawingMlThemeColor(resolved, kind, value, fallbackValue, color);
    }

    private static string? ReadTypeface(XElement? fontElement, XNamespace drawing) =>
        fontElement?.Element(drawing + "latin")?.Attribute("typeface")?.Value;

    private static bool TryParseRgb(string? value, out DrawingMlRgbColor color) =>
        DrawingMlRgbColor.TryParseHexRgb(value, out color);

    private static double ReadFraction(XElement element, string attribute, double fallback, double max) =>
        long.TryParse(element.Element(XName.Get(attribute, DrawingNamespaceUri))?.Attribute("val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw)
            ? Math.Clamp(raw / 100000.0, 0.0, max)
            : fallback;

    private static bool TryReadHsl(XElement element, out DrawingMlRgbColor color)
    {
        color = default;
        if (!TryReadInt(element, "hue", out var hue) || !TryReadInt(element, "sat", out var sat) || !TryReadInt(element, "lum", out var lum))
            return false;

        var h = (hue / 60000.0 % 360.0 + 360.0) % 360.0 / 360.0;
        var s = Math.Clamp(sat / 100000.0, 0.0, 1.0);
        var l = Math.Clamp(lum / 100000.0, 0.0, 1.0);
        if (s <= 0)
        {
            var gray = ToByte(l);
            color = new(gray, gray, gray);
            return true;
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        color = new(HueToByte(p, q, h + 1.0 / 3), HueToByte(p, q, h), HueToByte(p, q, h - 1.0 / 3));
        return true;
    }

    private static byte HueToByte(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        var value = t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        return ToByte(value);
    }

    private static bool TryReadScRgb(XElement element, out DrawingMlRgbColor color)
    {
        color = default;
        if (!TryReadInt(element, "r", out var r) || !TryReadInt(element, "g", out var g) || !TryReadInt(element, "b", out var b))
            return false;
        color = new(LinearToSrgb(r / 100000.0), LinearToSrgb(g / 100000.0), LinearToSrgb(b / 100000.0));
        return true;
    }

    private static byte LinearToSrgb(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var srgb = value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        return ToByte(srgb);
    }

    private static bool TryReadInt(XElement element, string attribute, out int value) =>
        int.TryParse(element.Attribute(attribute)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static byte ToByte(double unitValue) =>
        (byte)Math.Clamp(Math.Round(unitValue * 255.0, MidpointRounding.AwayFromZero), 0.0, 255.0);

    private static IReadOnlyDictionary<string, string> PresetColors => DrawingMlPresetColorMap.Values;
}
