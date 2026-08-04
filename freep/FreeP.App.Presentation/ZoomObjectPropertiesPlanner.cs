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
            ReadFrameBorderColor(properties));
        return value.IsEmpty ? fallback : value;
    }

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

    public static bool IsSupportedImageType(string? imageType) =>
        string.Equals(imageType, "preview", StringComparison.OrdinalIgnoreCase)
        || string.Equals(imageType, "cover", StringComparison.OrdinalIgnoreCase);

    public static bool IsTransitionEnabled(ZoomObjectProperties properties) =>
        !string.IsNullOrWhiteSpace(properties.TransitionDuration);

    public static bool IsFrameBorderEnabled(ZoomObjectProperties properties) =>
        TryNormalizeFrameBorderColor(properties.FrameBorderColor, out _);

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
