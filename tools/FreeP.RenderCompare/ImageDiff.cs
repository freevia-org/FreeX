using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.ToolsShared.Wpf;

namespace FreeP.RenderCompare;

/// <summary>
/// Pixel-level image comparison between two PNG files.
///
/// Algorithm (adapted from FreeX.SheetImageCompare / FreeX.ChartFileCompare):
///   Both images are decoded to Bgra32, alpha-composited over white, then compared
///   channel-by-channel (R, G, B only — 3 channels per pixel).
///
/// Metrics reported:
///   MeanChannelDiffPercent — mean absolute channel diff across all pixels, expressed
///     as a percentage of the maximum possible diff (255 × 3 × pixelCount × 100).
///     0.0 = identical; 100.0 = maximally different everywhere.
///   MaxChannelDiff — maximum single-channel absolute difference (0..255).
///
/// Heatmap (optional):
///   Each pixel's mean channel diff (0..255) is mapped to a false-colour scale
///   (blue=0, green=moderate, red=255) for visual inspection.
///
/// The comparison uses the larger of the two image sizes as the canvas.  Pixels
/// that exist in one image but not the other are compared against white (0xFF).
/// </summary>
internal static class ImageDiff
{
    internal const int DefaultChangedChannelThreshold = 24;

    /// <summary>Compare two PNG files and return metrics. Writes a heatmap if <paramref name="heatmapPath"/> is non-null.</summary>
    internal static DiffResult Compare(string pathA, string pathB, string? heatmapPath = null)
    {
        var bmpA = WpfImageDiff.LoadBitmap(pathA);
        var bmpB = WpfImageDiff.LoadBitmap(pathB);

        var widthA  = bmpA.PixelWidth;
        var heightA = bmpA.PixelHeight;
        var widthB  = bmpB.PixelWidth;
        var heightB = bmpB.PixelHeight;

        // Use maximum of the two dimensions as comparison canvas
        var w = Math.Max(widthA, widthB);
        var h = Math.Max(heightA, heightB);

        var pixA = WpfImageDiff.GetBgra32Pixels(bmpA, widthA, heightA);
        var pixB = WpfImageDiff.GetBgra32Pixels(bmpB, widthB, heightB);

        long totalDiff  = 0;
        int  maxDiff    = 0;
        long pixelCount = (long)w * h;

        // Heatmap data: one byte per pixel = mean channel diff (0..255)
        byte[]? heatPixels = heatmapPath != null ? new byte[pixelCount] : null;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                // Sample A (white if outside bounds)
                double aR, aG, aB;
                if (x < widthA && y < heightA)
                {
                    var off = (y * widthA + x) * 4;
                    var alpha = pixA[off + 3] / 255.0;
                    // Bgra32: B=off+0, G=off+1, R=off+2, A=off+3
                    aB = pixA[off + 0] * alpha + 255.0 * (1.0 - alpha);
                    aG = pixA[off + 1] * alpha + 255.0 * (1.0 - alpha);
                    aR = pixA[off + 2] * alpha + 255.0 * (1.0 - alpha);
                }
                else
                {
                    aR = aG = aB = 255.0;
                }

                // Sample B (white if outside bounds)
                double bR, bG, bB;
                if (x < widthB && y < heightB)
                {
                    var off = (y * widthB + x) * 4;
                    var alpha = pixB[off + 3] / 255.0;
                    bB = pixB[off + 0] * alpha + 255.0 * (1.0 - alpha);
                    bG = pixB[off + 1] * alpha + 255.0 * (1.0 - alpha);
                    bR = pixB[off + 2] * alpha + 255.0 * (1.0 - alpha);
                }
                else
                {
                    bR = bG = bB = 255.0;
                }

                var dR = (int)Math.Abs(aR - bR);
                var dG = (int)Math.Abs(aG - bG);
                var dB = (int)Math.Abs(aB - bB);

                totalDiff += dR + dG + dB;

                var channelMax = Math.Max(dR, Math.Max(dG, dB));
                if (channelMax > maxDiff)
                    maxDiff = channelMax;

                if (heatPixels != null)
                    heatPixels[y * w + x] = (byte)Math.Min(255, (dR + dG + dB) / 3);
            }
        }

        double maxPossible = pixelCount * 3.0 * 255.0;
        double meanPct     = maxPossible > 0 ? totalDiff / maxPossible * 100.0 : 0.0;

        if (heatmapPath != null && heatPixels != null)
            WriteHeatmap(heatPixels, w, h, heatmapPath);

        return new DiffResult(widthA, heightA, widthB, heightB, meanPct, maxDiff);
    }

    internal static NormalizedDiffResult CompareNormalized(
        string pathA,
        string pathB,
        int normalizedWidth,
        int normalizedHeight,
        string? heatmapPath = null,
        int changedChannelThreshold = DefaultChangedChannelThreshold)
    {
        var bitmapA = WpfImageDiff.LoadBitmap(pathA);
        var bitmapB = WpfImageDiff.LoadBitmap(pathB);
        var pixelsA = NormalizeOverWhite(bitmapA, normalizedWidth, normalizedHeight);
        var pixelsB = NormalizeOverWhite(bitmapB, normalizedWidth, normalizedHeight);
        var backgroundA = EstimateCornerBackground(pixelsA, normalizedWidth, normalizedHeight);
        var backgroundB = EstimateCornerBackground(pixelsB, normalizedWidth, normalizedHeight);

        long totalDelta = 0;
        var maxDelta = 0;
        long changedPixels = 0;
        long foregroundUnionPixels = 0;
        long foregroundChangedPixels = 0;
        var pixelCount = checked((long)normalizedWidth * normalizedHeight);
        var heatPixels = heatmapPath is null ? null : new byte[checked((int)pixelCount)];

        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var offset = checked((int)pixel * 4);
            var dB = Math.Abs(pixelsA[offset] - pixelsB[offset]);
            var dG = Math.Abs(pixelsA[offset + 1] - pixelsB[offset + 1]);
            var dR = Math.Abs(pixelsA[offset + 2] - pixelsB[offset + 2]);
            var pixelMax = Math.Max(dR, Math.Max(dG, dB));
            var changed = pixelMax >= changedChannelThreshold;
            var foreground = IsForeground(pixelsA, offset, backgroundA) ||
                IsForeground(pixelsB, offset, backgroundB);

            totalDelta += dR + dG + dB;
            maxDelta = Math.Max(maxDelta, pixelMax);
            if (changed)
                changedPixels++;
            if (foreground)
            {
                foregroundUnionPixels++;
                if (changed)
                    foregroundChangedPixels++;
            }
            if (heatPixels is not null)
                heatPixels[pixel] = (byte)Math.Min(255, (dR + dG + dB) / 3);
        }

        if (heatmapPath is not null && heatPixels is not null)
            WriteHeatmap(heatPixels, normalizedWidth, normalizedHeight, heatmapPath);

        var meanChannelDelta = pixelCount == 0 ? 0 : totalDelta / (pixelCount * 3d);
        return new NormalizedDiffResult(
            bitmapA.PixelWidth,
            bitmapA.PixelHeight,
            bitmapB.PixelWidth,
            bitmapB.PixelHeight,
            normalizedWidth,
            normalizedHeight,
            pixelCount,
            foregroundUnionPixels,
            changedPixels,
            foregroundChangedPixels,
            pixelCount == 0 ? 0 : changedPixels / (double)pixelCount,
            foregroundUnionPixels == 0 ? 0 : foregroundChangedPixels / (double)foregroundUnionPixels,
            meanChannelDelta,
            maxDelta,
            changedChannelThreshold,
            "alpha-composited-over-white; corner-background-estimate; high-quality-scale-to-logical-96-dpi");
    }

    private static byte[] NormalizeOverWhite(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static (byte B, byte G, byte R) EstimateCornerBackground(byte[] pixels, int width, int height)
    {
        long b = 0;
        long g = 0;
        long r = 0;
        var count = 0;
        var sampleSize = Math.Min(4, Math.Min(width, height));
        foreach (var origin in new[] { (0, 0), (width - sampleSize, 0), (0, height - sampleSize), (width - sampleSize, height - sampleSize) })
        {
            for (var y = origin.Item2; y < origin.Item2 + sampleSize; y++)
            {
                for (var x = origin.Item1; x < origin.Item1 + sampleSize; x++)
                {
                    var offset = (y * width + x) * 4;
                    b += pixels[offset];
                    g += pixels[offset + 1];
                    r += pixels[offset + 2];
                    count++;
                }
            }
        }
        return ((byte)(b / count), (byte)(g / count), (byte)(r / count));
    }

    private static bool IsForeground(byte[] pixels, int offset, (byte B, byte G, byte R) background) =>
        Math.Max(
            Math.Abs(pixels[offset] - background.B),
            Math.Max(
                Math.Abs(pixels[offset + 1] - background.G),
                Math.Abs(pixels[offset + 2] - background.R))) >= 12;

    // -----------------------------------------------------------------------
    // Heatmap writer — false-colour: blue (diff=0) -> green -> red (diff=255)
    // -----------------------------------------------------------------------
    private static void WriteHeatmap(byte[] heatPixels, int w, int h, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Build a Bgra32 buffer
        var buf = new byte[(long)w * h * 4];
        for (var i = 0; i < heatPixels.Length; i++)
        {
            var v = heatPixels[i]; // 0=same .. 255=max diff
            byte r, g, b;
            if (v < 128)
            {
                // blue -> green
                b = (byte)(255 - v * 2);
                g = (byte)(v * 2);
                r = 0;
            }
            else
            {
                // green -> red
                b = 0;
                g = (byte)(255 - (v - 128) * 2);
                r = (byte)((v - 128) * 2);
            }

            var off = i * 4;
            buf[off + 0] = b;
            buf[off + 1] = g;
            buf[off + 2] = r;
            buf[off + 3] = 255;
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

}

/// <summary>Result of <see cref="ImageDiff.Compare"/>.</summary>
internal sealed record DiffResult(
    int    WidthA,
    int    HeightA,
    int    WidthB,
    int    HeightB,
    double MeanChannelDiffPercent,
    int    MaxChannelDiff);

internal sealed record NormalizedDiffResult(
    int WidthA,
    int HeightA,
    int WidthB,
    int HeightB,
    int NormalizedWidth,
    int NormalizedHeight,
    long ComparedPixelCount,
    long ForegroundUnionPixelCount,
    long ChangedPixelCount,
    long ForegroundChangedPixelCount,
    double ChangedPixelRatio,
    double ForegroundChangedPixelRatio,
    double MeanChannelDelta,
    int MaxChannelDelta,
    int ChangedChannelThreshold,
    string BackgroundHandling);
