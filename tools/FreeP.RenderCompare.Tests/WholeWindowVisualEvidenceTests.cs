using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeP.RenderCompare.Tests;

public sealed class WholeWindowVisualEvidenceTests
{
    [Fact]
    public void Pixel_content_gate_rejects_black_transparent_and_uniform_captures()
    {
        var root = Path.Combine(Path.GetTempPath(), "freep-whole-window-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var black = Path.Combine(root, "black.png");
            var transparent = Path.Combine(root, "transparent.png");
            var uniform = Path.Combine(root, "uniform.png");
            WriteSolidPng(black, 128, 76, 0, 0, 0, 255);
            WriteSolidPng(transparent, 128, 76, 0, 0, 0, 0);
            WriteSolidPng(uniform, 128, 76, 210, 210, 210, 255);

            ImageDiff.ValidateContent(black).IsValid.Should().BeFalse();
            ImageDiff.ValidateContent(black).Failures.Should().Contain(reason => reason.Contains("black", StringComparison.Ordinal));
            ImageDiff.ValidateContent(transparent).IsValid.Should().BeFalse();
            ImageDiff.ValidateContent(transparent).Failures.Should().Contain(reason => reason.Contains("transparent", StringComparison.Ordinal));
            ImageDiff.ValidateContent(uniform).IsValid.Should().BeFalse();
            ImageDiff.ValidateContent(uniform).Failures.Should().Contain(reason => reason.Contains("variation", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Pixel_content_gate_accepts_structured_ui_capture()
    {
        var root = Path.Combine(Path.GetTempPath(), "freep-whole-window-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui.png");
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 128, 76));
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 64, 103)), null, new System.Windows.Rect(0, 0, 128, 8));
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(242, 242, 242)), null, new System.Windows.Rect(0, 8, 128, 18));
                context.DrawRectangle(Brushes.LightGray, null, new System.Windows.Rect(0, 26, 24, 46));
                context.DrawRectangle(Brushes.SteelBlue, null, new System.Windows.Rect(30, 34, 72, 28));
                context.DrawLine(new Pen(Brushes.Black, 1), new System.Windows.Point(0, 72), new System.Windows.Point(128, 72));
            }
            var bitmap = new RenderTargetBitmap(128, 76, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            WritePng(path, bitmap);

            var validation = ImageDiff.ValidateContent(path);

            validation.IsValid.Should().BeTrue(string.Join(", ", validation.Failures));
            validation.LuminanceStandardDeviation.Should().BeGreaterThan(3);
            validation.EdgePixelRatio.Should().BeGreaterThan(0.0005);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteSolidPng(string path, int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }
        WritePng(path, BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4));
    }

    private static void WritePng(string path, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
