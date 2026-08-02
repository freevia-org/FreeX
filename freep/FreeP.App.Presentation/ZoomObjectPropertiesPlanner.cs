using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared command metadata and defaults for the PowerPoint Zoom format dialog.</summary>
public static class ZoomObjectPropertiesPlanner
{
    public const string CommandId = "freep.zoom.format";
    public const string DialogTitle = "Zoom Format";

    public sealed record SummaryZoomTileLayoutEdit(
        string SectionId,
        int OffsetFactorX,
        int OffsetFactorY,
        int ScaleFactorX,
        int ScaleFactorY);

    public static ZoomObjectProperties Effective(PreservedObjectInfo? info) =>
        info?.ZoomProperties ?? new ZoomObjectProperties(true, "preview", null, true);

    public static bool IsSupportedImageType(string? imageType) =>
        string.Equals(imageType, "preview", StringComparison.OrdinalIgnoreCase)
        || string.Equals(imageType, "cover", StringComparison.OrdinalIgnoreCase);

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
