using System.Linq;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r198 (backlog item 67): <c>PresentationPdfExporter.AppendShapeText</c> always drew a table cell's
/// text from the top of the cell, because it had no way to see the cell's vertical anchor. The screen
/// renderer and the Full Page Slides export both honour it, so a Middle- or Bottom-anchored cell came
/// out in a different place on the notes-page and handout PDFs (and on native print, which shares this
/// renderer) than it appears on the slide.
/// </summary>
public class R198_PdfTableCellAnchorTests
{
    private static TextBody TextBodyOf(string text)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = text } } });
        return body;
    }

    /// <summary>A one-row, one-column table whose single cell is deliberately much taller than its text.</summary>
    private static Slide TableWith(TableCellAnchor anchor)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(1_828_800L);
        var row = new TableRow { HeightEmu = 1_828_800L };
        row.Cells.Add(new TableCell { TextBody = TextBodyOf("Anchored"), Anchor = anchor });
        table.Rows.Add(row);

        var slide = new Slide { Title = "Anchor slide" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Table,
            Table = table,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 1_828_800,
        });
        return slide;
    }

    private static double TextBaselineY(TableCellAnchor anchor) =>
        PresentationPdfExporter.BuildSlidePage(TableWith(anchor))
            .Ops.OfType<PdfText>()
            .Single(op => op.Text == "Anchored")
            .Y;

    [Fact]
    public void BuildSlidePage_MiddleAnchoredCell_DrawsTextBelowATopAnchoredOne()
    {
        // PDF space is y-up, so lower on the page means a smaller Y.
        TextBaselineY(TableCellAnchor.Middle).Should().BeLessThan(TextBaselineY(TableCellAnchor.Top));
    }

    [Fact]
    public void BuildSlidePage_BottomAnchoredCell_DrawsTextBelowAMiddleAnchoredOne()
    {
        TextBaselineY(TableCellAnchor.Bottom).Should().BeLessThan(TextBaselineY(TableCellAnchor.Middle));
    }

    [Fact]
    public void BuildSlidePage_MiddleAnchoredCell_SitsHalfwayBetweenTopAndBottom()
    {
        var top = TextBaselineY(TableCellAnchor.Top);
        var middle = TextBaselineY(TableCellAnchor.Middle);
        var bottom = TextBaselineY(TableCellAnchor.Bottom);

        middle.Should().BeApproximately((top + bottom) / 2, 0.01);
    }

    [Fact]
    public void BuildSlidePage_TopAnchoredCell_IsUnchanged()
    {
        // The control: Top is the historical behaviour and every non-table caller still gets it.
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(1_828_800L);
        var row = new TableRow { HeightEmu = 1_828_800L };
        row.Cells.Add(new TableCell { TextBody = TextBodyOf("Anchored") }); // default anchor
        table.Rows.Add(row);

        var slide = new Slide { Title = "Anchor slide" };
        slide.Shapes.Add(new SlideShape
        {
            Kind = SlideShapeKind.Table,
            Table = table,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 1_828_800,
        });

        PresentationPdfExporter.BuildSlidePage(slide)
            .Ops.OfType<PdfText>()
            .Single(op => op.Text == "Anchored")
            .Y.Should().Be(TextBaselineY(TableCellAnchor.Top));
    }
}
