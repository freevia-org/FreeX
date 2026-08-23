using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PdfOverlayVisualTreeWalkerTests
{
    [Fact]
    public void Extractors_PreserveNestedTraversalOffsetsAndPayloadDistinctions()
    {
        StaTestRunner.Run(() =>
        {
            var page = BuildNestedOverlayPage();

            var textOverlays = PdfTextOverlayExtractor.Extract(page);
            textOverlays.Select(overlay => overlay.Text).Should().Equal(
                "Nested text",
                "Hosted text",
                "Glyph text");
            textOverlays[0].X.Should().Be(29.5);
            textOverlays[0].Y.Should().Be(46);
            textOverlays[1].X.Should().Be(34);
            textOverlays[1].Y.Should().Be(50.75);
            textOverlays[2].X.Should().Be(31.625);
            textOverlays[2].Y.Should().Be(48.25);
            textOverlays[2].FontFamily.Should().Be("Arial");

            var link = PdfLinkOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
            link.Target.Should().Be("https://example.com/nested");
            link.X.Should().Be(34);
            link.Y.Should().Be(50.75);
            link.Width.Should().Be(10.125);
            link.Height.Should().Be(4.625);

            var destination = PdfCellDestinationOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
            destination.Address.Should().Be(new CellAddress(new SheetId(Guid.Empty), 2, 3));
            destination.X.Should().Be(34);
            destination.Y.Should().Be(50.75);
        });
    }

    [Fact]
    public void Extractors_KeepDipGeometryAfterHighDpiRendering()
    {
        StaTestRunner.Run(() =>
        {
            var page = BuildNestedOverlayPage(includeGlyphs: false);
            page.Measure(new Size(240, 160));
            page.Arrange(new Rect(0, 0, 240, 160));
            page.UpdateLayout();

            var textBeforeRender = PdfTextOverlayExtractor.Extract(page);
            var linksBeforeRender = PdfLinkOverlayExtractor.Extract(page);

            var bitmap = new RenderTargetBitmap(480, 320, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(page);

            PdfTextOverlayExtractor.Extract(page).Should().Equal(textBeforeRender);
            PdfLinkOverlayExtractor.Extract(page).Should().Equal(linksBeforeRender);
        });
    }

    private static FixedPage BuildNestedOverlayPage(bool includeGlyphs = true)
    {
        var page = new FixedPage { Width = 240, Height = 160 };
        var rootTransform = new TransformGroup();
        rootTransform.Children.Add(new TranslateTransform(3.25, 4.5));
        rootTransform.Children.Add(new MatrixTransform(new Matrix(1, 0, 0, 1, 5.125, 6.25)));

        var root = new Canvas
        {
            Margin = new Thickness(1.5, 2.5, 0, 0),
            RenderTransform = rootTransform
        };
        Canvas.SetLeft(root, 10.125);
        Canvas.SetTop(root, 20.25);

        var clippedBorder = new Border
        {
            Margin = new Thickness(0.375, 0.5, 0, 0),
            ClipToBounds = true,
            Clip = new RectangleGeometry(new Rect(0, 0, 1, 1)),
            IsHitTestVisible = false,
            RenderTransform = new ScaleTransform(2, 2)
        };
        Canvas.SetLeft(clippedBorder, 7.125);
        Canvas.SetTop(clippedBorder, 8.25);

        var nested = new Grid
        {
            Margin = new Thickness(0.125, 0.25, 0, 0),
            RenderTransform = new TranslateTransform(0.5, 0.75)
        };

        var text = new TextBlock
        {
            Text = "Nested text",
            Margin = new Thickness(0.125, 0.25, 0, 0)
        };
        Canvas.SetLeft(text, 1.25);
        Canvas.SetTop(text, 2.5);
        nested.Children.Add(text);

        var host = new VisualHost
        {
            RenderTransform = new MatrixTransform(new Matrix(1, 0, 0, 1, 0.25, 0.5)),
            TextOverlays =
            [
                new PdfTextOverlay(
                    "Hosted text",
                    X: 0.125,
                    Y: 0.25,
                    FontSize: 11,
                    FontFamily: "Segoe UI",
                    Bold: false,
                    Italic: false,
                    Colors.Navy)
            ],
            LinkOverlays =
            [
                new PdfLinkOverlay(
                    "https://example.com/nested",
                    HyperlinkTargetKind.ExistingFileOrWebPage,
                    X: 0.125,
                    Y: 0.25,
                    Width: 10.125,
                    Height: 4.625)
            ],
            CellDestinationOverlays =
            [
                new PdfCellDestinationOverlay(
                    new CellAddress(new SheetId(Guid.Empty), 2, 3),
                    X: 0.125,
                    Y: 0.25,
                    Width: 10.125,
                    Height: 4.625)
            ]
        };
        Canvas.SetLeft(host, 5.5);
        Canvas.SetTop(host, 6.75);
        nested.Children.Add(host);

        if (includeGlyphs)
        {
            var glyphs = new Glyphs
            {
                UnicodeString = "Glyph text",
                FontRenderingEmSize = 9,
                Fill = Brushes.Maroon,
                Margin = new Thickness(0.375, 0.5, 0, 0)
            };
            Canvas.SetLeft(glyphs, 3.125);
            Canvas.SetTop(glyphs, 4.5);
            nested.Children.Add(glyphs);
        }

        nested.Children.Add(new VisualHost
        {
            Visibility = Visibility.Hidden,
            TextOverlays =
            [
                new PdfTextOverlay("Hidden text", 0, 0, 10, "Arial", false, false, Colors.Black)
            ],
            LinkOverlays =
            [
                new PdfLinkOverlay(
                    "https://example.com/hidden",
                    HyperlinkTargetKind.ExistingFileOrWebPage,
                    0,
                    0,
                    1,
                    1)
            ]
        });

        clippedBorder.Child = nested;
        root.Children.Add(clippedBorder);
        page.Children.Add(root);
        return page;
    }
}
