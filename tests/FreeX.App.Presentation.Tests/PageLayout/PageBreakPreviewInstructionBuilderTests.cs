using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Unit tests for the non-UI glue that turns the portable page-break-preview planner output into the
/// renderer's draw instructions, resolves the print range for a sheet, and re-projects the
/// viewport metrics into display pixel space. No running UI.
/// </summary>
public sealed class PageBreakPreviewInstructionBuilderTests
{
    private static Sheet CreateSheet()
    {
        var workbook = new Workbook("Book");
        return workbook.AddSheet("Sheet1");
    }

    [Fact]
    public void TryResolvePrintRange_PrefersExplicitPrintArea()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 5, 5));

        PageBreakPreviewInstructionBuilder.TryResolvePrintRange(sheet, out var range).Should().BeTrue();
        range.Start.Row.Should().Be(2u);
        range.End.Col.Should().Be(5u);
    }

    [Fact]
    public void TryResolvePrintRange_FallsBackToUsedRange()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(2));

        PageBreakPreviewInstructionBuilder.TryResolvePrintRange(sheet, out var range).Should().BeTrue();
        range.Start.Row.Should().Be(1u);
        range.End.Col.Should().Be(4u);
    }

    [Fact]
    public void TryResolvePrintRange_EmptySheetReturnsFalse()
    {
        var sheet = CreateSheet();

        PageBreakPreviewInstructionBuilder.TryResolvePrintRange(sheet, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolvePrintRanges_ReturnsEveryConfiguredPrintArea()
    {
        // R79-services-pagesetup-print-5-3: a sheet with two non-adjacent print areas (as Excel stores
        // via a comma-separated _xlnm.Print_Area) must expose BOTH ranges to the page-break preview, not
        // only the first (that is what TryResolvePrintRange, the single-range convenience accessor, is
        // for). Otherwise the preview dims the second area as if it were non-printing even though it
        // really prints (see WorkbookExportPrintPlanner.ResolveSheetPrintRanges).
        var sheet = CreateSheet();
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 10, 7));
        sheet.SetPrintAreas([area1, area2]);

        PageBreakPreviewInstructionBuilder.TryResolvePrintRanges(sheet, out var ranges).Should().BeTrue();

        ranges.Should().HaveCount(2);
        ranges[0].Should().Be(area1);
        ranges[1].Should().Be(area2);
    }

    [Fact]
    public void TryResolvePrintRanges_FallsBackToUsedRangeAsSingleRange()
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(2));

        PageBreakPreviewInstructionBuilder.TryResolvePrintRanges(sheet, out var ranges).Should().BeTrue();

        var range = ranges.Should().ContainSingle().Which;
        range.Start.Row.Should().Be(1u);
        range.End.Col.Should().Be(4u);
    }

    [Fact]
    public void TryResolvePrintRanges_EmptySheetReturnsFalse()
    {
        var sheet = CreateSheet();

        PageBreakPreviewInstructionBuilder.TryResolvePrintRanges(sheet, out var ranges).Should().BeFalse();
        ranges.Should().BeEmpty();
    }

    [Fact]
    public void ProjectToDisplaySpace_AppliesZoomAndMinimumsWithCumulativeOffsets()
    {
        var viewport = new ViewportModel(
            Cells: [],
            RowMetrics: [new RowMetric(1, 10, 0), new RowMetric(2, 30, 10)],
            ColMetrics: [new ColMetric(1, 20, 0), new ColMetric(2, 100, 20)]);

        var projected = PageBreakPreviewInstructionBuilder.ProjectToDisplaySpace(
            viewport,
            zoomFactor: 2.0,
            minimumColumnWidth: 50,
            minimumRowHeight: 22);

        // Row 1 height: max(22, 10) * 2 = 44; row 2: max(22, 30) * 2 = 60, offset 44.
        projected.RowMetrics[0].Height.Should().Be(44);
        projected.RowMetrics[0].TopOffset.Should().Be(0);
        projected.RowMetrics[1].Height.Should().Be(60);
        projected.RowMetrics[1].TopOffset.Should().Be(44);

        // Col 1 width: max(50, 20) * 2 = 100; col 2: max(50, 100) * 2 = 200, offset 100.
        projected.ColMetrics[0].Width.Should().Be(100);
        projected.ColMetrics[0].LeftOffset.Should().Be(0);
        projected.ColMetrics[1].Width.Should().Be(200);
        projected.ColMetrics[1].LeftOffset.Should().Be(100);
    }

    [Fact]
    public void Build_FlattensMasksBordersLinesAndWatermarks()
    {
        var pageBounds = new LayoutRect(10, 20, 100, 200);
        var layout = new PageBreakPreviewLayout(
            OutsidePrintAreaMasks: [new LayoutRect(0, 0, 5, 5)],
            Pages:
            [
                new PageBreakPreviewPageLayout(
                    3,
                    pageBounds,
                    new PageBreakPreviewPageEdges(true, false, true, true)),
            ],
            AutomaticBreakLines:
            [
                new PageBreakPreviewBreakLine(new LayoutPoint(1, 2), new LayoutPoint(3, 4)),
            ]);

        var instructions = PageBreakPreviewInstructionBuilder.Build(layout);

        instructions.IsEmpty.Should().BeFalse();
        instructions.Masks.Should().ContainSingle();
        instructions.Masks[0].Width.Should().Be(5);

        instructions.Borders.Should().ContainSingle();
        instructions.Borders[0].Left.Should().Be(10);
        instructions.Borders[0].Width.Should().Be(100);
        instructions.Borders[0].Edges.Bottom.Should().BeFalse();
        instructions.Borders[0].Edges.Top.Should().BeTrue();

        instructions.Lines.Should().ContainSingle();
        instructions.Lines[0].X1.Should().Be(1);
        instructions.Lines[0].Y2.Should().Be(4);

        instructions.Watermarks.Should().ContainSingle();
        instructions.Watermarks[0].Text.Should().Be("Page 3");
        instructions.Watermarks[0].FontSize.Should().Be(
            PageBreakPreviewLayoutPlanner.CalculateWatermarkFontSize(pageBounds));
        instructions.Watermarks[0].Width.Should().Be(100);
    }

    [Fact]
    public void Build_EmptyLayoutProducesEmptyInstructions()
    {
        var layout = new PageBreakPreviewLayout([], [], []);

        var instructions = PageBreakPreviewInstructionBuilder.Build(layout);

        instructions.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Build_FromPlannerOverViewportProducesPagesAndMasks()
    {
        // A small grid wholly on-screen with a print area that doesn't fill it, so we expect masks.
        var rows = new List<RowMetric>();
        for (uint r = 1; r <= 6; r++)
            rows.Add(new RowMetric(r, 20, (r - 1) * 20));
        var cols = new List<ColMetric>();
        for (uint c = 1; c <= 6; c++)
            cols.Add(new ColMetric(c, 60, (c - 1) * 60));

        var viewport = new ViewportModel(Cells: [], RowMetrics: rows, ColMetrics: cols);
        var sheetId = new SheetId(Guid.NewGuid());
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 3, 3));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printArea,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 0,
            columnHeaderHeight: 0,
            actualWidth: 360,
            actualHeight: 120);

        var instructions = PageBreakPreviewInstructionBuilder.Build(layout);

        instructions.Borders.Should().NotBeEmpty();
        instructions.Watermarks.Should().HaveCount(instructions.Borders.Count);
        instructions.Masks.Should().NotBeEmpty();
    }
}
