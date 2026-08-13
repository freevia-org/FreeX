using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Unit tests for the non-UI glue that flattens a portable <see cref="PageContentLayout"/> into the
/// print-preview canvas's ordered paint primitives (rectangles, lines, text runs). The layouts
/// are hand-built so the mapping is verified deterministically without a running UI or font backend.
/// </summary>
public sealed class PrintPreviewInstructionBuilderTests
{
    private static readonly PageTextFont SampleFont =
        new("Segoe UI", 9, Bold: false, Italic: false, new PresentationRgb(10, 20, 30));

    private static PageContentLayout EmptyLayout(int pageNumber = 1) =>
        new(
            pageNumber,
            new LayoutRect(0, 0, 816, 1056),
            new LayoutRect(48, 48, 720, 960),
            new LayoutRect(48, 48, 0, 0),
            Cells: [],
            GridLines: [],
            ColumnHeadings: [],
            RowHeadings: [],
            Charts: [],
            TextBoxes: [],
            HeaderRuns: [],
            FooterRuns: [],
            Pictures: [],
            Comments: []);

    [Fact]
    public void Build_AlwaysPaintsPageBackgroundFirst()
    {
        var painting = PrintPreviewInstructionBuilder.Build(EmptyLayout());

        painting.Instructions.Should().ContainSingle();
        var background = painting.Instructions[0];
        background.Kind.Should().Be(PrintPreviewPaintKind.Rectangle);
        background.Fill.Should().Be(PrintPreviewInstructionBuilder.PageBackground);
        background.Width.Should().Be(816);
        background.Height.Should().Be(1056);
        painting.PageBounds.Width.Should().Be(816);
    }

    [Fact]
    public void Build_PreservesPageNumber()
    {
        PrintPreviewInstructionBuilder.Build(EmptyLayout(pageNumber: 7)).PageNumber.Should().Be(7);
    }

    [Fact]
    public void Build_CellFillBecomesRectangleAndTextUsesPrintRendererAlignment()
    {
        var cellFill = new PresentationRgb(200, 100, 50);
        var cell = new PageCellBlock(
            new LayoutRect(48, 48, 60, 20),
            Row: 1,
            Column: 1,
            cellFill,
            Text: "Hello",
            SampleFont,
            PageTextAlignment.Right,
            PageCellBorders.None,
            new LayoutPoint(50, 53));

        var layout = EmptyLayout() with { Cells = [cell] };

        var painting = PrintPreviewInstructionBuilder.Build(layout);

        // Background, then the fill rectangle, then the text run (no gridlines/borders here).
        painting.Instructions.Should().HaveCount(3);

        var fill = painting.Instructions[1];
        fill.Kind.Should().Be(PrintPreviewPaintKind.Rectangle);
        fill.Fill.Should().Be(cellFill);
        fill.Left.Should().Be(48);
        fill.Width.Should().Be(60);

        var text = painting.Instructions[2];
        text.Kind.Should().Be(PrintPreviewPaintKind.Text);
        text.Text.Should().Be("Hello");
        text.Alignment.Should().Be(PageTextAlignment.Left);
        text.Left.Should().Be(50);
        text.Top.Should().Be(53);
        text.Width.Should().Be(60);
        text.Font.Should().Be(SampleFont);
    }

    [Fact]
    public void Build_CellWithoutFillOrTextEmitsNoCellPrimitives()
    {
        // A cell carrying only borders: no fill rect and no text run, just the border edges.
        var borders = new PageCellBorders(
            new PageBorderEdge(BorderStyle.Thin, new PresentationRgb(0, 0, 0)),
            PageBorderEdge.None,
            PageBorderEdge.None,
            PageBorderEdge.None);
        var cell = new PageCellBlock(
            new LayoutRect(48, 48, 60, 20),
            1, 1,
            Fill: null,
            Text: "",
            SampleFont,
            PageTextAlignment.Left,
            borders,
            new LayoutPoint(50, 53));

        var painting = PrintPreviewInstructionBuilder.Build(EmptyLayout() with { Cells = [cell] });

        painting.Instructions.Should().HaveCount(2); // background + one border edge.
        painting.Instructions.Should().NotContain(i => i.Kind == PrintPreviewPaintKind.Text);
        painting.Instructions[1].Kind.Should().Be(PrintPreviewPaintKind.Line);
    }

    [Fact]
    public void Build_GridLinesBecomeLinePrimitives()
    {
        var layout = EmptyLayout() with
        {
            GridLines =
            [
                new PageGridLine(new LayoutPoint(48, 48), new LayoutPoint(48, 88)),
                new PageGridLine(new LayoutPoint(48, 48), new LayoutPoint(108, 48)),
            ],
        };

        var painting = PrintPreviewInstructionBuilder.Build(layout);

        var lines = painting.Instructions.Where(i => i.Kind == PrintPreviewPaintKind.Line).ToList();
        lines.Should().HaveCount(2);
        lines.Should().OnlyContain(l => l.Stroke == PrintPreviewInstructionBuilder.GridLineColor);
        lines[0].X1.Should().Be(48);
        lines[0].Y2.Should().Be(88);
    }

    [Fact]
    public void Build_EmitsOneLinePerVisibleCellBorderEdge()
    {
        var color = new PresentationRgb(1, 2, 3);
        var borders = new PageCellBorders(
            new PageBorderEdge(BorderStyle.Thin, color),
            new PageBorderEdge(BorderStyle.Medium, color),
            new PageBorderEdge(BorderStyle.Thick, color),
            PageBorderEdge.None); // left absent
        var cell = new PageCellBlock(
            new LayoutRect(10, 20, 40, 30),
            1, 1, Fill: null, Text: "",
            SampleFont, PageTextAlignment.Left, borders, new LayoutPoint(12, 25));

        var painting = PrintPreviewInstructionBuilder.Build(EmptyLayout() with { Cells = [cell] });

        var lines = painting.Instructions.Where(i => i.Kind == PrintPreviewPaintKind.Line).ToList();
        lines.Should().HaveCount(3); // top, right, bottom (left absent).

        // Thicknesses follow the style mapping.
        lines.Select(l => l.StrokeThickness).Should().BeEquivalentTo(new[] { 1.0, 2.0, 3.0 });

        // Top edge spans the top of the cell rect.
        var top = lines[0];
        top.Y1.Should().Be(20);
        top.Y2.Should().Be(20);
        top.X1.Should().Be(10);
        top.X2.Should().Be(50);
    }

    [Fact]
    public void Build_HeadingsEmitFillRectThenCenteredTextRun()
    {
        var heading = new PageHeadingCell(new LayoutRect(48, 20, 60, 28), "A", new LayoutPoint(50, 27));
        var layout = EmptyLayout() with { ColumnHeadings = [heading] };

        var painting = PrintPreviewInstructionBuilder.Build(layout);

        var fills = painting.Instructions
            .Where(i => i.Kind == PrintPreviewPaintKind.Rectangle && i.Fill == PrintPreviewInstructionBuilder.HeadingFill)
            .ToList();
        fills.Should().ContainSingle();

        var text = painting.Instructions.Single(i => i.Kind == PrintPreviewPaintKind.Text);
        text.Text.Should().Be("A");
        text.Alignment.Should().Be(PageTextAlignment.Center);
    }

    [Fact]
    public void Build_HeaderAndFooterRunsBecomeTextRunsWithBandAlignment()
    {
        var layout = EmptyLayout() with
        {
            HeaderRuns =
            [
                new PageHeaderFooterRun(new LayoutRect(48, 4, 240, 16), "Title", [], PageTextAlignment.Center, new LayoutPoint(50, 6)),
            ],
            FooterRuns =
            [
                new PageHeaderFooterRun(new LayoutRect(48, 1000, 240, 16), "Page 1", [], PageTextAlignment.Right, new LayoutPoint(50, 1002)),
            ],
        };

        var painting = PrintPreviewInstructionBuilder.Build(layout);

        var texts = painting.Instructions.Where(i => i.Kind == PrintPreviewPaintKind.Text).ToList();
        texts.Should().HaveCount(2);
        texts.Should().Contain(t => t.Text == "Title" && t.Alignment == PageTextAlignment.Center);
        texts.Should().Contain(t => t.Text == "Page 1" && t.Alignment == PageTextAlignment.Right);
    }

    [Fact]
    public void Build_OrderIsFillsThenLinesThenText()
    {
        var cell = new PageCellBlock(
            new LayoutRect(48, 48, 60, 20),
            1, 1,
            new PresentationRgb(10, 10, 10),
            "X",
            SampleFont,
            PageTextAlignment.Left,
            new PageCellBorders(
                new PageBorderEdge(BorderStyle.Thin, new PresentationRgb(0, 0, 0)),
                PageBorderEdge.None, PageBorderEdge.None, PageBorderEdge.None),
            new LayoutPoint(50, 53));
        var layout = EmptyLayout() with
        {
            Cells = [cell],
            GridLines = [new PageGridLine(new LayoutPoint(48, 48), new LayoutPoint(108, 48))],
        };

        var kinds = PrintPreviewInstructionBuilder.Build(layout).Instructions
            .Select(i => i.Kind)
            .ToList();

        // background(rect) -> cell fill(rect) -> gridline(line) -> border(line) -> cell text(text)
        var lastRect = kinds.LastIndexOf(PrintPreviewPaintKind.Rectangle);
        var firstLine = kinds.IndexOf(PrintPreviewPaintKind.Line);
        var firstText = kinds.IndexOf(PrintPreviewPaintKind.Text);
        lastRect.Should().BeLessThan(firstLine);
        firstLine.Should().BeLessThan(firstText);
    }

    [Fact]
    public void Build_TextBoxesEmitRectangleAndTextAfterCellTextBeforeHeaderFooter()
    {
        var cell = new PageCellBlock(
            new LayoutRect(48, 48, 60, 20),
            1, 1,
            Fill: null,
            Text: "Cell",
            SampleFont,
            PageTextAlignment.Left,
            PageCellBorders.None,
            new LayoutPoint(50, 53));
        var textBoxFont = new PageTextFont("Segoe UI", 9, Bold: false, Italic: false, new PresentationRgb(0, 0, 0));
        var textBox = new PageTextBoxBlock(
            Guid.NewGuid(),
            new LayoutRect(60, 70, 96, 42),
            new LayoutRect(64, 74, 88, 34),
            "Box",
            Fill: new PresentationRgb(200, 220, 240),
            FillAlpha: 242,
            Outline: new PresentationRgb(20, 70, 120),
            OutlineThickness: 1,
            textBoxFont);
        var header = new PageHeaderFooterRun(
            new LayoutRect(48, 4, 240, 16),
            "Header",
            [],
            PageTextAlignment.Center,
            new LayoutPoint(50, 6));
        var layout = EmptyLayout() with
        {
            Cells = [cell],
            TextBoxes = [textBox],
            HeaderRuns = [header],
        };

        var painting = PrintPreviewInstructionBuilder.Build(layout);
        var texts = painting.Instructions.Where(i => i.Kind == PrintPreviewPaintKind.Text).ToList();

        texts.Select(i => i.Text).Should().ContainInOrder("Cell", "Box", "Header");
        var boxRect = painting.Instructions.Single(i =>
            i.Kind == PrintPreviewPaintKind.Rectangle &&
            i.Left == 60 &&
            i.Top == 70);
        boxRect.Fill.Should().Be(new PresentationRgb(200, 220, 240));
        boxRect.Stroke.Should().Be(new PresentationRgb(20, 70, 120));
        boxRect.StrokeThickness.Should().Be(1);
        var boxText = texts.Single(i => i.Text == "Box");
        boxText.Left.Should().Be(64);
        boxText.Top.Should().Be(74);
        boxText.Width.Should().Be(88);
        boxText.Font.Should().Be(textBoxFont);
    }

    [Fact]
    public void Build_ChartsEmitRectangleAndOverlayTextBeforeTextBoxes()
    {
        var chart = new PageChartBlock(
            Guid.NewGuid(),
            new LayoutRect(70, 80, 220, 140),
            new PresentationRgb(250, 252, 255),
            new PresentationRgb(40, 50, 60),
            OutlineThickness: 1,
            TextOverlays:
            [
                new PrintChartTextOverlayPlan(
                    "Chart title",
                    90,
                    92,
                    16,
                    new PresentationRgb(20, 30, 40),
                    RotationDegrees: 0),
            ]);
        var textBox = new PageTextBoxBlock(
            Guid.NewGuid(),
            new LayoutRect(100, 120, 96, 42),
            new LayoutRect(104, 124, 88, 34),
            "Box",
            Fill: new PresentationRgb(200, 220, 240),
            FillAlpha: 242,
            Outline: new PresentationRgb(20, 70, 120),
            OutlineThickness: 1,
            SampleFont);

        var painting = PrintPreviewInstructionBuilder.Build(EmptyLayout() with
        {
            Charts = [chart],
            TextBoxes = [textBox],
        });

        var chartRect = painting.Instructions.Single(i =>
            i.Kind == PrintPreviewPaintKind.Rectangle &&
            i.Left == 70 &&
            i.Top == 80);
        chartRect.Fill.Should().Be(new PresentationRgb(250, 252, 255));
        chartRect.Stroke.Should().Be(new PresentationRgb(40, 50, 60));

        var texts = painting.Instructions.Where(i => i.Kind == PrintPreviewPaintKind.Text).ToList();
        texts.Select(i => i.Text).Should().ContainInOrder("Chart title", "Box");
        texts.Single(i => i.Text == "Chart title").Font.FontFamily.Should().Be(PrintChartTextOverlayPlanner.FontFamily);
    }

    [Fact]
    public void BorderThickness_MapsStyles()
    {
        PrintPreviewInstructionBuilder.BorderThickness(BorderStyle.Thin).Should().Be(1);
        PrintPreviewInstructionBuilder.BorderThickness(BorderStyle.Medium).Should().Be(2);
        PrintPreviewInstructionBuilder.BorderThickness(BorderStyle.Thick).Should().Be(3);
        PrintPreviewInstructionBuilder.BorderThickness(BorderStyle.Double).Should().Be(2);
        PrintPreviewInstructionBuilder.BorderThickness(BorderStyle.Dashed).Should().Be(1);
        PrintPreviewInstructionBuilder.BorderThickness(BorderStyle.None).Should().Be(0);
    }
}
