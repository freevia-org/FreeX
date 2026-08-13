using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record PictureRasterSurfacePlan(int PixelWidth, int PixelHeight);

/// <summary>
/// Owns the toolkit-neutral sizing and model construction used by Insert Picture and Insert Icons.
/// Renderers only decode/rasterize a selected file and hand the resulting PNG pixels to this planner.
/// </summary>
public static class PictureInsertionPlanner
{
    public const double PixelsPerPoint = 96d / 72d;
    public const double DefaultMaximumWidthPt = 400d;
    public const int DefaultVectorRasterExtentPx = 400;
    public const double DefaultIconWidthPt = 72d;

    public static IReadOnlyList<string> SupportedFilePatterns { get; } = Array.AsReadOnly(
        new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.tif", "*.tiff", "*.svg" });

    public static IReadOnlyList<string> SupportedMimeTypes { get; } = Array.AsReadOnly(
        new[] { "image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/svg+xml" });

    public static string BuildWindowsFileDialogFilter(string label = "Images")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var patterns = string.Join(';', SupportedFilePatterns);
        return $"{label} ({patterns})|{patterns}|All files (*.*)|*.*";
    }

    public static PictureRasterSurfacePlan BuildVectorRasterSurface(
        double sourceWidth,
        double sourceHeight,
        int maximumExtentPx = DefaultVectorRasterExtentPx)
    {
        if (maximumExtentPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumExtentPx));

        if (!double.IsFinite(sourceWidth) || sourceWidth <= 0
            || !double.IsFinite(sourceHeight) || sourceHeight <= 0)
        {
            return new PictureRasterSurfacePlan(maximumExtentPx, maximumExtentPx);
        }

        var scale = maximumExtentPx / Math.Max(sourceWidth, sourceHeight);
        return new PictureRasterSurfacePlan(
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    public static InlineImage CreatePngImage(
        byte[] pngBytes,
        int pixelWidth,
        int pixelHeight,
        double maximumWidthPt = DefaultMaximumWidthPt)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
            throw new ArgumentException("PNG bytes are empty.", nameof(pngBytes));
        if (pixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        if (!double.IsFinite(maximumWidthPt) || maximumWidthPt <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWidthPt));

        var widthPt = pixelWidth / PixelsPerPoint;
        var heightPt = pixelHeight / PixelsPerPoint;
        if (widthPt > maximumWidthPt)
        {
            var scale = maximumWidthPt / widthPt;
            widthPt = maximumWidthPt;
            heightPt *= scale;
        }

        return new InlineImage(pngBytes, widthPt, heightPt, ImageFormat.Png)
        {
            OriginalPixelWidth = pixelWidth,
            OriginalPixelHeight = pixelHeight,
        };
    }

    public static InlineImage FitIcon(InlineImage image, double maximumWidthPt = DefaultIconWidthPt)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!double.IsFinite(maximumWidthPt) || maximumWidthPt <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWidthPt));
        if (image.WidthPt <= maximumWidthPt || image.WidthPt <= 0)
            return image;

        var scale = maximumWidthPt / image.WidthPt;
        var fitted = image.Clone();
        fitted.WidthPt = maximumWidthPt;
        fitted.HeightPt = image.HeightPt * scale;
        return fitted;
    }
}
