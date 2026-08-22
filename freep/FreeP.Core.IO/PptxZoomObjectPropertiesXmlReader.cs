using System.Globalization;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>Reads the portable projection of a PowerPoint <c>zmPr</c> element.</summary>
public static class PptxZoomObjectPropertiesXmlReader
{
    public static ZoomObjectProperties? Read(XElement properties) =>
        Read(properties, dialogProjection: false);

    public static ZoomObjectProperties? ReadDialogProjection(XElement properties) =>
        Read(properties, dialogProjection: true);

    private static ZoomObjectProperties? Read(
        XElement properties,
        bool dialogProjection)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var value = new ZoomObjectProperties(
            ParseNullableBoolean(properties.Attribute("returnToParent")?.Value, dialogProjection),
            properties.Attribute("imageType")?.Value,
            properties.Attribute("transitionDur")?.Value,
            ParseNullableBoolean(properties.Attribute("showBg")?.Value, dialogProjection),
            ParseNullableInt(DescendantAttribute(properties, "srcRect", "l")),
            ParseNullableInt(DescendantAttribute(properties, "srcRect", "t")),
            ParseNullableInt(DescendantAttribute(properties, "srcRect", "r")),
            ParseNullableInt(DescendantAttribute(properties, "srcRect", "b")),
            ReadFrameBorderColor(properties),
            ReadFrameBorderWidth(properties),
            ReadFrameBorderDash(properties),
            ReadFrameGeometry(properties, dialogProjection),
            ReadFrameBorderGradient(properties),
            ReadFrameBorderPattern(properties),
            ReadFrameBorderNoFill(properties),
            ReadFrameBorderThemeColor(properties),
            ReadFrameBorderShadow(properties),
            ReadFrameBorderShadowEnabled(properties),
            ReadFrameBorderGlow(properties),
            ReadFrameBorderGlowEnabled(properties),
            ReadFrameBorderSoftEdge(properties),
            ReadFrameBorderSoftEdgeEnabled(properties),
            ReadFrameBorderReflection(properties, dialogProjection),
            ReadFrameBorderReflectionEnabled(properties, dialogProjection));
        return value.IsEmpty ? null : value;
    }

    public static OutlineDash? ParseDashToken(string? token) =>
        string.IsNullOrWhiteSpace(token)
            ? null
            : token.Trim().ToLowerInvariant() switch
            {
                "solid" => OutlineDash.Solid,
                "dash" => OutlineDash.Dash,
                "dot" => OutlineDash.Dot,
                "dashdot" => OutlineDash.DashDot,
                "lgdash" => OutlineDash.LongDash,
                "lgdashdot" => OutlineDash.LongDashDot,
                "lgdashdotdot" => OutlineDash.LongDashDotDot,
                "sysdash" => OutlineDash.SystemDash,
                "sysdot" => OutlineDash.SystemDot,
                "sysdashdot" => OutlineDash.SystemDashDot,
                _ => null,
            };

    private static ZoomFrameBorderShadow? ReadFrameBorderShadow(XElement properties)
    {
        var shadow = EffectList(properties)?.Elements().FirstOrDefault(element =>
            IsNamed(element, "outerShdw"));
        var color = ReadRgbColor(shadow?.Elements().FirstOrDefault(element =>
            IsNamed(element, "srgbClr")));
        if (color is null)
            return null;

        var alpha = ParseNullableInt(DescendantAttribute(shadow, "alpha", "val")) ?? 50000;
        var blur = ParseNullableLong(shadow?.Attribute("blurRad")?.Value) ?? 0;
        var distance = ParseNullableLong(shadow?.Attribute("dist")?.Value) ?? 0;
        var direction = ParseNullableInt(shadow?.Attribute("dir")?.Value) ?? 0;
        return alpha is >= 0 and <= 100000
            && blur >= 0 && distance >= 0
            && direction is >= 0 and <= 21600000
                ? new ZoomFrameBorderShadow(color, alpha, blur, distance, direction)
                : null;
    }

    private static bool? ReadFrameBorderShadowEnabled(XElement properties) =>
        HasEffect(properties, "outerShdw");

    private static ZoomFrameBorderGlow? ReadFrameBorderGlow(XElement properties)
    {
        var glow = EffectList(properties)?.Elements().FirstOrDefault(element =>
            IsNamed(element, "glow"));
        var color = ReadRgbColor(glow?.Elements().FirstOrDefault(element =>
            IsNamed(element, "srgbClr")));
        if (color is null)
            return null;

        var alpha = ParseNullableInt(DescendantAttribute(glow, "alpha", "val")) ?? 50000;
        var radius = ParseNullableLong(glow?.Attribute("rad")?.Value) ?? 0;
        return alpha is >= 0 and <= 100000 && radius >= 0
            ? new ZoomFrameBorderGlow(color, alpha, radius)
            : null;
    }

    private static bool? ReadFrameBorderGlowEnabled(XElement properties) =>
        HasEffect(properties, "glow");

    private static ZoomFrameBorderSoftEdge? ReadFrameBorderSoftEdge(XElement properties)
    {
        var softEdge = EffectList(properties)?.Elements().FirstOrDefault(element =>
            IsNamed(element, "softEdge"));
        var radius = ParseNullableLong(softEdge?.Attribute("rad")?.Value);
        return radius is >= 0 ? new ZoomFrameBorderSoftEdge(radius.Value) : null;
    }

    private static bool? ReadFrameBorderSoftEdgeEnabled(XElement properties) =>
        HasEffect(properties, "softEdge");

    private static ZoomFrameBorderReflection? ReadFrameBorderReflection(
        XElement properties,
        bool dialogProjection)
    {
        var reflection = dialogProjection
            ? properties.Descendants().FirstOrDefault(element => IsNamed(element, "reflection"))
            : EffectList(properties)?.Elements().FirstOrDefault(element =>
                IsNamed(element, "reflection"));
        if (reflection is null)
            return null;

        var alpha = ParseNullableInt(reflection.Attribute("stA")?.Value) ?? 50000;
        var blur = ParseNullableLong(reflection.Attribute("blurRad")?.Value) ?? 0;
        var distance = ParseNullableLong(reflection.Attribute("dist")?.Value) ?? 0;
        var direction = ParseNullableInt(reflection.Attribute("dir")?.Value) ?? 5400000;
        var scaleY = ParseNullableInt(reflection.Attribute("sy")?.Value) ?? -100000;
        var endPosition = ParseNullableInt(reflection.Attribute("endPos")?.Value) ?? 100000;
        return alpha is >= 0 and <= 100000
            && blur >= 0 && distance >= 0
            && direction is >= 0 and <= 21600000
            && scaleY is >= -100000 and <= 100000
            && endPosition is >= 0 and <= 100000
                ? new ZoomFrameBorderReflection(alpha, blur, distance, direction, scaleY, endPosition)
                : null;
    }

    private static bool? ReadFrameBorderReflectionEnabled(
        XElement properties,
        bool dialogProjection) =>
        (dialogProjection
            ? properties.Descendants().Any(element => IsNamed(element, "reflection"))
            : EffectList(properties)?.Elements().Any(element => IsNamed(element, "reflection")) == true)
                ? true
                : null;

    private static string? ReadFrameBorderColor(XElement properties)
    {
        var solidFill = Line(properties)?.Elements().FirstOrDefault(element =>
            IsNamed(element, "solidFill"));
        return ReadRgbColor(solidFill?.Elements().FirstOrDefault(element =>
            IsNamed(element, "srgbClr")));
    }

    private static int? ReadFrameBorderWidth(XElement properties) =>
        int.TryParse(Line(properties)?.Attribute("w")?.Value, out var width) && width > 0
                ? width
                : null;

    private static OutlineDash? ReadFrameBorderDash(XElement properties)
    {
        var token = Line(properties)?.Elements().FirstOrDefault(element =>
                IsNamed(element, "prstDash"))
            ?.Attribute("val")?.Value;
        return ParseDashToken(token);
    }

    private static ZoomFrameBorderGradient? ReadFrameBorderGradient(XElement properties)
    {
        var gradient = Line(properties)?.Elements().FirstOrDefault(element =>
            IsNamed(element, "gradFill"));
        var stops = gradient?.Elements().FirstOrDefault(element => IsNamed(element, "gsLst"))
            ?.Elements().Where(element => IsNamed(element, "gs"))
            .ToArray();
        if (stops is not { Length: >= 2 })
            return null;

        var start = ReadRgbColor(stops[0].Elements().FirstOrDefault(element =>
            IsNamed(element, "srgbClr")));
        var end = ReadRgbColor(stops[^1].Elements().FirstOrDefault(element =>
            IsNamed(element, "srgbClr")));
        var angleText = gradient?.Elements().FirstOrDefault(element => IsNamed(element, "lin"))
            ?.Attribute("ang")?.Value;
        var angle = string.IsNullOrWhiteSpace(angleText)
            ? 0
            : int.TryParse(angleText, out var parsedAngle)
                ? parsedAngle
                : -1;
        return start is not null && end is not null && angle is >= 0 and <= 21600000
            ? new ZoomFrameBorderGradient(start, end, angle)
            : null;
    }

    private static ZoomFrameBorderPattern? ReadFrameBorderPattern(XElement properties)
    {
        var pattern = Line(properties)?.Elements().FirstOrDefault(element =>
            IsNamed(element, "pattFill"));
        var preset = ZoomFrameBorderPatternCatalog.Normalize(pattern?.Attribute("prst")?.Value);
        var foreground = ReadPatternColor(pattern, "fgClr");
        var background = ReadPatternColor(pattern, "bgClr");
        return preset is { Length: > 0 } && foreground is not null && background is not null
            ? new ZoomFrameBorderPattern(preset, foreground, background)
            : null;
    }

    private static bool? ReadFrameBorderNoFill(XElement properties) =>
        Line(properties)?.Elements().Any(element => IsNamed(element, "noFill")) == true
            ? true
            : null;

    private static ThemeColorSlot? ReadFrameBorderThemeColor(XElement properties)
    {
        var value = Line(properties)?.Elements().FirstOrDefault(element =>
                IsNamed(element, "solidFill"))
            ?.Elements().FirstOrDefault(element => IsNamed(element, "schemeClr"))
            ?.Attribute("val")?.Value;
        return ThemeColorSlotMapper.TryMapRole(value, out var slot) ? slot : null;
    }

    private static string? ReadFrameGeometry(
        XElement properties,
        bool dialogProjection)
    {
        var geometry = (dialogProjection
                ? ShapeProperties(properties)?.Elements().FirstOrDefault(element =>
                    IsNamed(element, "prstGeom"))
                : properties.Descendants().FirstOrDefault(element =>
                    IsNamed(element, "prstGeom")))
            ?.Attribute("prst")?.Value?.Trim();
        if (string.Equals(geometry, "rect", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!dialogProjection)
            return geometry;

        return geometry is not null
            && (string.Equals(geometry, "roundRect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(geometry, "ellipse", StringComparison.OrdinalIgnoreCase))
                    ? geometry.Equals("roundRect", StringComparison.OrdinalIgnoreCase)
                        ? "roundRect"
                        : "ellipse"
                    : null;
    }

    private static XElement? ShapeProperties(XElement properties) =>
        properties.Elements().FirstOrDefault(element => IsNamed(element, "spPr"));

    private static XElement? Line(XElement properties) =>
        ShapeProperties(properties)?.Elements().FirstOrDefault(element => IsNamed(element, "ln"));

    private static XElement? EffectList(XElement properties) =>
        ShapeProperties(properties)?.Elements().FirstOrDefault(element => IsNamed(element, "effectLst"));

    private static bool? HasEffect(XElement properties, string name) =>
        EffectList(properties)?.Elements().Any(element => IsNamed(element, name)) == true
            ? true
            : null;

    private static string? ReadPatternColor(XElement? pattern, string name) =>
        ReadRgbColor(pattern?.Elements().FirstOrDefault(element => IsNamed(element, name))
            ?.Elements().FirstOrDefault(element => IsNamed(element, "srgbClr")));

    private static string? ReadRgbColor(XElement? color)
    {
        var value = color?.Attribute("val")?.Value?.Trim().TrimStart('#');
        return value is { Length: 6 } && value.All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : null;
    }

    private static string? DescendantAttribute(
        XElement? parent,
        string elementName,
        string attributeName) =>
        parent?.Descendants().FirstOrDefault(element => IsNamed(element, elementName))
            ?.Attribute(attributeName)?.Value;

    private static bool IsNamed(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static bool? ParseNullableBoolean(string? value, bool dialogProjection)
    {
        if (!dialogProjection)
            return value is null
                ? null
                : value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "on" => true,
                "0" or "false" or "off" => false,
                _ => null,
            };
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ParseNullableLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
