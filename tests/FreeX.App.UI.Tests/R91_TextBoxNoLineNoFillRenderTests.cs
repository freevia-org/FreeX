using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R91-commands-insert-object-5-1: a text box whose line is explicitly suppressed
/// (<see cref="TextBoxModel.OutlineHasNoFill"/> = true -- exactly what a freshly-inserted text box
/// now gets, see FreeX.Core.Commands.AddTextBoxCommand) must render with no border ink at all.
/// Before this fix, GridView.DrawingObjects.cs's RenderTextBox drew the border pen unconditionally,
/// so a "no line" text box still showed its (explicit or fallback) border color. Uses a distinct,
/// saturated red outline color rather than the default fallback gray so the assertion can't be
/// confused with GridView's own opaque white/gray canvas chrome -- and looks for "reddish" ink
/// (rather than an exact color match) since the 1-DIP border pen is antialiased against the white
/// background and rarely lands at full, unblended strength.
/// </summary>
public sealed class R91_TextBoxNoLineNoFillRenderTests
{
    private static readonly CellColor OutlineProbeColor = new(200, 20, 20);

    // Give the grid canvas slack beyond the text box's own bounds (mirrors
    // GridViewDrawingObjectThemeTests.Rendering's PictureRenderer_DrawsCellRangeSnapshotFormatting)
    // so the object rect sits comfortably inside the viewport's visible-right/visible-bottom test
    // instead of landing exactly on the clipping edge.
    private const int CanvasWidth = 130;
    private const int CanvasHeight = 80;
    private const int ObjectWidth = 90;
    private const int ObjectHeight = 45;

    private static byte[] RenderTextBoxPixels(TextBoxModel textBox)
    {
        byte[] pixels = null!;
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Width = CanvasWidth,
                Height = CanvasHeight,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, ObjectHeight, 0)],
                    [new ColMetric(1, ObjectWidth, 0)]),
                TextBoxes = [textBox]
            };

            grid.Measure(new Size(CanvasWidth, CanvasHeight));
            grid.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            pixels = new byte[CanvasWidth * CanvasHeight * 4];
            bitmap.CopyPixels(pixels, stride: CanvasWidth * 4, offset: 0);
        });
        return pixels;
    }

    /// <summary>True if any pixel in the whole bitmap reads as "reddish" -- red channel clearly
    /// dominant over green and blue -- which only the probe outline color's pen ink (possibly
    /// antialiased against the white background) can produce; GridView's own chrome is always
    /// white, black, or neutral gray (R == G == B), so this can't false-positive on the background
    /// alone.</summary>
    private static bool AnyReddishPixel(byte[] pixels, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                // Pbgra32 byte order: B, G, R, A.
                var b = pixels[offset];
                var g = pixels[offset + 1];
                var r = pixels[offset + 2];

                if (r > 100 && r > g + 40 && r > b + 40)
                    return true;
            }
        }

        return false;
    }

    [Fact]
    public void RenderTextBox_SuppressedLine_DrawsNoProbeColorBorderInk()
    {
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(SheetId.New(), 1, 1),
            Text = "",
            Width = ObjectWidth,
            Height = ObjectHeight,
            HasFill = false,
            OutlineColor = OutlineProbeColor,
            OutlineHasNoFill = true
        };

        var pixels = RenderTextBoxPixels(textBox);

        // Before the fix: RenderTextBox drew the border pen unconditionally, so this authored
        // outline color would still show up even though the line was explicitly suppressed.
        AnyReddishPixel(pixels, CanvasWidth, CanvasHeight).Should().BeFalse(
            "a text box with OutlineHasNoFill=true must draw no border, even when an outline color is set");
    }

    /// <summary>No-regression sibling: a text box with an authored line (OutlineHasNoFill=false,
    /// the safe/back-compat default) must still render a visible border in its outline color, so
    /// the new suppression path doesn't silently swallow real borders too.</summary>
    [Fact]
    public void RenderTextBox_AuthoredLine_StillDrawsBorderInk()
    {
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(SheetId.New(), 1, 1),
            Text = "",
            Width = ObjectWidth,
            Height = ObjectHeight,
            HasFill = false,
            OutlineColor = OutlineProbeColor,
            OutlineHasNoFill = false
        };

        var pixels = RenderTextBoxPixels(textBox);

        AnyReddishPixel(pixels, CanvasWidth, CanvasHeight).Should().BeTrue(
            "a text box that does not suppress its line must still draw a visible border in its outline color");
    }
}
