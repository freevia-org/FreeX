using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts a preserved PowerPoint InkML content part into renderer-neutral stroke plans.
/// The package remains authoritative: unsupported InkML channels are ignored, while the
/// original XML and referenced parts continue to round-trip through <see cref="PreservedObjectInfo"/>.
/// </summary>
public sealed record SlideShowInkRenderStrokePlan(
    IReadOnlyList<LayoutPoint> Points,
    SrgbColor Color,
    double ThicknessDip,
    byte Alpha);

public static class SlideShowInkRenderPlanner
{
    private static readonly XNamespace InkMl = "http://www.w3.org/2003/InkML";
    private static readonly XNamespace FreePInk = "https://freex.local/freep/ink/2026";
    private static readonly Regex NumberPattern = new(
        @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Channel(string Name, string Units);

    private sealed record Brush(
        SrgbColor Color,
        double ThicknessDip,
        byte Alpha);

    /// <summary>
    /// Builds absolute slide-space stroke plans for an imported Ink shape.
    /// FreeP-generated InkML already uses absolute slide coordinates. For native InkML that
    /// lacks the FreeP marker, coordinates that fit entirely inside the content-part frame are
    /// treated as frame-local and translated by the shape anchor.
    /// </summary>
    public static IReadOnlyList<SlideShowInkRenderStrokePlan> Build(
        SlideShape shape,
        Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(presentation);

        if (shape.Kind != SlideShapeKind.Ink || shape.PreservedObject is not { ObjectKind: PreservedObjectKind.Ink } info)
            return Array.Empty<SlideShowInkRenderStrokePlan>();

        var inkBytes = FindInkPart(info);
        if (inkBytes is null || inkBytes.Length == 0)
            return Array.Empty<SlideShowInkRenderStrokePlan>();

        XDocument document;
        try
        {
            document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(inkBytes), LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return Array.Empty<SlideShowInkRenderStrokePlan>();
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "ink", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<SlideShowInkRenderStrokePlan>();

        var channels = ReadChannels(root);
        if (channels.Count < 2)
            return Array.Empty<SlideShowInkRenderStrokePlan>();

        var brushes = ReadBrushes(root, channels);
        var slideWidthDip = presentation.SlideSizeCxEmu / DrawingMlCoordinateUnits.EmuPerPixel;
        var slideHeightDip = presentation.SlideSizeCyEmu / DrawingMlCoordinateUnits.EmuPerPixel;
        var frame = new LayoutRect(
            shape.OffsetXEmu / DrawingMlCoordinateUnits.EmuPerPixel,
            shape.OffsetYEmu / DrawingMlCoordinateUnits.EmuPerPixel,
            shape.ExtentCxEmu / DrawingMlCoordinateUnits.EmuPerPixel,
            shape.ExtentCyEmu / DrawingMlCoordinateUnits.EmuPerPixel);
        var isFreePAbsolute = string.Equals(
            GetAttribute(root, "format", FreePInk),
            "freep-slideshow-ink",
            StringComparison.OrdinalIgnoreCase);

        var result = new List<SlideShowInkRenderStrokePlan>();
        foreach (var trace in root.Descendants().Where(element => element.Name.LocalName == "trace"))
        {
            var points = ReadTracePoints(trace, channels);
            if (points.Count == 0)
                continue;

            if (!isFreePAbsolute && IsFrameLocal(points, frame))
            {
                points = points
                    .Select(point => new LayoutPoint(point.X + frame.Left, point.Y + frame.Top))
                    .ToList();
            }

            var brush = ResolveBrush(trace, brushes, channels);
            result.Add(new SlideShowInkRenderStrokePlan(
                points
                    .Where(point => point.X >= -slideWidthDip && point.X <= slideWidthDip * 2
                        && point.Y >= -slideHeightDip && point.Y <= slideHeightDip * 2)
                    .ToArray(),
                brush.Color,
                brush.ThicknessDip,
                brush.Alpha));
        }

        return result.Where(stroke => stroke.Points.Count > 0).ToArray();
    }

    private static byte[]? FindInkPart(PreservedObjectInfo info)
    {
        foreach (var part in info.Parts)
        {
            if (info.PartContentTypes.TryGetValue(part.Key, out var contentType)
                && contentType.Contains("inkml", StringComparison.OrdinalIgnoreCase))
            {
                return part.Value;
            }
        }

        return info.Parts
            .Where(part => part.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(part => part.Value)
            .FirstOrDefault(bytes => bytes.AsSpan().IndexOf("<ink"u8) >= 0);
    }

    private static List<Channel> ReadChannels(XElement root)
    {
        var format = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "traceFormat");
        if (format is null)
            return new List<Channel>();

        return format.Elements()
            .Where(element => element.Name.LocalName == "channel")
            .Select(element => new Channel(
                GetAttribute(element, "name") ?? string.Empty,
                GetAttribute(element, "units") ?? string.Empty))
            .ToList();
    }

    private static Dictionary<string, Brush> ReadBrushes(XElement root, IReadOnlyList<Channel> channels)
    {
        var result = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);
        foreach (var brushElement in root.Descendants().Where(element => element.Name.LocalName == "brush"))
        {
            var id = GetAttribute(brushElement, "id", XNamespace.Xml);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var color = SrgbColor.Black;
            var thicknessDip = 1.5;
            byte alpha = 255;
            foreach (var property in brushElement.Elements().Where(element => element.Name.LocalName == "brushProperty"))
            {
                var name = GetAttribute(property, "name");
                var value = GetAttribute(property, "value");
                switch (name?.ToLowerInvariant())
                {
                    case "color":
                        TryParseColor(value, out color);
                        break;
                    case "width":
                        thicknessDip = ConvertToDip(ParseDouble(value, 1.5), GetAttribute(property, "units"));
                        break;
                    case "transparency":
                        alpha = OpacityToAlpha(1 - ParseTransparency(value));
                        break;
                }
            }

            result[id.TrimStart('#')] = new Brush(color, Math.Max(0.1, thicknessDip), alpha);
        }

        return result;
    }

    private static Brush ResolveBrush(
        XElement trace,
        IReadOnlyDictionary<string, Brush> brushes,
        IReadOnlyList<Channel> channels)
    {
        var brushId = GetAttribute(trace, "brushRef")?.TrimStart('#');
        var brush = brushId is not null && brushes.TryGetValue(brushId, out var resolved)
            ? resolved
            : new Brush(SrgbColor.Black, 1.5, 255);

        var color = TryParseColor(GetAttribute(trace, "color", FreePInk), out var traceColor)
            ? traceColor
            : brush.Color;
        var thickness = ParseOptionalDouble(GetAttribute(trace, "thicknessDip", FreePInk))
            ?? brush.ThicknessDip;
        var opacity = ParseOptionalDouble(GetAttribute(trace, "opacity", FreePInk));
        var alpha = opacity.HasValue ? OpacityToAlpha(opacity.Value) : brush.Alpha;
        return new Brush(color, Math.Max(0.1, thickness), alpha);
    }

    private static List<LayoutPoint> ReadTracePoints(XElement trace, IReadOnlyList<Channel> channels)
    {
        var values = NumberPattern.Matches(trace.Value)
            .Select(match => ParseDouble(match.Value, double.NaN))
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        if (values.Length < channels.Count)
            return new List<LayoutPoint>();

        var xIndex = -1;
        var yIndex = -1;
        for (var channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            if (channels[channelIndex].Name.Equals("X", StringComparison.OrdinalIgnoreCase))
                xIndex = channelIndex;
            else if (channels[channelIndex].Name.Equals("Y", StringComparison.OrdinalIgnoreCase))
                yIndex = channelIndex;
        }
        if (xIndex < 0 || yIndex < 0)
            return new List<LayoutPoint>();
        var result = new List<LayoutPoint>();
        for (var index = 0; index + channels.Count <= values.Length; index += channels.Count)
        {
            result.Add(new LayoutPoint(
                ConvertToDip(values[index + xIndex], channels[xIndex].Units),
                ConvertToDip(values[index + yIndex], channels[yIndex].Units)));
        }

        return result;
    }

    private static bool IsFrameLocal(IReadOnlyList<LayoutPoint> points, LayoutRect frame) =>
        frame.Width > 0 && frame.Height > 0
        && points.All(point => point.X >= -1 && point.Y >= -1
            && point.X <= frame.Width + 1 && point.Y <= frame.Height + 1);

    private static string? GetAttribute(XElement element, string localName, XNamespace? namespaceName = null)
    {
        var attribute = namespaceName is null
            ? element.Attributes().FirstOrDefault(item => item.Name.LocalName == localName)
            : element.Attribute(namespaceName + localName);
        return attribute?.Value;
    }

    private static double ConvertToDip(double value, string? units) =>
        units?.Trim().ToLowerInvariant() switch
        {
            "cm" => value * 96 / 2.54,
            "mm" => value * 96 / 25.4,
            "in" or "inch" or "inches" => value * 96,
            "pt" or "point" or "points" => value * 96 / 72,
            "m" => value * 96 / 0.0254,
            "um" => value * 96 / 25400,
            "nm" => value * 96 / 25400000,
            _ => value,
        };

    private static double ParseTransparency(string? value)
    {
        var parsed = ParseDouble(value, 0);
        return parsed > 1 ? Math.Clamp(parsed / 255, 0, 1) : Math.Clamp(parsed, 0, 1);
    }

    private static byte OpacityToAlpha(double opacity) =>
        (byte)Math.Clamp(Math.Round(Math.Clamp(opacity, 0, 1) * 255), 0, 255);

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static double? ParseOptionalDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool TryParseColor(string? value, out SrgbColor color)
    {
        color = SrgbColor.Black;
        if (!RgbColorTextCodec.TryParse(
                value,
                RgbColorTextProfile.FlexibleInk,
                out var rgb))
            return false;

        color = new SrgbColor(rgb.R, rgb.G, rgb.B);
        return true;
    }
}
