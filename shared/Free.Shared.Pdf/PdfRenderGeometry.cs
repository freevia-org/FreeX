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
