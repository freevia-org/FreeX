using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeX.ToolsShared.Wpf;

/// <summary>
/// Shared WPF bitmap helpers for the FreeX visual-fidelity tools. This preserves the
/// existing comparer metric: letterbox-resize onto white, alpha-composite over white,
/// then accumulate RGB channel deltas.
/// </summary>
public static class WpfImageDiff
{
    public static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        return source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
    }

    public static double ComputeMeanPixelDiff(string expectedPath, string actualPath, int width, int height)
    {
        var expectedBmp = ResizeTo(LoadBitmap(expectedPath), width, height);
        var actualBmp = File.Exists(actualPath)
            ? ResizeTo(LoadBitmap(actualPath), width, height)
            : CreateWhite(width, height);

        var expectedPixels = GetBgra32Pixels(expectedBmp, width, height);
        var actualPixels = GetBgra32Pixels(actualBmp, width, height);

        long totalDiff = 0;
        var pixelCount = GetBgra32BufferLayout(width, height).PixelCount;
        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 4;
            double expectedAlpha = expectedPixels[offset + 3] / 255.0;
            double actualAlpha = actualPixels[offset + 3] / 255.0;

            for (int channel = 0; channel < 3; channel++)
            {
                double expectedValue = expectedPixels[offset + channel] * expectedAlpha + 255 * (1 - expectedAlpha);
                double actualValue = actualPixels[offset + channel] * actualAlpha + 255 * (1 - actualAlpha);
                totalDiff += (long)Math.Abs(expectedValue - actualValue);
            }
        }

        double maxDiff = (double)pixelCount * 3 * 255;
        return totalDiff / maxDiff * 100.0;
    }

    public static BitmapSource ResizeTo(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            double scale = Math.Min((double)width / source.PixelWidth, (double)height / source.PixelHeight);
            double drawWidth = source.PixelWidth * scale;
            double drawHeight = source.PixelHeight * scale;
            var bounds = new Rect((width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight);
            ctx.DrawImage(source, bounds);
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
    }

    public static BitmapSource CreateWhite(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
    }

    public static byte[] GetBgra32Pixels(BitmapSource bitmap, int width, int height)
    {
        var layout = GetBgra32BufferLayout(width, height);
        var pixels = new byte[layout.BufferLength];
        bitmap.CopyPixels(pixels, layout.Stride, 0);
        return pixels;
    }

    private static (int Stride, int BufferLength, int PixelCount) GetBgra32BufferLayout(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var pixelCount = checked((long)width * height);
        var stride = checked((long)width * 4);
        var bufferLength = checked(pixelCount * 4);
        if (stride > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "The BGRA32 stride exceeds the supported buffer size.");
        if (bufferLength > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "The BGRA32 pixel buffer exceeds the supported buffer size.");

        return ((int)stride, (int)bufferLength, checked((int)pixelCount));
    }
}
