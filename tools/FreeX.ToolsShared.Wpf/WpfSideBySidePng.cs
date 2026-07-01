using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeX.ToolsShared.Wpf;

public sealed record WpfSideBySidePngOptions(
    int ThumbnailWidth,
    int ThumbnailHeight,
    int Padding,
    int LabelHeight,
    string HeaderText,
    string LeftLabel,
    string RightLabel,
    string? FooterText = null);

public sealed record WpfHeaderSideBySidePngOptions(
    int ThumbnailWidth,
    int ThumbnailHeight,
    int Padding,
    int HeaderHeight,
    string HeaderText);

public static class WpfSideBySidePng
{
    public static void Write(
        string leftImagePath,
        string? rightImagePath,
        string outputPath,
        WpfSideBySidePngOptions options)
    {
        var totalWidth = options.ThumbnailWidth * 2 + options.Padding * 3;
        var totalHeight = options.ThumbnailHeight + options.Padding * 2 + options.LabelHeight * 2;

        var leftBitmap = File.Exists(leftImagePath)
            ? WpfImageDiff.ResizeTo(WpfImageDiff.LoadBitmap(leftImagePath), options.ThumbnailWidth, options.ThumbnailHeight)
            : WpfImageDiff.CreateWhite(options.ThumbnailWidth, options.ThumbnailHeight);
        var rightBitmap = rightImagePath is not null && File.Exists(rightImagePath)
            ? WpfImageDiff.ResizeTo(WpfImageDiff.LoadBitmap(rightImagePath), options.ThumbnailWidth, options.ThumbnailHeight)
            : WpfImageDiff.CreateWhite(options.ThumbnailWidth, options.ThumbnailHeight);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                null,
                new Rect(0, 0, totalWidth, totalHeight));

            context.DrawText(
                CreateText(options.HeaderText, 13, Brushes.Black, FontWeights.SemiBold),
                new Point(options.Padding, 4));

            var imageY = options.LabelHeight;
            var leftX = options.Padding;
            var rightX = options.Padding * 2 + options.ThumbnailWidth;

            context.DrawText(
                CreateText(options.LeftLabel, 11, Brushes.DarkSlateGray, FontWeights.Normal),
                new Point(leftX, imageY + options.ThumbnailHeight + 4));
            context.DrawText(
                CreateText(options.RightLabel, 11, Brushes.DarkSlateGray, FontWeights.Normal),
                new Point(rightX, imageY + options.ThumbnailHeight + 4));

            if (options.FooterText is not null)
            {
                context.DrawText(
                    CreateText(options.FooterText, 10, Brushes.DarkSlateGray, FontWeights.Normal),
                    new Point(options.Padding, imageY + options.ThumbnailHeight + options.LabelHeight + 4));
            }

            context.DrawImage(leftBitmap, new Rect(leftX, imageY, options.ThumbnailWidth, options.ThumbnailHeight));
            context.DrawImage(rightBitmap, new Rect(rightX, imageY, options.ThumbnailWidth, options.ThumbnailHeight));
        }

        var renderTarget = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    public static void WriteHeaderOnly(
        string leftImagePath,
        string rightImagePath,
        string outputPath,
        WpfHeaderSideBySidePngOptions options)
    {
        var totalWidth = options.ThumbnailWidth * 2 + options.Padding * 3;
        var totalHeight = options.ThumbnailHeight + options.Padding * 2 + options.HeaderHeight;

        var leftBitmap = WpfImageDiff.ResizeTo(
            WpfImageDiff.LoadBitmap(leftImagePath),
            options.ThumbnailWidth,
            options.ThumbnailHeight);
        var rightBitmap = WpfImageDiff.ResizeTo(
            WpfImageDiff.LoadBitmap(rightImagePath),
            options.ThumbnailWidth,
            options.ThumbnailHeight);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                null,
                new Rect(0, 0, totalWidth, totalHeight));

            context.DrawText(
                CreateText(options.HeaderText, 13, Brushes.Black, FontWeights.Normal),
                new Point(options.Padding, 4));
            context.DrawImage(
                leftBitmap,
                new Rect(options.Padding, options.HeaderHeight, options.ThumbnailWidth, options.ThumbnailHeight));
            context.DrawImage(
                rightBitmap,
                new Rect(
                    options.Padding * 2 + options.ThumbnailWidth,
                    options.HeaderHeight,
                    options.ThumbnailWidth,
                    options.ThumbnailHeight));
        }

        var renderTarget = new RenderTargetBitmap(totalWidth, totalHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static FormattedText CreateText(string text, double size, Brush brush, FontWeight weight) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);
}
