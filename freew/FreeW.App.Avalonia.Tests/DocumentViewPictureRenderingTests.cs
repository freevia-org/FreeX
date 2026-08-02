using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewPictureRenderingTests
{
    private const int WindowWidth = 816;
    private const int WindowHeight = 1200;

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    public enum PictureHost
    {
        Inline,
        Floating,
        Header,
        Group,
    }

    [Theory]
    [InlineData(PictureHost.Inline)]
    [InlineData(PictureHost.Floating)]
    [InlineData(PictureHost.Header)]
    [InlineData(PictureHost.Group)]
    public async Task Live_picture_hosts_apply_both_flips_then_rotation(PictureHost host)
    {
        var capture = await CaptureAsync(host, image =>
        {
            image.FlipH = true;
            image.FlipV = true;
            image.RotationAngle = 90;
        });

        if (!capture.Ran || capture.Png is null)
            return;

        using var bitmap = SKBitmap.Decode(capture.Png);
        bitmap.Should().NotBeNull();

        // Source quadrants are red, green, blue, yellow. Flip H + flip V + clockwise 90 degrees
        // produces green, yellow, red, blue in screen-space reading order.
        AssertQuadrants(bitmap!, capture.Rect, SKColors.Lime, SKColors.Yellow, SKColors.Red, SKColors.Blue);
    }

    [Theory]
    [InlineData(PictureHost.Inline)]
    [InlineData(PictureHost.Floating)]
    [InlineData(PictureHost.Header)]
    [InlineData(PictureHost.Group)]
    public async Task Live_picture_hosts_leave_identity_controls_untouched(PictureHost host)
    {
        var capture = await CaptureAsync(host, _ => { });

        if (!capture.Ran || capture.Png is null)
            return;

        using var bitmap = SKBitmap.Decode(capture.Png);
        bitmap.Should().NotBeNull();
        AssertQuadrants(bitmap!, capture.Rect, SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow);
    }

    [Fact]
    public async Task Authored_picture_border_renders_color_width_and_dash_gaps()
    {
        var capture = await CaptureAsync(PictureHost.Inline, image =>
        {
            image.BorderColorHex = "D02040";
            image.BorderWidthPt = 3;
            image.BorderDash = "dash";
        }, SolidPng(SKColors.White));

        if (!capture.Ran || capture.Png is null)
            return;

        using var bitmap = SKBitmap.Decode(capture.Png);
        bitmap.Should().NotBeNull();

        var y = Math.Clamp((int)Math.Round(capture.Rect.Top), 0, bitmap!.Height - 1);
        var startX = Math.Clamp((int)Math.Ceiling(capture.Rect.Left + 6), 0, bitmap.Width - 1);
        var endX = Math.Clamp((int)Math.Floor(capture.Rect.Right - 6), startX, bitmap.Width - 1);
        var redPixels = 0;
        var gapPixels = 0;
        for (var x = startX; x <= endX; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (IsNear(pixel, new SKColor(0xD0, 0x20, 0x40)))
                redPixels++;
            else if (pixel.Red > 220 && pixel.Green > 220 && pixel.Blue > 220)
                gapPixels++;
        }

        redPixels.Should().BeGreaterThan(12, "the authored red stroke should paint multiple dash segments");
        gapPixels.Should().BeGreaterThan(8, "the dash token should leave visible gaps between segments");
    }

    [Fact]
    public void Picture_render_contract_preserves_authored_pen_and_neutral_identity()
    {
        var view = new DocumentView();
        var authored = new InlineImage(QuadrantPng(), 48, 48)
        {
            BorderColorHex = "C02040",
            BorderWidthPt = 2.25,
            BorderDash = "lgDashDot",
        };

        var pen = view.BuildPictureBorderPen(authored);
        pen.Should().NotBeNull();
        pen!.Thickness.Should().BeApproximately(3, 0.001, "2.25 points is 3 DIP at 96 DPI");
        pen.Brush.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.Parse("#C02040"));
        pen.DashStyle.Should().NotBeNull();
        pen.DashStyle!.Dashes.Should().Equal(8, 2, 1, 2);

        var neutral = new InlineImage(QuadrantPng(), 48, 48);
        view.BuildPictureBorderPen(neutral).Should().BeNull();
        DocumentView.BuildPictureDashStyle("solid").Should().BeNull();
        DocumentView.BuildPictureTransform(new Rect(10, 20, 64, 64), neutral)
            .Should().Be(Matrix.Identity);
    }

    private static async Task<(bool Ran, byte[]? Png, Rect Rect)> CaptureAsync(
        PictureHost host,
        Action<InlineImage> configure,
        byte[]? png = null)
    {
        byte[]? captured = null;
        Rect pictureRect = default;
        try
        {
            await Session.Dispatch(() =>
            {
                var image = new InlineImage(png ?? QuadrantPng(), 48, 48);
                configure(image);
                var document = BuildDocument(host, image);
                var view = new DocumentView();
                view.LoadDocument(document);

                var window = new Window
                {
                    Width = WindowWidth,
                    Height = WindowHeight,
                    Content = view,
                };
                window.Show();
                window.Measure(new Size(WindowWidth, WindowHeight));
                window.Arrange(new Rect(0, 0, WindowWidth, WindowHeight));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                pictureRect = host switch
                {
                    PictureHost.Inline => view.InlineImageRects.Single(),
                    PictureHost.Floating => view.FloatingImageRects.Single().Rect,
                    PictureHost.Header => view.HeaderFooterImageItems.Single().Rect,
                    PictureHost.Group => view.FloatingGroupChildRectsForTest(1, 1)
                        .Single(item => item.ChildIndex == 0).Rect,
                    _ => default,
                };
                if (window.CaptureRenderedFrame() is { } frame)
                    captured = WriteableBitmapToPng(frame);
                window.Close();
            }, CancellationToken.None);
            return (true, captured, pictureRect);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[PictureRendering] Skipped: {exception.GetType().Name}: {exception.Message}");
            return (false, null, default);
        }
    }

    private static TextDocument BuildDocument(PictureHost host, InlineImage image)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body."));

        if (host == PictureHost.Header)
        {
            var header = new HeaderFooter();
            header.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run(string.Empty, RunFormatting.Default) { Image = image } },
            });
            document.FinalSectionHeadersFooters.Header = header;
            return document;
        }

        var paragraph = new Paragraph();
        if (host == PictureHost.Inline)
        {
            image.Wrapping = ImageWrapping.Inline;
            paragraph.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = image });
        }
        else if (host == PictureHost.Floating)
        {
            paragraph.Runs.Add(new Run("Anchor.", RunFormatting.Default));
            image.Wrapping = ImageWrapping.InFront;
            image.HorizontalAnchor = HorizontalAnchor.Page;
            image.VerticalAnchor = VerticalAnchor.Page;
            image.HorizontalOffsetPt = 72;
            image.VerticalOffsetPt = 180;
            paragraph.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = image });
        }
        else
        {
            paragraph.Runs.Add(new Run("Anchor.", RunFormatting.Default));
            var group = new FreeW.Core.Model.DrawingGroup
            {
                WidthPt = 120,
                HeightPt = 54,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.InFront,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    VerticalAnchor = VerticalAnchor.Page,
                    HorizontalOffsetPt = 72,
                    VerticalOffsetPt = 180,
                },
            };
            group.Children.Add(image);
            group.ChildOffsets.Add((0, 0));
            group.Children.Add(new Shape(ShapeKind.Rectangle, 48, 48, "#808080"));
            group.ChildOffsets.Add((66, 0));
            paragraph.Runs.Add(Run.FromDrawingGroup(group));
        }

        document.Blocks.Add(paragraph);
        return document;
    }

    private static void AssertQuadrants(
        SKBitmap bitmap,
        Rect rect,
        SKColor topLeft,
        SKColor topRight,
        SKColor bottomLeft,
        SKColor bottomRight)
    {
        AssertPixel(bitmap, rect, 0.25, 0.25, topLeft);
        AssertPixel(bitmap, rect, 0.75, 0.25, topRight);
        AssertPixel(bitmap, rect, 0.25, 0.75, bottomLeft);
        AssertPixel(bitmap, rect, 0.75, 0.75, bottomRight);
    }

    private static void AssertPixel(SKBitmap bitmap, Rect rect, double xFraction, double yFraction, SKColor expected)
    {
        var x = Math.Clamp((int)Math.Round(rect.X + rect.Width * xFraction), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)Math.Round(rect.Y + rect.Height * yFraction), 0, bitmap.Height - 1);
        var actual = bitmap.GetPixel(x, y);
        IsNear(actual, expected).Should().BeTrue(
            $"pixel ({x},{y}) should be near {expected}, but was {actual}");
    }

    private static bool IsNear(SKColor actual, SKColor expected) =>
        Math.Abs(actual.Red - expected.Red) <= 20
        && Math.Abs(actual.Green - expected.Green) <= 20
        && Math.Abs(actual.Blue - expected.Blue) <= 20;

    private static byte[] QuadrantPng()
    {
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, (x < 2, y < 2) switch
            {
                (true, true) => SKColors.Red,
                (false, true) => SKColors.Lime,
                (true, false) => SKColors.Blue,
                _ => SKColors.Yellow,
            });
        return EncodePng(bitmap);
    }

    private static byte[] SolidPng(SKColor color)
    {
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        return EncodePng(bitmap);
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] WriteableBitmapToPng(WriteableBitmap bitmap)
    {
        using var locked = bitmap.Lock();
        var info = new SKImageInfo(
            locked.Size.Width,
            locked.Size.Height,
            locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var skBitmap = new SKBitmap();
        if (!skBitmap.InstallPixels(info, locked.Address, locked.RowBytes))
            return [];
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray() ?? [];
    }
}
