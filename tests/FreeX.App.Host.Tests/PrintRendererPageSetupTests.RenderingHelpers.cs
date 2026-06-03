using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
{
    private static int CountApproximateRgbPixels(FrameworkElement page, byte expectedRed, byte expectedGreen, byte expectedBlue)
    {
        var width = Math.Max(1, (int)Math.Ceiling(page.Width));
        var height = Math.Max(1, (int)Math.Ceiling(page.Height));
        var size = new Size(width, height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];

            if (Math.Abs(red - expectedRed) <= 3 &&
                Math.Abs(green - expectedGreen) <= 3 &&
                Math.Abs(blue - expectedBlue) <= 3)
            {
                count++;
            }
        }

        return count;
    }
}
