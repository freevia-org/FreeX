namespace FreeX.App.Host.Tests;

internal static class ImageTestData
{
    public static byte[] CreatePngBytes(int pixelWidth, int pixelHeight, double dpiX = 96, double dpiY = 96)
    {
        var stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0xFF;
            pixels[index + 1] = 0xFF;
            pixels[index + 2] = 0xFF;
            pixels[index + 3] = 0xFF;
        }

        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            pixelWidth,
            pixelHeight,
            dpiX,
            dpiY,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
