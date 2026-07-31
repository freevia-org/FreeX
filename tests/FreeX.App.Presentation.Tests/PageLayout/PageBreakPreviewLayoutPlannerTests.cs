using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageBreakPreviewLayoutPlannerTests
{
    [Fact]
    public void Calculate_BuildsPagesAndAutomaticBreakLinesForPreviewRange()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 70).Select(row => new RowMetric((uint)row, (row - 1) * 20, 20)).ToList(),
            Enumerable.Range(1, 20).Select(col => new ColMetric((uint)col, (col - 1) * 40, 40)).ToList(),
            null,
            []);
        var previewRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 70, 20));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            previewRange,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 900,
            actualHeight: 1500);

        layout.Pages.Should().HaveCount(4);
        layout.Pages.Select(page => page.PageNumber).Should().Equal(1, 3, 2, 4);
        layout.Pages.Should().OnlyContain(page => page.Bounds.Width > 0 && page.Bounds.Height > 0);
        layout.OutsidePrintAreaMasks.Should().NotBeEmpty();
    }

    [Fact]
    public void Calculate_MarksOffscreenPageLayoutTopAndBottomEdgesNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(10, 11).Select(row => new RowMetric((uint)row, (row - 10) * 20, 20)).ToList(),
            Enumerable.Range(1, 5).Select(col => new ColMetric((uint)col, (col - 1) * 40, 40)).ToList(),
            null,
            []);
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 40, 5));

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
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 230,
            actualHeight: 238);

        var page = layout.Pages.Should().ContainSingle().Which;
        page.Bounds.Top.Should().Be(18);
        page.Bounds.Bottom.Should().Be(238);
        page.VisibleEdges.Top.Should().BeFalse();
        page.VisibleEdges.Bottom.Should().BeFalse();
        page.VisibleEdges.Left.Should().BeTrue();
        page.VisibleEdges.Right.Should().BeTrue();
    }

    [Fact]
    public void Calculate_MarksPageLayoutBoundaryEdgesOnlyWhenDocumentRowsAreVisible()
    {
        var sheetId = SheetId.New();
        var printArea = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 40, 5));

        var topViewport = CreatePageLayoutViewport(firstRow: 1, rowCount: 12);
        var topPage = CalculateSinglePage(topViewport, printArea);
        topPage.VisibleEdges.Top.Should().BeTrue();
        topPage.VisibleEdges.Bottom.Should().BeFalse();

        var bottomViewport = CreatePageLayoutViewport(firstRow: 30, rowCount: 11);
        var bottomPage = CalculateSinglePage(bottomViewport, printArea);
        bottomPage.VisibleEdges.Top.Should().BeFalse();
        bottomPage.VisibleEdges.Bottom.Should().BeTrue();
    }

    [Fact]
    public void Calculate_ReturnsEmptyLayoutWhenPrintAreaIsNull()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 40, 0)],
            null,
            []);

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printArea: null,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 900,
            actualHeight: 1500);

        _ = sheetId;
        layout.Pages.Should().BeEmpty();
        layout.OutsidePrintAreaMasks.Should().BeEmpty();
        layout.AutomaticBreakLines.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_PlacesHorizontalBreakLinesBetweenRowPages()
    {
        var sheetId = SheetId.New();
        // 70 rows, A4 narrow margins -> automatic horizontal break(s) inside the visible region.
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 70).Select(row => new RowMetric((uint)row, 20, (row - 1) * 20.0)).ToList(),
            Enumerable.Range(1, 5).Select(col => new ColMetric((uint)col, 40, (col - 1) * 40.0)).ToList(),
            null,
            []);
        var previewRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 70, 5));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            previewRange,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 400,
            actualHeight: 2000);

        layout.AutomaticBreakLines.Should().NotBeEmpty();
        layout.AutomaticBreakLines.Should().OnlyContain(line =>
            Math.Abs(line.Start.Y - line.End.Y) < 0.0001 && line.End.X > line.Start.X);
    }

    [Fact]
    public void Calculate_MultiArea_DoesNotMaskSecondPrintArea()
    {
        // R79-services-pagesetup-print-5-3: two non-adjacent print areas (A1:C5 and E1:G5, Excel's
        // comma-separated _xlnm.Print_Area) must both render as live, printable regions. Only column D
        // (the true gap between them) should be dimmed - area 2 (columns 5-7, x in [190, 310)) must never
        // be covered by an "outside print area" mask, even though the single-GridRange overload (fed only
        // the first area) would have dimmed it entirely.
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 5).Select(row => new RowMetric((uint)row, 20, (row - 1) * 20.0)).ToList(),
            Enumerable.Range(1, 7).Select(col => new ColMetric((uint)col, 40, (col - 1) * 40.0)).ToList(),
            null,
            []);
        var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 3));
        var area2 = new GridRange(new CellAddress(sheetId, 1, 5), new CellAddress(sheetId, 5, 7));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printAreas: [area1, area2],
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 310,
            actualHeight: 118);

        // Column D (x in [150, 190)) is the only region outside both print areas - no mask may extend
        // into area 2's band (x >= 190).
        layout.OutsidePrintAreaMasks.Should().NotBeEmpty();
        layout.OutsidePrintAreaMasks.Should().OnlyContain(mask => mask.Right <= 190.0001);

        // Both areas produced visible pages, with numbers continuing across areas (area 1 first).
        layout.Pages.Should().HaveCountGreaterThan(1);
        layout.Pages.Select(page => page.PageNumber).Should().BeInAscendingOrder();
        layout.Pages.Select(page => page.PageNumber).Should().OnlyHaveUniqueItems();
        layout.Pages.First().PageNumber.Should().Be(1);
    }

    [Fact]
    public void Calculate_MultiArea_SingleElementListMatchesSingleAreaOverload()
    {
        // No-regression sibling: the multi-area overload's single-area fast path must produce identical
        // geometry to the original single-GridRange Calculate (same call as
        // Calculate_BuildsPagesAndAutomaticBreakLinesForPreviewRange above).
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 70).Select(row => new RowMetric((uint)row, (row - 1) * 20, 20)).ToList(),
            Enumerable.Range(1, 20).Select(col => new ColMetric((uint)col, (col - 1) * 40, 40)).ToList(),
            null,
            []);
        var previewRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 70, 20));

        var singleAreaLayout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            previewRange,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 900,
            actualHeight: 1500);

        var multiAreaLayout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printAreas: [previewRange],
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 900,
            actualHeight: 1500);

        multiAreaLayout.Should().BeEquivalentTo(singleAreaLayout);
    }

    [Fact]
    public void R106_Calculate_MultiArea_OffscreenEarlierAreaStillContributesToPageNumberOffset()
    {
        // R106: area 1 (rows 1-70, col 1) paginates into multiple pages but is scrolled entirely out of
        // the viewport's visible columns (only column 2 is in view). Area 2 (rows 1-5, col 2) is fully
        // visible and must continue numbering from where area 1's (invisible) pages left off - Excel's
        // Page Break Preview numbers every page of a multi-area job continuously regardless of what is
        // currently scrolled into view. Before the fix, the `continue` at the visibility check skipped the
        // pageNumberOffset increment entirely, so area 2's page would incorrectly restart at 1.
        var sheetId = SheetId.New();
        var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 70, 1));
        var area2 = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 5, 2));

        // Viewport only exposes column 2 (area 1's column 1 is scrolled out of view) and rows 1-10 (covers
        // area 2's rows 1-5 fully; area 1's rows 1-70 are irrelevant since its column is already excluded).
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 10).Select(row => new RowMetric((uint)row, 20, (row - 1) * 20.0)).ToList(),
            [new ColMetric(2, 40, 0)],
            null,
            []);

        // Sanity check: area 1 alone (as the single-area overload would see it, with a viewport that DOES
        // include its column) paginates into more than one page, so it has a nonzero page count to lose.
        var area1OnlyViewport = new ViewportModel(
            [],
            Enumerable.Range(1, 70).Select(row => new RowMetric((uint)row, 20, (row - 1) * 20.0)).ToList(),
            [new ColMetric(1, 40, 0)],
            null,
            []);
        var area1OnlyLayout = PageBreakPreviewLayoutPlanner.Calculate(
            area1OnlyViewport,
            area1,
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 100,
            actualHeight: 1500);
        var area1PageCount = area1OnlyLayout.Pages.Count;
        area1PageCount.Should().BeGreaterThan(1, "area 1 must contribute more than one page for this test to be meaningful");

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printAreas: [area1, area2],
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 100,
            actualHeight: 250);

        var page = layout.Pages.Should().ContainSingle("only area 2 is visible; area 1 is scrolled off-screen").Which;
        page.PageNumber.Should().Be(area1PageCount + 1,
            "area 2's page number must continue after area 1's page count, even though area 1 contributed no visible tiles");
    }

    [Fact]
    public void R106_Calculate_MultiArea_BothAreasVisible_NoRegressionInContinuousNumbering()
    {
        // No-regression sibling: when both areas ARE visible (the existing
        // Calculate_MultiArea_DoesNotMaskSecondPrintArea path), numbering must remain continuous and
        // start at 1 - the fix must not change behavior for the already-covered fully-visible case.
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 5).Select(row => new RowMetric((uint)row, 20, (row - 1) * 20.0)).ToList(),
            Enumerable.Range(1, 7).Select(col => new ColMetric((uint)col, 40, (col - 1) * 40.0)).ToList(),
            null,
            []);
        var area1 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 3));
        var area2 = new GridRange(new CellAddress(sheetId, 1, 5), new CellAddress(sheetId, 5, 7));

        var layout = PageBreakPreviewLayoutPlanner.Calculate(
            viewport,
            printAreas: [area1, area2],
            rowPageBreaks: null,
            columnPageBreaks: null,
            WorksheetPageOrder.DownThenOver,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            actualWidth: 310,
            actualHeight: 118);

        layout.Pages.Should().HaveCountGreaterThan(1);
        layout.Pages.Select(page => page.PageNumber).Should().BeInAscendingOrder();
        layout.Pages.Select(page => page.PageNumber).Should().OnlyHaveUniqueItems();
        layout.Pages.First().PageNumber.Should().Be(1);
    }

    [Fact]
    public void CalculateWatermarkFontSize_ClampsToLegibleRange()
    {
        PageBreakPreviewLayoutPlanner.CalculateWatermarkFontSize(new LayoutRect(0, 0, 50, 50))
            .Should().Be(24.0);
        PageBreakPreviewLayoutPlanner.CalculateWatermarkFontSize(new LayoutRect(0, 0, 2000, 2000))
            .Should().Be(96.0);
        PageBreakPreviewLayoutPlanner.CalculateWatermarkFontSize(new LayoutRect(0, 0, 400, 300))
            .Should().Be(54.0);
    }

    private static ViewportModel CreatePageLayoutViewport(int firstRow, int rowCount) =>
        new(
            [],
            Enumerable.Range(firstRow, rowCount)
                .Select(row => new RowMetric((uint)row, (row - firstRow) * 20, 20))
                .ToList(),
            Enumerable.Range(1, 5)
                .Select(col => new ColMetric((uint)col, (col - 1) * 40, 40))
                .ToList(),
            null,
            []);

    private static PageBreakPreviewPageLayout CalculateSinglePage(ViewportModel viewport, GridRange printArea) =>
        PageBreakPreviewLayoutPlanner.Calculate(
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
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                actualWidth: 230,
                actualHeight: 238)
            .Pages
            .Should()
            .ContainSingle()
            .Which;
}
