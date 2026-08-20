using System.Linq;
using Free.Shared.Pdf;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R159 (freep-export-fidelity F1): Group, Table, Chart, and SmartArt shapes used to fall through
/// <see cref="PresentationPdfExporter.BuildSlidePage(Slide)"/>'s generic rect+text branch, which
/// substitutes the literal debug string "[Kind]" (e.g. "[Group]", "[Table]") for any shape with no
/// TextBody -- true of all four of these kinds, since their real content lives in
/// <see cref="SlideShape.Children"/>/<see cref="SlideShape.Table"/>/<see cref="SlideShape.Chart"/>/
/// <see cref="SlideShape.SmartArt"/> rather than TextBody. This is the renderer Notes-Page PDF,
/// Handout PDF, and native print (Notes Pages/Handouts) all share, so a deck with a chart, table,
/// grouped shapes, or SmartArt diagram exported/printed that way showed only outlined boxes
/// containing that debug text instead of the shape's actual content.
/// </summary>
public class PresentationPdfExporterCompositeShapesTests
{
    private static TextBody TextBodyOf(string text)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = text } } });
        return body;
    }

    [Fact]
    public void BuildSlidePage_GroupShape_RendersChildContentInsteadOfDebugPlaceholder()
    {
        var slide = new Slide { Title = "Group slide" };

        var child = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 1_000_000,
            OffsetYEmu = 1_000_000,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 600_000, // tall enough for one 18pt line plus the shape's 8pt text insets
        };
        child.Text = "Nested child text";

        var group = new SlideShape
        {
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 900_000,
            OffsetYEmu = 900_000,
            ExtentCxEmu = 700_000,
            ExtentCyEmu = 500_000,
        };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        var page = PresentationPdfExporter.BuildSlidePage(slide);
        var textOps = page.Ops.OfType<PdfText>().ToList();

        textOps.Select(t => t.Text).Should().NotContain("[Group]");
        var childTextOp = textOps.Should().ContainSingle(t => t.Text == "Nested child text").Which;

        // The child keeps its own absolute EMU position (no ChildOffset/ChildExtent on the group
        // means the identity transform applies), not the group's -- proves this is real recursive
        // per-child geometry, not just some placeholder text appearing anywhere on the page.
        const double shapeTextInsetPt = 8.0;
        childTextOp.X.Should().BeApproximately(child.OffsetXEmu / 12700.0 + shapeTextInsetPt, 0.01);
    }

    [Fact]
    public void BuildSlidePage_TableShape_RendersCellTextInsteadOfDebugPlaceholder()
    {
        var slide = new Slide { Title = "Table slide" };

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914_400L);
        table.ColumnWidthsEmu.Add(914_400L);

        var row0 = new TableRow { HeightEmu = 457_200L };
        row0.Cells.Add(new TableCell { TextBody = TextBodyOf("A1") });
        row0.Cells.Add(new TableCell { TextBody = TextBodyOf("B1") });
        table.Rows.Add(row0);

        var row1 = new TableRow { HeightEmu = 457_200L };
        row1.Cells.Add(new TableCell { TextBody = TextBodyOf("A2") });
        row1.Cells.Add(new TableCell { TextBody = TextBodyOf("B2") });
        table.Rows.Add(row1);

        var tableShape = new SlideShape
        {
            Kind = SlideShapeKind.Table,
            Table = table,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_828_800,
            ExtentCyEmu = 914_400,
        };
        slide.Shapes.Add(tableShape);

        var page = PresentationPdfExporter.BuildSlidePage(slide);
        var texts = page.Ops.OfType<PdfText>().Select(t => t.Text).ToList();

        texts.Should().Contain(new[] { "A1", "B1", "A2", "B2" });
        texts.Should().NotContain("[Table]");
    }

    [Fact]
    public void BuildSlidePage_ColumnChart_RendersDataDrivenBarsInsteadOfDebugPlaceholder()
    {
        var slide = new Slide { Title = "Chart slide" };

        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.Add("Q1");
        chart.Categories.Add("Q2");
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.Add(10);
        series.Values.Add(20);
        chart.Series.Add(series);

        var chartShape = new SlideShape
        {
            Kind = SlideShapeKind.Chart,
            Chart = chart,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 3_657_600, // 4in
            ExtentCyEmu = 2_743_200, // 3in
        };
        slide.Shapes.Add(chartShape);

        var page = PresentationPdfExporter.BuildSlidePage(slide);

        var texts = page.Ops.OfType<PdfText>().Select(t => t.Text).ToList();
        texts.Should().NotContain("[Chart]");
        texts.Should().Contain("Q1");
        texts.Should().Contain("Q2");
        // Two category-groups x one series = two real, value-proportional bars.
        page.Ops.OfType<PdfFillRect>().Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void BuildSlidePage_PieChart_RendersWedgesInsteadOfDebugPlaceholder()
    {
        var slide = new Slide { Title = "Pie slide" };

        var chart = new ChartShape { ChartType = ChartType.Pie };
        chart.Categories.Add("North");
        chart.Categories.Add("South");
        chart.Categories.Add("West");
        var series = new ChartSeries { Name = "Share" };
        series.Values.Add(30);
        series.Values.Add(50);
        series.Values.Add(20);
        chart.Series.Add(series);

        var chartShape = new SlideShape
        {
            Kind = SlideShapeKind.Chart,
            Chart = chart,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 3_657_600,
            ExtentCyEmu = 2_743_200,
        };
        slide.Shapes.Add(chartShape);

        var page = PresentationPdfExporter.BuildSlidePage(slide);

        page.Ops.OfType<PdfText>().Select(t => t.Text).Should().NotContain("[Chart]");
        page.Ops.OfType<PdfPath>().Should().HaveCount(3); // one wedge per non-zero point
    }

    [Fact]
    public void BuildSlidePage_SmartArtFallbackShapes_RendersChildContentInsteadOfDebugPlaceholder()
    {
        var slide = new Slide { Title = "SmartArt slide" };

        var fallbackChild = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 600_000,
            OffsetYEmu = 600_000,
            ExtentCxEmu = 400_000,
            ExtentCyEmu = 600_000, // tall enough for one 18pt line plus the shape's 8pt text insets
        };
        fallbackChild.Text = "SmartArt node";

        var smartArt = new SmartArtShape();
        smartArt.FallbackShapes.Add(fallbackChild);

        var smartArtShape = new SlideShape
        {
            Kind = SlideShapeKind.SmartArt,
            SmartArt = smartArt,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 1_000_000,
            ExtentCyEmu = 800_000,
        };
        slide.Shapes.Add(smartArtShape);

        var page = PresentationPdfExporter.BuildSlidePage(slide);
        var texts = page.Ops.OfType<PdfText>().Select(t => t.Text).ToList();

        texts.Should().Contain("SmartArt node");
        texts.Should().NotContain("[SmartArt]");
    }

    // ── Sibling / no-regression coverage ──────────────────────────────────────────

    [Fact]
    public void BuildSlidePage_TextlessAutoShape_StillUsesBracketPlaceholder()
    {
        // Pre-existing, deliberately-unrelated behavior (not part of this finding): a plain
        // textless AutoShape still renders the "[AutoShape]" debug label exactly as before. Only
        // Group/Table/Chart/SmartArt with real composed content got a new content-drawing path;
        // this proves that path didn't spread to ordinary shapes.
        var slide = new Slide { Title = "Autoshape slide" };
        var shape = new SlideShape
        {
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 500_000,
        };
        slide.Shapes.Add(shape);

        var page = PresentationPdfExporter.BuildSlidePage(slide);

        page.Ops.OfType<PdfText>().Select(t => t.Text).Should().Contain("[AutoShape]");
    }

    [Fact]
    public void BuildSlidePage_EmptyGroupAndEmptyTable_StillFallBackToBracketPlaceholder()
    {
        // A Group with no children or a Table with no rows has no composed content to draw, so
        // both intentionally keep the old generic-box-plus-label fallback (IsCompositeContentLike
        // returns false for them) rather than silently rendering nothing.
        var slide = new Slide { Title = "Empty composite slide" };

        var emptyGroup = new SlideShape
        {
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 500_000,
        };
        slide.Shapes.Add(emptyGroup);

        var emptyTable = new SlideShape
        {
            Kind = SlideShapeKind.Table,
            Table = new TableShape(),
            OffsetXEmu = 1_500_000,
            OffsetYEmu = 500_000,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 500_000,
        };
        slide.Shapes.Add(emptyTable);

        var page = PresentationPdfExporter.BuildSlidePage(slide);
        var texts = page.Ops.OfType<PdfText>().Select(t => t.Text).ToList();

        texts.Should().Contain("[Group]");
        texts.Should().Contain("[Table]");
    }
}
