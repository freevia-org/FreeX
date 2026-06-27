using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeP.RenderCompare;

internal static class PixelDiversity
{
    private const int MinimumUniqueColors = 2;
    private const double MaximumDominantColorPercent = 99.95;

    internal static PixelDiversityStats Analyze(string pngPath)
    {
        using var stream = File.OpenRead(pngPath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        BitmapSource source = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        source.CopyPixels(pixels, stride, 0);

        var counts = new Dictionary<uint, int>();
        var opaquePixels = 0;
        var dominantColor = 0u;
        var dominantCount = 0;

        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var a = pixels[offset + 3];
            if (a > 0)
                opaquePixels++;

            var argb = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
            var count = counts.TryGetValue(argb, out var current) ? current + 1 : 1;
            counts[argb] = count;
            if (count > dominantCount)
            {
                dominantCount = count;
                dominantColor = argb;
            }
        }

        var totalPixels = checked(width * height);
        var dominantPercent = totalPixels == 0 ? 100 : dominantCount * 100.0 / totalPixels;
        string? failure = null;
        if (totalPixels <= 0)
            failure = "image has no pixels";
        else if (opaquePixels == 0)
            failure = "image has no non-transparent pixels";
        else if (counts.Count < MinimumUniqueColors)
            failure = $"image has only {counts.Count} unique color(s)";
        else if (dominantPercent >= MaximumDominantColorPercent)
            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"dominant color covers {dominantPercent:F4}% of pixels");

        return new PixelDiversityStats(
            pngPath,
            width,
            height,
            totalPixels,
            counts.Count,
            opaquePixels,
            dominantColor,
            dominantCount,
            dominantPercent,
            failure);
    }
}

internal sealed record PixelDiversityStats(
    string Path,
    int Width,
    int Height,
    int TotalPixels,
    int UniqueColors,
    int OpaquePixels,
    uint DominantColorArgb,
    int DominantColorPixels,
    double DominantColorPercent,
    string? FailureReason)
{
    internal bool IsTrustworthy => FailureReason is null;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"pixel diversity: {Width}x{Height}, pixels={TotalPixels}, unique={UniqueColors}, opaque={OpaquePixels}, dominant=#{DominantColorArgb:X8} {DominantColorPixels} ({DominantColorPercent:F4}%)");
}
