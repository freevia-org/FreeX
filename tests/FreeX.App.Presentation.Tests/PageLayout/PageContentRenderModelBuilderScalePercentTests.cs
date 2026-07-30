using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R98: PageContentRenderModelBuilder.Build is the shared, portable renderer behind the interactive
/// Print Preview shell (via PrintPreviewPaginationContext.BuildPage) as well as every other renderer
/// that consumes PageContentLayout. It must honor the sheet's Page Setup &gt; Scaling (Scale%/Fit-to-
/// pages), resolved by PagePaginationPlanner into <see cref="PagePaginationResult.EffectiveScalePercent"/>,
/// the same way the source desktop print renderer and the page-setup-aware PDF export path already do
/// -- Print Preview must always show exactly what will print, at the correct in-page proportion.
/// </summary>
public sealed class PageContentRenderModelBuilderScalePercentTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void R98_AdjustToPercentShrinksCellGridAndFontGeometry()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var baseline = BuildFirstPage(workbook, sheet)!;

        sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
        var scaled = BuildFirstPage(workbook, sheet)!;

        // The whole grid rectangle shrinks in direct proportion to the resolved 50% scale -- matching
        // Excel's "Adjust to 50% normal size", which shrinks every printed element, not merely a
        // repagination hint that only kicks in once content overflows.
        scaled.GridBounds.Width.Should().BeApproximately(baseline.GridBounds.Width * 0.5, 0.01);
        scaled.GridBounds.Height.Should().BeApproximately(baseline.GridBounds.Height * 0.5, 0.01);

        var baseCell = baseline.Cells.Single(c => c.Row == 1 && c.Column == 1);
        var scaledCell = scaled.Cells.Single(c => c.Row == 1 && c.Column == 1);
        scaledCell.Bounds.Width.Should().BeApproximately(baseCell.Bounds.Width * 0.5, 0.01);
        scaledCell.Bounds.Height.Should().BeApproximately(baseCell.Bounds.Height * 0.5, 0.01);
        scaledCell.Bounds.Left.Should().BeApproximately(baseCell.Bounds.Left, 0.01,
            "the page margin/content origin is unaffected by scale; only extents shrink");

        // The cell's printed font size shrinks with the page, matching Excel: a shrunk printout has
        // visually smaller text, not full-size text overflowing a shrunk cell.
        scaledCell.Font.FontSize.Should().BeApproximately(baseCell.Font.FontSize * 0.5, 0.001);
    }

    [Fact]
    public void R98_AdjustToPercentShrinksGridlinesAndHeadingRects()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("b"));

        var baseline = BuildFirstPage(workbook, sheet)!;

        sheet.ScaleToFit = new WorksheetScaleToFit(40, null, null);
        var scaled = BuildFirstPage(workbook, sheet)!;

        scaled.GridLines.Should().HaveCount(baseline.GridLines.Count);
        // The rightmost vertical gridline, measured from each layout's own grid origin (the heading
        // gutter itself shrinks with scale too, so the scaled grid's left edge is not at the same
        // absolute X as the baseline's), spans 40% of the unscaled grid width.
        var baselineWidth = baseline.GridLines.Max(l => l.End.X) - baseline.GridBounds.Left;
        var scaledWidth = scaled.GridLines.Max(l => l.End.X) - scaled.GridBounds.Left;
        scaledWidth.Should().BeApproximately(baselineWidth * 0.4, 0.01);

        scaled.ColumnHeadings.Should().HaveCount(baseline.ColumnHeadings.Count);
        scaled.ColumnHeadings[0].Bounds.Width.Should().BeApproximately(
            baseline.ColumnHeadings[0].Bounds.Width * 0.4, 0.01);
        scaled.RowHeadings[0].Bounds.Height.Should().BeApproximately(
            baseline.RowHeadings[0].Bounds.Height * 0.4, 0.01);
    }

    [Fact]
    public void R98_AdjustToPercentShrinksTextBoxBoundsAndFont()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Anchor"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Printable note",
            // Large enough that even a steep 25% scale keeps both dimensions above
            // TextBoxFrameLayoutPlanner's minimum printable size (24x18), so the minimum-size clamp
            // does not mask the scale assertion below.
            Width = 400,
            Height = 200,
        });

        var baseline = BuildFirstPage(workbook, sheet)!;
        var baseBlock = baseline.TextBoxes.Should().ContainSingle().Subject;

        sheet.ScaleToFit = new WorksheetScaleToFit(25, null, null);
        var scaled = BuildFirstPage(workbook, sheet)!;
        var scaledBlock = scaled.TextBoxes.Should().ContainSingle().Subject;

        scaledBlock.Bounds.Width.Should().BeApproximately(baseBlock.Bounds.Width * 0.25, 0.01);
        scaledBlock.Bounds.Height.Should().BeApproximately(baseBlock.Bounds.Height * 0.25, 0.01);
        scaledBlock.Font.FontSize.Should().BeApproximately(baseBlock.Font.FontSize * 0.25, 0.001);
    }

    [Fact]
    public void R98_AdjustToPercentShrinksPictureBounds()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Width = 200,
            Height = 100,
        });

        var baseline = BuildFirstPage(workbook, sheet)!;
        var baseBlock = baseline.Pictures.Should().ContainSingle().Subject;

        sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
        var scaled = BuildFirstPage(workbook, sheet)!;
        var scaledBlock = scaled.Pictures.Should().ContainSingle().Subject;

        scaledBlock.Bounds.Width.Should().BeApproximately(baseBlock.Bounds.Width * 0.5, 0.01);
        scaledBlock.Bounds.Height.Should().BeApproximately(baseBlock.Bounds.Height * 0.5, 0.01);
    }

    [Fact]
    public void R98_AdjustToPercentShrinksChartBoundsInDirectProportion()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 8));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "Printable chart title",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
        });

        var baseline = BuildFirstPage(workbook, sheet)!;
        var baseBlock = baseline.Charts.Should().ContainSingle().Subject;

        sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
        var scaled = BuildFirstPage(workbook, sheet)!;
        var scaledBlock = scaled.Charts.Should().ContainSingle().Subject;

        scaledBlock.Bounds.Width.Should().BeApproximately(baseBlock.Bounds.Width * 0.5, 0.01);
        scaledBlock.Bounds.Height.Should().BeApproximately(baseBlock.Bounds.Height * 0.5, 0.01);
        // The chart's offset from the (unscaled, fixed) grid origin also shrinks in the same proportion.
        var gridLeft = baseline.GridBounds.Left;
        var gridTop = baseline.GridBounds.Top;
        (scaledBlock.Bounds.Left - gridLeft).Should().BeApproximately(
            (baseBlock.Bounds.Left - gridLeft) * 0.5, 0.01);
        (scaledBlock.Bounds.Top - gridTop).Should().BeApproximately(
            (baseBlock.Bounds.Top - gridTop) * 0.5, 0.01);
    }

    [Fact]
    public void R98_FitToOnePageWideTallShrinksRowsThatWouldOtherwiseOverflowThePhysicalPage()
    {
        var (workbook, sheet) = CreateWorkbook();
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        // Enough rows that the sheet naturally spans multiple row-pages at 100% scale.
        for (uint row = 1; row <= 200; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var unconstrainedPlan = Paginate(sheet);
        unconstrainedPlan.PageCount.Should().BeGreaterThan(1, "200 rows at the default row height need more than one page");

        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        var fitPlan = Paginate(sheet);
        fitPlan.PageCount.Should().Be(1, "Fit to 1 wide x 1 tall must collapse every row/column page onto a single page");

        var layout = PageContentRenderModelBuilder.Build(
            workbook, sheet, fitPlan, 0, Measurer, new DateTime(2026, 1, 1))!;

        // FreeX resolves "Fit to 1 page tall" by inflating the page's row CAPACITY to fit all 200 rows
        // (PagePaginationPlanner.ApplyScaleToFitCapacity), so pagePlan.EffectiveScalePercent itself
        // stays 100 here -- the real visual shrink comes entirely from the render model's defensive
        // residual-overflow guard (this is exactly the "Fit to 1 page tall" case Excel users rely on:
        // 200 rows of natural 20px height would print ~4000px tall, wildly overflowing an A4 page's
        // ~978px printable height, so it MUST shrink well below 100% to fit). Assert every row's
        // rendered (post-scale) height is far smaller than the unscaled 20px uniform fallback, and that
        // the whole printed grid now fits within the page's printable area -- both are impossible
        // without EffectiveScalePercent/residual-overflow scaling reaching this render model.
        var rowHeights = layout.Cells.Where(c => c.Column == 1).Select(c => c.Bounds.Height).Distinct().ToList();
        rowHeights.Should().NotBeEmpty();
        rowHeights.Should().AllSatisfy(h => h.Should().BeLessThan(20.0 * 0.5,
            "200 rows squeezed onto one A4 page must shrink well below their natural 20px height"));
        layout.GridBounds.Height.Should().BeLessThanOrEqualTo(
            layout.PrintableArea.Height + 0.5,
            "the whole grid must now fit within the page's printable height instead of overflowing it 4x over");
    }

    [Fact]
    public void R98_HeaderFooterBandsAreNotAffectedByScalePercent()
    {
        var (workbook, sheet) = CreateWorkbook("Budget.xlsx");
        sheet.Name = "Sheet1";
        sheet.PageHeader = new WorksheetHeaderFooter("&F", "", "");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a"));

        var baseline = BuildFirstPage(workbook, sheet)!;
        var baseRun = baseline.HeaderRuns.Single(r => r.Text == "Budget.xlsx");

        sheet.ScaleToFit = new WorksheetScaleToFit(30, null, null);
        var scaled = BuildFirstPage(workbook, sheet)!;
        var scaledRun = scaled.HeaderRuns.Single(r => r.Text == "Budget.xlsx");

        // Excel never scales header/footer text with Page Setup > Scaling -- only the printed sheet
        // body does. Mirrors the source desktop print renderer's ScaleTransform, which is pushed only
        // around the content area and never around the header/footer draw calls.
        scaledRun.Bounds.Should().Be(baseRun.Bounds);
        scaledRun.TextOrigin.Should().Be(baseRun.TextOrigin);
    }

    [Fact]
    public void R98_DefaultUnscaledSheetGeometryIsUnaffectedByTheFix()
    {
        // No-regression sibling: a sheet with no ScaleToFit configured (the overwhelming common case)
        // resolves EffectiveScalePercent == 100, so ScaleMeasurement/ResolveScaleRatio must be a no-op
        // and every existing (pre-fix) geometry/font expectation still holds exactly.
        var (workbook, sheet) = CreateWorkbook();
        var style = new CellStyle { FontSize = 14 };
        var styleId = workbook.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("styled"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        layout!.Cells.Single(c => c.Row == 1 && c.Column == 1).Font.FontSize.Should().Be(14);
    }

    private static void PopulateChartSource(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet) =>
        PageContentRenderModelBuilder.Build(workbook, sheet, Paginate(sheet), 0, Measurer, new DateTime(2026, 1, 1));

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
