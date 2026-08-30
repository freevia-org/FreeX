using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Host;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Regression coverage for the WPF table-cell text clip finding (freep-text-autofit F1):
/// <c>SlideCompositor.ComposeTable</c> derives a cell's height purely from
/// <see cref="TableRow.HeightEmu"/>, which is never grown to fit typed content -- only an
/// explicit row-resize command changes it. When wrapped text needs more vertical room than the
/// authored row height provides, the WPF renderer must crop the overflow to the cell's own
/// bounds (matching <c>FreeP.App.Rendering.Avalonia.SlideCanvas.RenderTableCellText</c>) instead
/// of letting it bleed into the row below.
/// </summary>
public sealed class R171_TableCellTextOverflowClipTests
{
    private const double EmuPerDip = 9525.0;
    private static readonly SrgbColor MarkerBlue = new(0x00, 0x70, 0xC0);

    private static SlideShape MakeOverflowingTableShape(long row0HeightEmu, long row1HeightEmu)
    {
        const long columnWidthEmu = 1_400_000L; // ~147 DIP -- narrow enough to force wrapping

        var overflowingCell = new TableCell
        {
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs =
                        {
                            new Run
                            {
                                Text = "Overflowing cell text that wraps across many lines and " +
                                       "needs far more vertical room than the authored row height.",
                                FontSizePt = 28.0,
                                Bold = true,
                                Color = new ThemeAwareColor(MarkerBlue)
                            }
                        }
                    }
                }
            }
        };

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(columnWidthEmu);
        table.Rows.Add(new TableRow { HeightEmu = row0HeightEmu, Cells = { overflowingCell } });
        table.Rows.Add(new TableRow { HeightEmu = row1HeightEmu, Cells = { new TableCell() } });

        return new SlideShape
        {
            Id = 1,
            Name = "Table 1",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = columnWidthEmu,
            ExtentCyEmu = row0HeightEmu + row1HeightEmu,
            Table = table
        };
    }

    private static byte[] RenderToPixels(SlideShape tableShape, int width, int height)
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(tableShape);

        var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        var pixels = new byte[width * height * 4];
        rtb.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>True if any pixel in the given DIP-space vertical band [yStart, yEnd) carries the
    /// marker-blue run color (distinguished from grayscale/background by blue clearly exceeding
    /// red, the same heuristic <c>SlideCanvasTests.FindBlueFillBoundingBox</c> uses).</summary>
    private static bool BandContainsMarkerBlue(byte[] pixels, int width, int height, int yStart, int yEnd)
    {
        yStart = Math.Max(0, yStart);
        yEnd = Math.Min(height, yEnd);
        for (int y = yStart; y < yEnd; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                byte b = pixels[o];
                byte r = pixels[o + 2];
                if (b - r > 40)
                    return true;
            }
        }
        return false;
    }

    [StaFact]
    public void RenderTableCellText_OverflowingRow_DoesNotBleedIntoRowBelow()
    {
        // Row 0 is far too short (≈2 DIP) for the wrapped 28pt bold text it holds; row 1 is
        // tall and carries no text of its own, so any marker-blue pixel found in row 1's band
        // can only be overflow that escaped row 0's bounds.
        const long row0HeightEmu = 20_000L;   // ≈2.1 DIP
        const long row1HeightEmu = 1_200_000L; // ≈126 DIP
        var shape = MakeOverflowingTableShape(row0HeightEmu, row1HeightEmu);

        const int width = 200, height = 220;
        var pixels = RenderToPixels(shape, width, height);

        int row0BottomDip = (int)Math.Round(row0HeightEmu / EmuPerDip);
        int row1BottomDip = (int)Math.Round((row0HeightEmu + row1HeightEmu) / EmuPerDip);

        bool bleedIntoRowBelow = BandContainsMarkerBlue(pixels, width, height, row0BottomDip, row1BottomDip);

        bleedIntoRowBelow.Should().BeFalse(
            "row-0's overflowing text must be clipped to its own cell bounds, matching the " +
            "Avalonia renderer's PushGeometryClip in RenderTableCellText, instead of painting " +
            "over row 1's area");
    }

    /// <summary>
    /// Sibling no-regression check: a cell whose row height comfortably fits its text must still
    /// have that text drawn (the clip must bound the cell, not suppress it).
    /// </summary>
    [StaFact]
    public void RenderTableCellText_RowTallEnoughForText_StillRendersText()
    {
        const long row0HeightEmu = 1_200_000L; // ≈126 DIP -- ample room for the wrapped text
        const long row1HeightEmu = 200_000L;
        var shape = MakeOverflowingTableShape(row0HeightEmu, row1HeightEmu);

        const int width = 200, height = 220;
        var pixels = RenderToPixels(shape, width, height);

        int row0BottomDip = (int)Math.Round(row0HeightEmu / EmuPerDip);

        bool textVisibleInOwnCell = BandContainsMarkerBlue(pixels, width, height, 0, row0BottomDip);

        textVisibleInOwnCell.Should().BeTrue(
            "clipping to the cell's own bounds must not suppress text that already fits within " +
            "the row -- only overflow beyond the row's own bounds should ever be cropped");
    }
}
