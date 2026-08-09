using FreeP.Core.Model;
using System.Xml;
using System.Xml.Linq;

namespace FreeP.App.Compositor;

/// <summary>Shared command metadata and defaults for the PowerPoint Zoom format dialog.</summary>
public static class ZoomObjectPropertiesPlanner
{
    public const string CommandId = "freep.zoom.format";
    public const string DialogTitle = "Zoom Format";
    public const int DefaultTransitionDurationMs = 1000;
    public const string InvalidTransitionDurationMessage =
        "Transition duration must be a positive whole number of milliseconds.";
    public const string InvalidFrameBorderColorMessage =
        "Border color must be a six-digit RGB value.";
    public const string InvalidFrameBorderWidthMessage =
        "Border width must be a positive value in points.";
    public const string InvalidFrameBorderDashMessage =
        "Border dash must be a supported PowerPoint line pattern.";
    public const string InvalidFrameBorderGradientMessage =
        "Gradient border requires two six-digit RGB colors and an angle from 0 to 360 degrees.";
    public const string InvalidFrameBorderPatternMessage =
        "Pattern border requires a supported preset and two six-digit RGB colors.";
    public const string InvalidFrameBorderShadowMessage =
        "Border shadow requires a six-digit RGB color, alpha from 0 to 100 percent, and non-negative blur, distance, and direction from 0 to 360 degrees.";
    public const string InvalidFrameBorderGlowMessage =
        "Border glow requires a six-digit RGB color, alpha from 0 to 100 percent, and a non-negative radius in points.";
    public const string InvalidFrameBorderSoftEdgeMessage =
        "Border soft edge requires a non-negative radius in points.";
    public const string InvalidFrameBorderReflectionMessage =
        "Border reflection requires alpha and fade end from 0 to 100 percent, non-negative distance, direction from 0 to 360 degrees, and a non-zero scale from -100 to 100 percent.";
    public const string InvalidFrameGeometryMessage =
        "Frame shape must be Rectangle, Rounded rectangle, or Ellipse.";
    public const string InvalidCropEdgesMessage =
        "Crop edges must be four percentages: left, top, right, bottom.";
    public const string InvalidSummaryTileLayoutMessage =
        "Summary tile position and scale must each be two percentages.";

    public sealed record SummaryZoomTileLayoutEdit(
        string SectionId,
        int OffsetFactorX,
        int OffsetFactorY,
        int ScaleFactorX,
        int ScaleFactorY);

    public sealed record SummaryZoomTilePropertiesEdit(
        string SectionId,
        ZoomObjectProperties Properties);

    public static ZoomObjectProperties Effective(PreservedObjectInfo? info) =>
        info?.ZoomProperties ?? new ZoomObjectProperties(true, "preview", null, true);

    /// <summary>
    /// Reads one Summary Zoom tile's native properties, falling back to the object-level
    /// projection for older packages that do not carry a tile-local <c>zmPr</c>.
    /// </summary>
    public static ZoomObjectProperties EffectiveSummaryTile(
        PreservedObjectInfo? info,
        string sectionId)
    {
        var fallback = Effective(info);
        if (info is null || string.IsNullOrWhiteSpace(sectionId)
            || string.IsNullOrWhiteSpace(info.RawXml))
            return fallback;

        try
        {
            var root = XElement.Parse(info.RawXml, LoadOptions.PreserveWhitespace);
            var tile = root.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "summaryZmObj", StringComparison.OrdinalIgnoreCase)
                && string.Equals(element.Attribute("sectionId")?.Value, sectionId,
                    StringComparison.OrdinalIgnoreCase));
            var properties = tile?.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "zmPr", StringComparison.OrdinalIgnoreCase));
            return properties is null ? fallback : Read(properties, fallback);
        }
        catch (XmlException)
        {
            return fallback;
        }
    }

    private static ZoomObjectProperties Read(XElement properties, ZoomObjectProperties fallback)
    {
        var value = new ZoomObjectProperties(
            ReadNullableBoolean(properties.Attribute("returnToParent")?.Value),
            properties.Attribute("imageType")?.Value,
            properties.Attribute("transitionDur")?.Value,
            ReadNullableBoolean(properties.Attribute("showBg")?.Value),
            ReadNullableInt(properties.Descendants().FirstOrDefault(element => element.Name.LocalName == "srcRect")?.Attribute("l")?.Value),
            ReadNullableInt(properties.Descendants().FirstOrDefault(element => element.Name.LocalName == "srcRect")?.Attribute("t")?.Value),
            ReadNullableInt(properties.Descendants().FirstOrDefault(element => element.Name.LocalName == "srcRect")?.Attribute("r")?.Value),
            ReadNullableInt(properties.Descendants().FirstOrDefault(element => element.Name.LocalName == "srcRect")?.Attribute("b")?.Value),
            ReadFrameBorderColor(properties),
            ReadFrameBorderWidth(properties),
            ReadFrameBorderDash(properties),
            ReadFrameGeometry(properties),
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
            ReadFrameBorderReflection(properties),
            ReadFrameBorderReflectionEnabled(properties));
        return value.IsEmpty ? fallback : value;
    }

    private static ZoomFrameBorderShadow? ReadFrameBorderShadow(XElement properties)
    {
        var shadow = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "outerShdw", StringComparison.OrdinalIgnoreCase));
        var color = shadow?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value?.Trim().TrimStart('#');
        if (color is not { Length: 6 } || !color.All(Uri.IsHexDigit))
            return null;

        var alpha = ReadNullableInt(shadow?.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "alpha", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value) ?? 50000;
        var blur = ReadNullableLong(shadow?.Attribute("blurRad")?.Value) ?? 0;
        var distance = ReadNullableLong(shadow?.Attribute("dist")?.Value) ?? 0;
        var direction = ReadNullableInt(shadow?.Attribute("dir")?.Value) ?? 0;
        if (alpha is < 0 or > 100000 || blur < 0 || distance < 0
            || direction is < 0 or > 21600000)
            return null;

        return new ZoomFrameBorderShadow(color.ToUpperInvariant(), alpha, blur, distance, direction);
    }

    private static bool? ReadFrameBorderShadowEnabled(XElement properties)
    {
        var effectList = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase));
        return effectList?.Elements().Any(element =>
            string.Equals(element.Name.LocalName, "outerShdw", StringComparison.OrdinalIgnoreCase)) == true
            ? true
            : null;
    }

    private static ZoomFrameBorderGlow? ReadFrameBorderGlow(XElement properties)
    {
        var glow = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "glow", StringComparison.OrdinalIgnoreCase));
        var color = glow?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value?.Trim().TrimStart('#');
        if (color is not { Length: 6 } || !color.All(Uri.IsHexDigit))
            return null;

        var alpha = ReadNullableInt(glow?.Descendants().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "alpha", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value) ?? 50000;
        var radius = ReadNullableLong(glow?.Attribute("rad")?.Value) ?? 0;
        if (alpha is < 0 or > 100000 || radius < 0)
            return null;

        return new ZoomFrameBorderGlow(color.ToUpperInvariant(), alpha, radius);
    }

    private static bool? ReadFrameBorderGlowEnabled(XElement properties)
    {
        var effectList = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase));
        return effectList?.Elements().Any(element =>
            string.Equals(element.Name.LocalName, "glow", StringComparison.OrdinalIgnoreCase)) == true
            ? true
            : null;
    }

    private static ZoomFrameBorderSoftEdge? ReadFrameBorderSoftEdge(XElement properties)
    {
        var softEdge = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "softEdge", StringComparison.OrdinalIgnoreCase));
        var radius = ReadNullableLong(softEdge?.Attribute("rad")?.Value);
        return radius is >= 0 ? new ZoomFrameBorderSoftEdge(radius.Value) : null;
    }

    private static bool? ReadFrameBorderSoftEdgeEnabled(XElement properties)
    {
        var effectList = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "effectLst", StringComparison.OrdinalIgnoreCase));
        return effectList?.Elements().Any(element =>
            string.Equals(element.Name.LocalName, "softEdge", StringComparison.OrdinalIgnoreCase)) == true
            ? true
            : null;
    }

    private static ZoomFrameBorderReflection? ReadFrameBorderReflection(XElement properties)
    {
        var reflection = properties.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "reflection", StringComparison.OrdinalIgnoreCase));
        if (reflection is null)
            return null;

        var alpha = ReadNullableInt(reflection.Attribute("stA")?.Value) ?? 50000;
        var blur = ReadNullableLong(reflection.Attribute("blurRad")?.Value) ?? 0;
        var distance = ReadNullableLong(reflection.Attribute("dist")?.Value) ?? 0;
        var direction = ReadNullableInt(reflection.Attribute("dir")?.Value) ?? 5400000;
        var scaleY = ReadNullableInt(reflection.Attribute("sy")?.Value) ?? -100000;
        var endPosition = ReadNullableInt(reflection.Attribute("endPos")?.Value) ?? 100000;
        return alpha is >= 0 and <= 100000
            && blur >= 0 && distance >= 0
            && direction is >= 0 and <= 21600000
            && scaleY is >= -100000 and <= 100000
            && endPosition is >= 0 and <= 100000
            ? new ZoomFrameBorderReflection(alpha, blur, distance, direction, scaleY, endPosition)
            : null;
    }

    private static bool? ReadFrameBorderReflectionEnabled(XElement properties) =>
        properties.Descendants().Any(element =>
            string.Equals(element.Name.LocalName, "reflection", StringComparison.OrdinalIgnoreCase))
            ? true
            : null;

    private static string? ReadFrameBorderColor(XElement properties)
    {
        var shapeProperties = properties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        var line = shapeProperties?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        var solidFill = line?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "solidFill", StringComparison.OrdinalIgnoreCase));
        var color = solidFill?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        return TryNormalizeFrameBorderColor(color, out var normalized) ? normalized : null;
    }

    private static int? ReadFrameBorderWidth(XElement properties)
    {
        var shapeProperties = properties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        var line = shapeProperties?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        return int.TryParse(line?.Attribute("w")?.Value, out var width) && width > 0
            ? width
            : null;
    }

    private static OutlineDash? ReadFrameBorderDash(XElement properties)
    {
        var line = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        var token = line?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "prstDash", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        return TryParseFrameBorderDash(token, out var dash) ? dash : null;
    }

    private static ZoomFrameBorderGradient? ReadFrameBorderGradient(XElement properties)
    {
        var line = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        var gradient = line?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "gradFill", StringComparison.OrdinalIgnoreCase));
        var stops = gradient?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "gsLst", StringComparison.OrdinalIgnoreCase))
            ?.Elements().Where(element =>
                string.Equals(element.Name.LocalName, "gs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (stops is not { Length: >= 2 })
            return null;

        var start = ReadRgbStop(stops[0]);
        var end = ReadRgbStop(stops[^1]);
        var angleText = gradient?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "lin", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ang")?.Value;
        var angle = string.IsNullOrWhiteSpace(angleText)
            ? 0
            : int.TryParse(angleText, out var parsedAngle) ? parsedAngle : -1;
        return start is not null && end is not null
            && angle is >= 0 and <= 21_600_000
            ? new ZoomFrameBorderGradient(start, end, angle)
            : null;
    }

    private static ZoomFrameBorderPattern? ReadFrameBorderPattern(XElement properties)
    {
        var line = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        var pattern = line?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "pattFill", StringComparison.OrdinalIgnoreCase));
        var preset = pattern?.Attribute("prst")?.Value;
        var foreground = ReadPatternRgb(pattern?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "fgClr", StringComparison.OrdinalIgnoreCase)));
        var background = ReadPatternRgb(pattern?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "bgClr", StringComparison.OrdinalIgnoreCase)));
        return TryNormalizeFrameBorderPatternPreset(preset, out var normalizedPreset)
            && foreground is not null
            && background is not null
            ? new ZoomFrameBorderPattern(normalizedPreset!, foreground, background)
            : null;
    }

    private static bool? ReadFrameBorderNoFill(XElement properties)
    {
        var line = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        return line?.Elements().Any(element =>
            string.Equals(element.Name.LocalName, "noFill", StringComparison.OrdinalIgnoreCase)) == true
            ? true
            : null;
    }

    private static ThemeColorSlot? ReadFrameBorderThemeColor(XElement properties)
    {
        var line = properties.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "ln", StringComparison.OrdinalIgnoreCase));
        var value = line?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "solidFill", StringComparison.OrdinalIgnoreCase))
            ?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "schemeClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;
        return ThemeColorSlotMapper.TryMapRole(value, out var slot) ? slot : null;
    }

    private static string? ReadPatternRgb(XElement? color)
    {
        var value = color?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value?.Trim().TrimStart('#');
        return value is { Length: 6 } && value.All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : null;
    }

    private static string? ReadRgbStop(XElement stop)
    {
        var value = stop.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "srgbClr", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value?.Trim().TrimStart('#');
        return value is { Length: 6 } && value.All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : null;
    }

    private static string? ReadFrameGeometry(XElement properties)
    {
        var shapeProperties = properties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        var token = shapeProperties?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "prstGeom", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("prst")?.Value;
        return TryNormalizeFrameGeometry(token, out var normalized)
            && !string.Equals(normalized, "rect", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : null;
    }

    public static IReadOnlyList<string> FrameGeometryOptions { get; } =
        new[] { "rect", "roundRect", "ellipse" };

    public static bool TryParseFrameGeometry(string? text, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            normalized = null;
            return true;
        }

        return TryNormalizeFrameGeometry(text, out normalized);
    }

    private static bool TryNormalizeFrameGeometry(string? text, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        var match = FrameGeometryOptions.FirstOrDefault(option =>
            string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;

        normalized = match;
        return true;
    }

    private static bool? ReadNullableBoolean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "on" => true,
                "0" or "false" or "off" => false,
                _ => null,
            };

    private static int? ReadNullableInt(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static long? ReadNullableLong(string? value) =>
        long.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    public static bool IsSupportedImageType(string? imageType) =>
        string.Equals(imageType, "preview", StringComparison.OrdinalIgnoreCase)
        || string.Equals(imageType, "cover", StringComparison.OrdinalIgnoreCase);

    public static bool IsTransitionEnabled(ZoomObjectProperties properties) =>
        !string.IsNullOrWhiteSpace(properties.TransitionDuration);

    public static bool IsFrameBorderEnabled(ZoomObjectProperties properties) =>
        TryNormalizeFrameBorderColor(properties.FrameBorderColor, out _)
        || properties.FrameBorderGradient is not null
        || properties.FrameBorderPattern is not null
        || properties.FrameBorderNoFill == true
        || properties.FrameBorderThemeColor is not null
        || IsFrameBorderShadowEnabled(properties)
        || IsFrameBorderGlowEnabled(properties)
        || IsFrameBorderSoftEdgeEnabled(properties)
        || IsFrameBorderReflectionEnabled(properties);

    public static string FormatFrameBorderWidth(ZoomObjectProperties properties) =>
        properties.FrameBorderWidthEmu is int width
            ? (width / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    public static string FormatFrameBorderDash(ZoomObjectProperties properties) =>
        (properties.FrameBorderDash ?? OutlineDash.Solid).ToString();

    public static string FormatFrameBorderGradientStart(ZoomObjectProperties properties) =>
        properties.FrameBorderGradient?.StartColor ?? string.Empty;

    public static string FormatFrameBorderGradientEnd(ZoomObjectProperties properties) =>
        properties.FrameBorderGradient?.EndColor ?? string.Empty;

    public static string FormatFrameBorderGradientAngle(ZoomObjectProperties properties) =>
        properties.FrameBorderGradient is { } gradient
            ? (gradient.Angle / 60000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "0";

    public static bool IsFrameBorderGradientEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderGradient is not null;

    public static IReadOnlyList<string> FrameBorderPatternOptions =>
        ZoomFrameBorderPatternCatalog.Presets;

    public static bool IsFrameBorderPatternEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderPattern is not null;

    public static bool IsFrameBorderNoFillEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderNoFill == true;

    public static IReadOnlyList<ThemeColorSlot> FrameBorderThemeColorOptions { get; } =
        Enum.GetValues<ThemeColorSlot>();

    public static bool IsFrameBorderThemeColorEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderThemeColor is not null;

    public static bool IsFrameBorderShadowEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderShadowEnabled == true || properties.FrameBorderShadow is not null;

    public static bool IsFrameBorderGlowEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderGlowEnabled == true || properties.FrameBorderGlow is not null;

    public static bool IsFrameBorderSoftEdgeEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderSoftEdgeEnabled == true || properties.FrameBorderSoftEdge is not null;

    public static bool IsFrameBorderReflectionEnabled(ZoomObjectProperties properties) =>
        properties.FrameBorderReflectionEnabled == true || properties.FrameBorderReflection is not null;

    public static string FormatFrameBorderReflectionAlpha(ZoomObjectProperties properties) =>
        properties.FrameBorderReflection is { } reflection
            ? (reflection.Alpha / 1000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "50";

    public static string FormatFrameBorderReflectionBlur(ZoomObjectProperties properties) =>
        properties.FrameBorderReflection is { } reflection
            ? (reflection.BlurRadiusEmu / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "0";

    public static string FormatFrameBorderReflectionDistance(ZoomObjectProperties properties) =>
        properties.FrameBorderReflection is { } reflection
            ? (reflection.DistanceEmu / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "0";

    public static string FormatFrameBorderReflectionDirection(ZoomObjectProperties properties) =>
        properties.FrameBorderReflection is { } reflection
            ? (reflection.Direction / 60000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "90";

    public static string FormatFrameBorderReflectionScale(ZoomObjectProperties properties) =>
        properties.FrameBorderReflection is { } reflection
            ? (reflection.ScaleY / 1000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "-100";

    public static string FormatFrameBorderReflectionEndPosition(ZoomObjectProperties properties) =>
        properties.FrameBorderReflection is { } reflection
            ? (reflection.EndPosition / 1000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "100";

    public static bool TryParseFrameBorderReflection(
        string? alphaText,
        string? distanceText,
        string? directionText,
        string? scaleText,
        string? blurText,
        string? endPositionText,
        bool enabled,
        out ZoomFrameBorderReflection? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (!double.TryParse(alphaText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var alphaPercent)
            || !double.TryParse(distanceText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var distancePoints)
            || !double.TryParse(directionText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var directionDegrees)
            || !double.TryParse(scaleText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var scalePercent)
            || !double.TryParse(blurText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var blurPoints)
            || !double.TryParse(endPositionText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var endPositionPercent)
            || !double.IsFinite(alphaPercent) || !double.IsFinite(distancePoints)
            || !double.IsFinite(directionDegrees) || !double.IsFinite(scalePercent)
            || !double.IsFinite(blurPoints) || !double.IsFinite(endPositionPercent)
            || alphaPercent is < 0 or > 100 || distancePoints < 0
            || directionDegrees is < 0 or > 360 || blurPoints < 0
            || scalePercent is < -100 or > 100 || Math.Abs(scalePercent) < 0.01
            || endPositionPercent is < 0 or > 100)
            return false;

        normalized = new ZoomFrameBorderReflection(
            checked((int)Math.Round(alphaPercent * 1000d, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(blurPoints * 12700d, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(distancePoints * 12700d, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(directionDegrees * 60000d, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(scalePercent * 1000d, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(endPositionPercent * 1000d, MidpointRounding.AwayFromZero)));
        return true;
    }

    public static string FormatFrameBorderShadowColor(ZoomObjectProperties properties) =>
        properties.FrameBorderShadow?.Color ?? string.Empty;

    public static string FormatFrameBorderShadowAlpha(ZoomObjectProperties properties) =>
        properties.FrameBorderShadow is { } shadow
            ? (shadow.Alpha / 1000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "50";

    public static string FormatFrameBorderShadowBlur(ZoomObjectProperties properties) =>
        properties.FrameBorderShadow is { } shadow
            ? (shadow.BlurRadiusEmu / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "4";

    public static string FormatFrameBorderShadowDistance(ZoomObjectProperties properties) =>
        properties.FrameBorderShadow is { } shadow
            ? (shadow.DistanceEmu / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "3";

    public static string FormatFrameBorderShadowDirection(ZoomObjectProperties properties) =>
        properties.FrameBorderShadow is { } shadow
            ? (shadow.Direction / 60000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "45";

    public static bool TryParseFrameBorderShadow(
        string? colorText,
        string? alphaText,
        string? blurText,
        string? distanceText,
        string? directionText,
        bool enabled,
        out ZoomFrameBorderShadow? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (!TryNormalizeFrameBorderColor(colorText, out var color)
            || !double.TryParse(alphaText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var alphaPercent)
            || !double.TryParse(blurText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var blurPoints)
            || !double.TryParse(distanceText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var distancePoints)
            || !double.TryParse(directionText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var directionDegrees)
            || !double.IsFinite(alphaPercent) || !double.IsFinite(blurPoints)
            || !double.IsFinite(distancePoints) || !double.IsFinite(directionDegrees)
            || alphaPercent is < 0 or > 100
            || blurPoints < 0 || distancePoints < 0
            || directionDegrees is < 0 or > 360)
            return false;

        normalized = new ZoomFrameBorderShadow(
            color!,
            checked((int)Math.Round(alphaPercent * 1000d, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(blurPoints * 12700d, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(distancePoints * 12700d, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(directionDegrees * 60000d, MidpointRounding.AwayFromZero)));
        return true;
    }

    public static string FormatFrameBorderGlowColor(ZoomObjectProperties properties) =>
        properties.FrameBorderGlow?.Color ?? string.Empty;

    public static string FormatFrameBorderGlowAlpha(ZoomObjectProperties properties) =>
        properties.FrameBorderGlow is { } glow
            ? (glow.Alpha / 1000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "50";

    public static string FormatFrameBorderGlowRadius(ZoomObjectProperties properties) =>
        properties.FrameBorderGlow is { } glow
            ? (glow.RadiusEmu / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "16";

    public static string FormatFrameBorderSoftEdgeRadius(ZoomObjectProperties properties) =>
        properties.FrameBorderSoftEdge is { } softEdge
            ? (softEdge.RadiusEmu / 12700d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "10";

    public static bool TryParseFrameBorderGlow(
        string? colorText,
        string? alphaText,
        string? radiusText,
        bool enabled,
        out ZoomFrameBorderGlow? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (!TryNormalizeFrameBorderColor(colorText, out var color)
            || !double.TryParse(alphaText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var alphaPercent)
            || !double.TryParse(radiusText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var radiusPoints)
            || !double.IsFinite(alphaPercent) || !double.IsFinite(radiusPoints)
            || alphaPercent is < 0 or > 100 || radiusPoints < 0)
            return false;

        normalized = new ZoomFrameBorderGlow(
            color!,
            checked((int)Math.Round(alphaPercent * 1000d, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(radiusPoints * 12700d, MidpointRounding.AwayFromZero)));
        return true;
    }

    public static bool TryParseFrameBorderSoftEdge(
        string? radiusText,
        bool enabled,
        out ZoomFrameBorderSoftEdge? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (!double.TryParse(radiusText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var radiusPoints)
            || !double.IsFinite(radiusPoints)
            || radiusPoints < 0)
            return false;

        normalized = new ZoomFrameBorderSoftEdge(
            checked((long)Math.Round(radiusPoints * 12700d, MidpointRounding.AwayFromZero)));
        return true;
    }

    public static string FormatFrameBorderPatternPreset(ZoomObjectProperties properties) =>
        properties.FrameBorderPattern?.Preset ?? FrameBorderPatternOptions[0];

    public static string FormatFrameBorderPatternForeground(ZoomObjectProperties properties) =>
        properties.FrameBorderPattern?.ForegroundColor ?? string.Empty;

    public static string FormatFrameBorderPatternBackground(ZoomObjectProperties properties) =>
        properties.FrameBorderPattern?.BackgroundColor ?? string.Empty;

    public static bool TryParseFrameBorderPattern(
        string? presetText,
        string? foregroundText,
        string? backgroundText,
        bool enabled,
        out ZoomFrameBorderPattern? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (!TryNormalizeFrameBorderPatternPreset(presetText, out var preset)
            || !TryNormalizeFrameBorderColor(foregroundText, out var foreground)
            || !TryNormalizeFrameBorderColor(backgroundText, out var background))
            return false;

        normalized = new ZoomFrameBorderPattern(preset!, foreground!, background!);
        return true;
    }

    private static bool TryNormalizeFrameBorderPatternPreset(
        string? text,
        out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        normalized = ZoomFrameBorderPatternCatalog.Normalize(text);
        return normalized is not null;
    }

    public static bool TryParseFrameBorderGradient(
        string? startText,
        string? endText,
        string? angleText,
        bool enabled,
        out ZoomFrameBorderGradient? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (!TryNormalizeFrameBorderColor(startText, out var start)
            || !TryNormalizeFrameBorderColor(endText, out var end)
            || !double.TryParse(angleText?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var degrees)
            || !double.IsFinite(degrees)
            || degrees < 0
            || degrees > 360)
            return false;

        normalized = new ZoomFrameBorderGradient(
            start!, end!, checked((int)Math.Round(degrees * 60000d, MidpointRounding.AwayFromZero)));
        return true;
    }

    public static IReadOnlyList<OutlineDash> FrameBorderDashOptions { get; } =
        Enum.GetValues<OutlineDash>();

    public static bool TryParseFrameBorderDash(string? text, out OutlineDash? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (!Enum.TryParse<OutlineDash>(text.Trim(), ignoreCase: true, out var dash)
            || !Enum.IsDefined(dash))
            return false;

        normalized = dash;
        return true;
    }

    public static bool TryParseFrameBorderWidth(
        string? text,
        bool enabled,
        out int? normalized)
    {
        normalized = null;
        if (!enabled || string.IsNullOrWhiteSpace(text))
            return true;

        if (!double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var points)
            || !double.IsFinite(points)
            || points <= 0
            || points > 1584)
            return false;

        normalized = checked((int)Math.Round(points * 12700, MidpointRounding.AwayFromZero));
        return true;
    }

    /// <summary>Normalizes the supported solid Zoom border color; an unchecked border is an explicit clear.</summary>
    public static bool TryParseFrameBorderColor(
        string? text,
        bool enabled,
        out string? normalized)
    {
        normalized = null;
        if (!enabled)
        {
            normalized = string.Empty;
            return true;
        }

        return TryNormalizeFrameBorderColor(text, out normalized);
    }

    private static bool TryNormalizeFrameBorderColor(string? text, out string? normalized)
    {
        normalized = null;
        var value = text?.Trim().TrimStart('#');
        if (value is null or { Length: 0 })
            return false;
        if (value.Length != 6 || !value.All(Uri.IsHexDigit))
            return false;

        normalized = value.ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Normalizes the Zoom transition control used by both desktop dialogs. An unchecked
    /// transition removes transitionDur; enabling it with an empty field uses PowerPoint's
    /// one-second authoring default. Stored values remain invariant-culture integer milliseconds.
    /// </summary>
    public static bool TryParseTransitionDuration(
        string? text,
        bool enabled,
        out string? normalized)
    {
        normalized = null;
        if (!enabled)
            return true;

        if (string.IsNullOrWhiteSpace(text))
        {
            normalized = DefaultTransitionDurationMs.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        if (!int.TryParse(
                text.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var durationMs)
            || durationMs <= 0)
            return false;

        normalized = durationMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    public static string FormatCropEdges(ZoomObjectProperties properties)
    {
        if (properties.CropLeft is null && properties.CropTop is null
            && properties.CropRight is null && properties.CropBottom is null)
            return string.Empty;

        return string.Join(", ", new[]
        {
            properties.CropLeft ?? 0,
            properties.CropTop ?? 0,
            properties.CropRight ?? 0,
            properties.CropBottom ?? 0,
        }.Select(value => (value / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static bool TryParseCropEdges(string? text, out int? left, out int? top, out int? right, out int? bottom)
    {
        left = top = right = bottom = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            return false;

        var values = new int[4];
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(parts[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent)
                || !double.IsFinite(percent)
                || percent is < 0 or > 100)
                return false;
            values[index] = checked((int)Math.Round(percent * 1000, MidpointRounding.AwayFromZero));
        }

        left = values[0];
        top = values[1];
        right = values[2];
        bottom = values[3];
        return true;
    }

    public static string FormatFactorPair(int first, int second) =>
        string.Join(", ", new[] { first, second }
            .Select(value => (value / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));

    public static bool TryParseFactorPair(
        string? text,
        bool allowNegative,
        out int first,
        out int second)
    {
        first = second = 0;
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 })
            return false;

        var values = new int[2];
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(parts[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var percent)
                || !double.IsFinite(percent)
                || (!allowNegative && percent < 0)
                || percent < -100
                || percent > (allowNegative ? 100 : 400))
                return false;
            values[index] = checked((int)Math.Round(percent * 1000, MidpointRounding.AwayFromZero));
        }

        first = values[0];
        second = values[1];
        return true;
    }
}
