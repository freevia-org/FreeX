namespace Free.Shared.Pdf;

/// <summary>
/// Renderer-neutral geometry and normalization decisions shared by the PDF adapters. The
/// returned values stay in PDF coordinates or plain numeric pixel space; backend-specific point
/// and path types remain owned by the adapter.
/// </summary>
public static class PdfRenderGeometry
{
    public static double ToCanvasY(double pageHeight, double pdfY) => pageHeight - pdfY;

    public static double ToCanvasTop(double pageHeight, double pdfY, double height) =>
        pageHeight - (pdfY + height);

    public static double RoundedClipRadius(double width, double height) =>
        Math.Min(width, height) * 0.18;

    public static bool TryGetImageSourceRect(
        int imageWidth,
        int imageHeight,
        PdfImageSourceCrop crop,
        out PdfImagePixelRect sourceRect)
    {
        sourceRect = default;
        if (!crop.HasCrop || imageWidth <= 0 || imageHeight <= 0)
            return false;

        var sourceX = Clamp(
            (int)Math.Round(NormalizeCropFraction(crop.Left) * imageWidth),
            0,
            imageWidth - 1);
        var sourceY = Clamp(
            (int)Math.Round(NormalizeCropFraction(crop.Top) * imageHeight),
            0,
            imageHeight - 1);
        var sourceWidth = Clamp(
            (int)Math.Round((1.0 - NormalizeCropFraction(crop.Left) - NormalizeCropFraction(crop.Right)) * imageWidth),
            1,
            imageWidth - sourceX);
        var sourceHeight = Clamp(
            (int)Math.Round((1.0 - NormalizeCropFraction(crop.Top) - NormalizeCropFraction(crop.Bottom)) * imageHeight),
            1,
            imageHeight - sourceY);

        if (sourceX == 0 &&
            sourceY == 0 &&
            sourceWidth == imageWidth &&
            sourceHeight == imageHeight)
            return false;

        sourceRect = new PdfImagePixelRect(sourceX, sourceY, sourceWidth, sourceHeight);
        return true;
    }

    public static PdfPathPoint[] GetPresetClipPolygonPoints(
        double x,
        double y,
        double width,
        double height,
        PdfImageClipKind clipKind)
    {
        if (width <= 0 || height <= 0)
            return [];

        var right = x + width;
        var top = y + height;
        var midX = x + width / 2.0;
        var midY = y + height / 2.0;
        var quarterX = x + width * 0.25;
        var threeQuarterX = x + width * 0.75;

        return clipKind switch
        {
            PdfImageClipKind.Triangle =>
            [
                new PdfPathPoint(midX, top),
                new PdfPathPoint(right, y),
                new PdfPathPoint(x, y),
            ],
            PdfImageClipKind.Diamond =>
            [
                new PdfPathPoint(midX, top),
                new PdfPathPoint(right, midY),
                new PdfPathPoint(midX, y),
                new PdfPathPoint(x, midY),
            ],
            PdfImageClipKind.Parallelogram =>
            [
                new PdfPathPoint(quarterX, top),
                new PdfPathPoint(right, top),
                new PdfPathPoint(threeQuarterX, y),
                new PdfPathPoint(x, y),
            ],
            PdfImageClipKind.Hexagon =>
            [
                new PdfPathPoint(quarterX, top),
                new PdfPathPoint(threeQuarterX, top),
                new PdfPathPoint(right, midY),
                new PdfPathPoint(threeQuarterX, y),
                new PdfPathPoint(quarterX, y),
                new PdfPathPoint(x, midY),
            ],
            PdfImageClipKind.Chevron =>
            [
                new PdfPathPoint(x, top),
                new PdfPathPoint(threeQuarterX, top),
                new PdfPathPoint(right, midY),
                new PdfPathPoint(threeQuarterX, y),
                new PdfPathPoint(x, y),
                new PdfPathPoint(quarterX, midY),
            ],
            _ => [],
        };
    }

    public static bool TryNormalizeGradient(
        PdfLinearGradient gradient,
        out PdfGradientStop[] stops)
    {
        stops = [];
        if (!IsFinite(gradient.StartX) ||
            !IsFinite(gradient.StartY) ||
            !IsFinite(gradient.EndX) ||
            !IsFinite(gradient.EndY) ||
            DistanceSquared(gradient.StartX, gradient.StartY, gradient.EndX, gradient.EndY) < 0.000001)
            return false;

        stops = gradient.Stops
            .Where(stop => IsFinite(stop.Position))
            .Select(stop => new PdfGradientStop(Math.Clamp(stop.Position, 0.0, 1.0), stop.Color))
            .OrderBy(stop => stop.Position)
            .ToArray();
        if (stops.Length == 0)
            return false;
        if (stops.Length == 1)
            stops = [stops[0], new PdfGradientStop(1.0, stops[0].Color)];
        if (stops[0].Position > 0.0)
            stops = [new PdfGradientStop(0.0, stops[0].Color), .. stops];
        if (stops[^1].Position < 1.0)
            stops = [.. stops, new PdfGradientStop(1.0, stops[^1].Color)];
        return true;
    }

    public static double NormalizeOpacity(double opacity) =>
        Math.Round(Math.Clamp(double.IsFinite(opacity) ? opacity : 1.0, 0.0, 1.0), 3);

    /// <summary>
    /// Builds the two deterministic bands on each face of a bevel. The returned polygons are in
    /// PDF user space and are deliberately clipped to the effect bounds by each writer. A light
    /// direction of 135 degrees is the conventional upper-left Office bevel direction in PDF
    /// coordinates (x right, y up).
    /// </summary>
    public static IReadOnlyList<PdfBevelBand> GetBevelBands(PdfEffectGroup group)
    {
        if (!IsFinite(group.BoundsX) ||
            !IsFinite(group.BoundsY) ||
            !IsFinite(group.BoundsWidth) ||
            !IsFinite(group.BoundsHeight) ||
            group.BoundsWidth <= 0 ||
            group.BoundsHeight <= 0)
            return [];
        var width = group.BoundsWidth;
        var height = group.BoundsHeight;

        var fallback = Math.Max(0, double.IsFinite(group.Parameters.Radius) ? group.Parameters.Radius : 0);
        var bevelWidth = NormalizeBevelDimension(group.Parameters.BevelWidth, fallback, width);
        var bevelHeight = NormalizeBevelDimension(group.Parameters.BevelHeight, fallback, height);
        if (bevelWidth <= 0 || bevelHeight <= 0)
            return [];

        var direction = double.IsFinite(group.Parameters.BevelLightDirectionDegrees)
            ? group.Parameters.BevelLightDirectionDegrees * Math.PI / 180d
            : 135 * Math.PI / 180d;
        var lightX = Math.Cos(direction);
        var lightY = Math.Sin(direction);
        var bands = new List<PdfBevelBand>(8);
        AddBevelEdgeBands(
            bands,
            [
                new PdfPathPoint(group.BoundsX, group.BoundsY + height),
                new PdfPathPoint(group.BoundsX + width, group.BoundsY + height),
            ],
            [
                new PdfPathPoint(group.BoundsX, group.BoundsY + height - bevelHeight),
                new PdfPathPoint(group.BoundsX + width, group.BoundsY + height - bevelHeight),
            ],
            lightY >= 0,
            0,
            bevelHeight);
        AddBevelEdgeBands(
            bands,
            [
                new PdfPathPoint(group.BoundsX + width, group.BoundsY),
                new PdfPathPoint(group.BoundsX + width, group.BoundsY + height),
            ],
            [
                new PdfPathPoint(group.BoundsX + width - bevelWidth, group.BoundsY),
                new PdfPathPoint(group.BoundsX + width - bevelWidth, group.BoundsY + height),
            ],
            lightX >= 0,
            bevelWidth,
            0);
        AddBevelEdgeBands(
            bands,
            [
                new PdfPathPoint(group.BoundsX, group.BoundsY),
                new PdfPathPoint(group.BoundsX + width, group.BoundsY),
            ],
            [
                new PdfPathPoint(group.BoundsX, group.BoundsY + bevelHeight),
                new PdfPathPoint(group.BoundsX + width, group.BoundsY + bevelHeight),
            ],
            lightY < 0,
            0,
            -bevelHeight);
        AddBevelEdgeBands(
            bands,
            [
                new PdfPathPoint(group.BoundsX, group.BoundsY),
                new PdfPathPoint(group.BoundsX, group.BoundsY + height),
            ],
            [
                new PdfPathPoint(group.BoundsX + bevelWidth, group.BoundsY),
                new PdfPathPoint(group.BoundsX + bevelWidth, group.BoundsY + height),
            ],
            lightX < 0,
            -bevelWidth,
            0);
        return bands;
    }

    public static bool IsSupportedImageContentType(string? contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim();
        return normalized is not null &&
               (normalized.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("image/jpg", StringComparison.OrdinalIgnoreCase));
    }

    private static double NormalizeCropFraction(double value) =>
        double.IsFinite(value) ? value : 0.0;

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static double NormalizeBevelDimension(double value, double fallback, double bound) =>
        Math.Min(bound / 2, Math.Max(0, IsFinite(value) && value > 0 ? value : fallback));

    private static void AddBevelEdgeBands(
        List<PdfBevelBand> bands,
        IReadOnlyList<PdfPathPoint> outer,
        IReadOnlyList<PdfPathPoint> inner,
        bool highlight,
        double offsetX,
        double offsetY)
    {
        var first = outer[0];
        var second = outer[1];
        var innerFirst = inner[0];
        var innerSecond = inner[1];
        var firstMid = new PdfPathPoint(
            first.X + (innerFirst.X - first.X) * 0.5,
            first.Y + (innerFirst.Y - first.Y) * 0.5);
        var secondMid = new PdfPathPoint(
            second.X + (innerSecond.X - second.X) * 0.5,
            second.Y + (innerSecond.Y - second.Y) * 0.5);

        AddBand([first, second, secondMid, firstMid], highlight, 0, offsetX, offsetY);
        AddBand([firstMid, secondMid, innerSecond, innerFirst], highlight, 1, offsetX, offsetY);

        void AddBand(
            PdfPathPoint[] points,
            bool isHighlight,
            int index,
            double bandOffsetX,
            double bandOffsetY) =>
            bands.Add(new PdfBevelBand(
                points,
                bandOffsetX,
                bandOffsetY,
                isHighlight,
                index == 0 ? 0.72 : 0.44));
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return dx * dx + dy * dy;
    }

    private static int Clamp(int value, int min, int max) =>
        Math.Max(min, Math.Min(value, max));
}

public readonly record struct PdfImagePixelRect(int X, int Y, int Width, int Height);

public sealed record PdfBevelBand(
    IReadOnlyList<PdfPathPoint> Points,
    double OffsetX,
    double OffsetY,
    bool IsHighlight,
    double OpacityScale);
