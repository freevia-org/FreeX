namespace FreeX.ParityCompare.Core;

/// <summary>
/// Portable reimplementation of the mean-pixel-diff metric used by
/// <c>tools/FreeX.SheetImageCompare</c> and <c>tools/FreeX.ExcelExamplesCharts</c>:
/// letterbox-resize both images to a canonical size, alpha-composite over white,
/// then take the mean per-channel absolute difference as a percentage of full scale.
/// Identical images score 0; fully inverted images score 100.
/// </summary>
public static class ImageDiff
{
    public const int CanonicalWidth = 800;
    public const int CanonicalHeight = 600;

    /// <summary>
    /// Compute mean per-channel abs diff % (0..100) between two decoded images.
    /// Both are letterbox-resized to (<paramref name="w"/>,<paramref name="h"/>) and
    /// composited over white before comparison.
    /// </summary>
    public static double MeanPixelDiffPercent(
        PixelImage a, PixelImage b, int w = CanonicalWidth, int h = CanonicalHeight)
    {
        byte[] pa = CompositeOverWhite(LetterboxResize(a, w, h), w, h);
        byte[] pb = CompositeOverWhite(LetterboxResize(b, w, h), w, h);

        long total = 0;
        int count = w * h;
        for (int i = 0; i < count; i++)
        {
            int o = i * 4;
            // composited buffers are opaque RGB in BGRA layout (alpha already folded in)
            total += Math.Abs(pa[o] - pb[o]);
            total += Math.Abs(pa[o + 1] - pb[o + 1]);
            total += Math.Abs(pa[o + 2] - pb[o + 2]);
        }

        double max = (double)count * 3 * 255;
        return total / max * 100.0;
    }

    public static double MeanPixelDiffPercentFromFiles(string pathA, string pathB) =>
        MeanPixelDiffPercent(PngCodec.DecodeFile(pathA), PngCodec.DecodeFile(pathB));

    /// <summary>
    /// Compute a mean pixel difference after both images have been resized to the same
    /// logical viewport. Unlike <see cref="MeanPixelDiffPercent"/>, this method does
    /// not letterbox: callers use it only when the capture manifests establish that
    /// the two images represent exactly the same client rectangle at different DPI
    /// scales. It is intended for Office-vs-app chrome evidence, where a letterbox
    /// would make a DPI-only size difference look like a visual difference.
    /// </summary>
    public static double LogicalViewportMeanPixelDiffPercent(
        PixelImage a,
        PixelImage b,
        int logicalWidth,
        int logicalHeight)
    {
        if (logicalWidth <= 0 || logicalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth), "Logical viewport dimensions must be positive.");

        byte[] pa = CompositeOverWhite(StretchResize(a, logicalWidth, logicalHeight), logicalWidth, logicalHeight);
        byte[] pb = CompositeOverWhite(StretchResize(b, logicalWidth, logicalHeight), logicalWidth, logicalHeight);

        long total = 0;
        int count = logicalWidth * logicalHeight;
        for (int i = 0; i < count; i++)
        {
            int o = i * 4;
            total += Math.Abs(pa[o] - pb[o]);
            total += Math.Abs(pa[o + 1] - pb[o + 1]);
            total += Math.Abs(pa[o + 2] - pb[o + 2]);
        }

        return total / ((double)count * 3 * 255) * 100.0;
    }

    /// <summary>
    /// Nearest-neighbour letterbox resize: scale to fit inside (w,h) preserving aspect,
    /// center on a transparent canvas. Deterministic and dependency-free (no GPU/WPF).
    /// </summary>
    public static PixelImage LetterboxResize(PixelImage src, int w, int h)
    {
        var dst = new byte[w * h * 4]; // transparent canvas (all zero)

        double scale = Math.Min((double)w / src.Width, (double)h / src.Height);
        int dw = Math.Max(1, (int)Math.Round(src.Width * scale));
        int dh = Math.Max(1, (int)Math.Round(src.Height * scale));
        int offX = (w - dw) / 2;
        int offY = (h - dh) / 2;

        for (int y = 0; y < dh; y++)
        {
            int sy = Math.Min(src.Height - 1, (int)((y + 0.5) / scale));
            int destRow = (offY + y) * w;
            int srcRow = sy * src.Width;
            for (int x = 0; x < dw; x++)
            {
                int sx = Math.Min(src.Width - 1, (int)((x + 0.5) / scale));
                int si = (srcRow + sx) * 4;
                int di = (destRow + offX + x) * 4;
                dst[di] = src.Pixels[si];
                dst[di + 1] = src.Pixels[si + 1];
                dst[di + 2] = src.Pixels[si + 2];
                dst[di + 3] = src.Pixels[si + 3];
            }
        }
        return new PixelImage(w, h, dst);
    }

    /// <summary>Nearest-neighbour resize to an exact viewport, with no letterboxing.</summary>
    public static PixelImage StretchResize(PixelImage src, int w, int h)
    {
        var dst = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(src.Height - 1, (int)((long)y * src.Height / h));
            int srcRow = sy * src.Width;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(src.Width - 1, (int)((long)x * src.Width / w));
                int si = (srcRow + sx) * 4;
                int di = (dstRow + x) * 4;
                dst[di] = src.Pixels[si];
                dst[di + 1] = src.Pixels[si + 1];
                dst[di + 2] = src.Pixels[si + 2];
                dst[di + 3] = src.Pixels[si + 3];
            }
        }
        return new PixelImage(w, h, dst);
    }

    /// <summary>
    /// Alpha-composite a BGRA image over an opaque white background, returning an
    /// opaque BGRA buffer (alpha forced to 255). Matches the WPF tools' compositing.
    /// </summary>
    public static byte[] CompositeOverWhite(PixelImage img, int w, int h)
    {
        var outp = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            double alpha = img.Pixels[o + 3] / 255.0;
            for (int c = 0; c < 3; c++)
                outp[o + c] = (byte)Math.Round(img.Pixels[o + c] * alpha + 255 * (1 - alpha));
            outp[o + 3] = 255;
        }
        return outp;
    }
}
