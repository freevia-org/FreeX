using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PagePaginationPlannerTests
{
    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    [Fact]
    public void CalculatePageCapacity_DerivesRowsAndColumnsFromPrintableArea()
    {
        // A4 portrait, narrow (Excel: 0.25" left/right, 0.75" top/bottom) margins:
        // printable 7.77" x 10.19" at 96 dpi.
        // columns: floor((8.27-0.25-0.25)*96 / 40) = floor(745.92/40) = 18.
        // rows:    floor((11.69-0.75-0.75)*96 / 20) = floor(978.24/20) = 48.
        var capacity = PagePaginationPlanner.CalculatePageCapacity(
            Range(1, 1, 100, 100),
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        capacity.RowsPerPage.Should().Be(48);
        capacity.ColumnsPerPage.Should().Be(18);
    }

    [Fact]
    public void CalculatePageCapacity_OrientationSwapsPaperDimensions()
    {
        var portrait = PagePaginationPlanner.CalculatePageCapacity(
            Range(1, 1, 100, 100),
            WorksheetScaleToFit.Default,
            null,
            null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Normal);
        var landscape = PagePaginationPlanner.CalculatePageCapacity(
            Range(1, 1, 100, 100),
            WorksheetScaleToFit.Default,
            null,
            null,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Landscape,
            WorksheetPageMargins.Normal);

        // Landscape is wider and shorter -> more columns, fewer rows than portrait.
        landscape.ColumnsPerPage.Should().BeGreaterThan(portrait.ColumnsPerPage);
        landscape.RowsPerPage.Should().BeLessThan(portrait.RowsPerPage);
    }

    [Fact]
    public void CalculatePageCapacity_HalfScaleDoublesCapacity()
    {
        var capacity = PagePaginationPlanner.CalculatePageCapacity(
            Range(1, 1, 100, 100),
            new WorksheetScaleToFit(ScalePercent: 50, FitToPagesWide: null, FitToPagesTall: null),
            null,
            null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        // 100% capacity is 18 columns / 48 rows; 50% scale fits twice as much per page.
        capacity.ColumnsPerPage.Should().Be(36);
        capacity.RowsPerPage.Should().Be(96);
    }

    [Fact]
    public void Paginate_SplitsRangeAcrossMultiplePages()
    {
        var result = PagePaginationPlanner.Paginate(
            Range(1, 1, 70, 20),
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        // 70 rows / 48 per page = 2 row pages; 20 columns / 18 per page = 2 column pages.
        result.RowPageCount.Should().Be(2);
        result.ColumnPageCount.Should().Be(2);
        result.PageCount.Should().Be(4);
        result.RowSegments[0].Should().Be(new PageAxisSegment(1, 48));
        result.RowSegments[1].Should().Be(new PageAxisSegment(49, 70));
        result.ColumnSegments[0].Should().Be(new PageAxisSegment(1, 18));
        result.ColumnSegments[1].Should().Be(new PageAxisSegment(19, 20));
        result.EffectiveScalePercent.Should().Be(100.0);
    }

    [Fact]
    public void BuildPlan_ExposesRendererRowAndColumnPlansWithTitlesAndManualBreaks()
    {
        var plan = PagePaginationPlanner.BuildPlan(
            Range(1, 1, 8, 8),
            WorksheetScaleToFit.Default,
            printTitleRows: new WorksheetRepeatRange(1, 1),
            printTitleColumns: new WorksheetRepeatRange(1, 1),
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowPageBreaks: [5],
            columnPageBreaks: [5]);

        plan.Capacity.Should().Be(new PageCapacity(RowsPerPage: 48, ColumnsPerPage: 18));
        plan.RowPageCount.Should().Be(2);
        plan.ColumnPageCount.Should().Be(2);
        plan.PageCount.Should().Be(4);
        plan.RowPlans[0].TitleRows.Should().Equal(1u);
        plan.RowPlans[1].TitleRows.Should().Equal(1u);
        plan.RowPlans[0].BodyRows.Should().Equal(2u, 3u, 4u);
        plan.RowPlans[1].BodyRows.Should().Equal(5u, 6u, 7u, 8u);
        plan.ColumnPlans[0].TitleColumns.Should().Equal(1u);
        plan.ColumnPlans[1].TitleColumns.Should().Equal(1u);
        plan.ColumnPlans[0].BodyColumns.Should().Equal(2u, 3u, 4u);
        plan.ColumnPlans[1].BodyColumns.Should().Equal(5u, 6u, 7u, 8u);
        plan.EffectiveScalePercent.Should().Be(100.0);
    }

    [Fact]
    public void Paginate_FitToOneWideCollapsesColumnsOntoASinglePage()
    {
        var result = PagePaginationPlanner.Paginate(
            Range(1, 1, 10, 100),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        result.ColumnPageCount.Should().Be(1);
        result.ColumnSegments[0].Should().Be(new PageAxisSegment(1, 100));
        result.RowPageCount.Should().Be(1);
    }

    [Fact]
    public void Paginate_PrintAreaLimitsThePaginatedRange()
    {
        // Print area is only B2:D4 of a much larger sheet -> a single page covering exactly that range.
        var result = PagePaginationPlanner.Paginate(
            Range(2, 2, 4, 4),
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        result.PageCount.Should().Be(1);
        result.RowSegments[0].Should().Be(new PageAxisSegment(2, 4));
        result.ColumnSegments[0].Should().Be(new PageAxisSegment(2, 4));
    }

    [Fact]
    public void Paginate_RepeatRowsAreExcludedFromBodyAndShrinkRowCapacity()
    {
        // Print range A1:C6 with row 1 as a repeated title and fit-to-2-tall.
        // Body rows 2..6 (5 rows) split across 2 pages; the title row is reprinted but tracked
        // separately, so it is excluded from each page's body segment.
        var withTitles = PagePaginationPlanner.Paginate(
            Range(1, 1, 6, 3),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 2),
            printTitleRows: new WorksheetRepeatRange(1, 1),
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        withTitles.RowPageCount.Should().Be(2);
        withTitles.RowSegments[0].Should().Be(new PageAxisSegment(2, 4));
        withTitles.RowSegments[1].Should().Be(new PageAxisSegment(5, 6));
    }

    [Fact]
    public void Paginate_RepeatColumnsAreExcludedFromBodyAndShrinkColumnCapacity()
    {
        var withTitles = PagePaginationPlanner.Paginate(
            Range(1, 1, 3, 6),
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 2, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: new WorksheetRepeatRange(1, 1),
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow);

        withTitles.ColumnPageCount.Should().Be(2);
        withTitles.ColumnSegments[0].Should().Be(new PageAxisSegment(2, 4));
        withTitles.ColumnSegments[1].Should().Be(new PageAxisSegment(5, 6));
    }

    [Fact]
    public void Paginate_ManualRowBreakForcesANewPage()
    {
        var result = PagePaginationPlanner.Paginate(
            Range(1, 1, 7, 3),
            new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null),
            printTitleRows: null,
            printTitleColumns: null,
            WorksheetPaperSize.A4,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Narrow,
            rowPageBreaks: [5]);

        result.RowPageCount.Should().Be(2);
        result.RowSegments[0].Should().Be(new PageAxisSegment(1, 4));
        result.RowSegments[1].Should().Be(new PageAxisSegment(5, 7));
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(5, 10)]
    [InlineData(500, 400)]
    public void CalculateEffectiveScalePercent_ExplicitPercentIsClampedToSupportedRange(int requested, double expected)
    {
        var scale = PagePaginationPlanner.CalculateEffectiveScalePercent(
            new WorksheetScaleToFit(ScalePercent: requested, FitToPagesWide: null, FitToPagesTall: null),
            actualRowPages: 10,
            actualColumnPages: 10);

        scale.Should().Be(expected);
    }

    [Fact]
    public void CalculateEffectiveScalePercent_FitToPagesShrinksToTheTighterAxis()
    {
        // Want 1 wide x 1 tall but content needs 2 column pages and 4 row pages without scaling.
        // Horizontal ratio 1/2 = 0.5; vertical ratio 1/4 = 0.25; the tighter (smaller) wins.
        var scale = PagePaginationPlanner.CalculateEffectiveScalePercent(
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: 1),
            actualRowPages: 4,
            actualColumnPages: 2);

        scale.Should().Be(25.0);
    }

    [Fact]
    public void CalculateEffectiveScalePercent_FitToPagesDoesNotEnlargeWhenContentAlreadyFits()
    {
        var scale = PagePaginationPlanner.CalculateEffectiveScalePercent(
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 4, FitToPagesTall: 4),
            actualRowPages: 1,
            actualColumnPages: 1);

        scale.Should().Be(100.0);
    }

    [Fact]
    public void CalculateEffectiveScalePercent_NoScaleSettingsDefaultsToOneHundred()
    {
        var scale = PagePaginationPlanner.CalculateEffectiveScalePercent(
            new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: null),
            actualRowPages: 3,
            actualColumnPages: 3);

        scale.Should().Be(100.0);
    }
}
