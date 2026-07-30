using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Tests for <see cref="SheetPdfPageSetupResolver"/>: paper-size → points, orientation swap,
/// margin-derived content rect, and per-sheet page capacity from scale-to-fit.
/// </summary>
public sealed class SheetPdfPageSetupResolverTests
{
    private const double Pts = SheetPdfPageSetupResolver.PdfPointsPerInch; // 72

    // -----------------------------------------------------------------------
    // Paper size → PDF points
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(WorksheetPaperSize.Letter,    WorksheetPageOrientation.Portrait,  8.5  * 72, 11.0  * 72)]
    [InlineData(WorksheetPaperSize.Letter,    WorksheetPageOrientation.Landscape, 11.0 * 72, 8.5   * 72)]
    [InlineData(WorksheetPaperSize.A4,        WorksheetPageOrientation.Portrait,  8.27 * 72, 11.69 * 72)]
    [InlineData(WorksheetPaperSize.A4,        WorksheetPageOrientation.Landscape, 11.69* 72, 8.27  * 72)]
    [InlineData(WorksheetPaperSize.Legal,     WorksheetPageOrientation.Portrait,  8.5  * 72, 14.0  * 72)]
    [InlineData(WorksheetPaperSize.A3,        WorksheetPageOrientation.Portrait,  11.69* 72, 16.54 * 72)]
    [InlineData(WorksheetPaperSize.Tabloid,   WorksheetPageOrientation.Portrait,  11.0 * 72, 17.0  * 72)]
    public void ResolvePageSizePoints_HonorsPaperSizeAndOrientation(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        double expectedWidthPt,
        double expectedHeightPt)
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = paperSize;
        sheet.PageOrientation = orientation;

        var (w, h) = SheetPdfPageSetupResolver.ResolvePageSizePoints(sheet);

        w.Should().BeApproximately(expectedWidthPt,  0.5,
            $"paper={paperSize} orientation={orientation} width");
        h.Should().BeApproximately(expectedHeightPt, 0.5,
            $"paper={paperSize} orientation={orientation} height");
    }

    [Fact]
    public void ResolvePageSizePoints_LandscapeSwapsWidthAndHeight()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;

        var (portraitW, portraitH) = SheetPdfPageSetupResolver.ResolvePageSizePoints(sheet);

        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        var (landscapeW, landscapeH) = SheetPdfPageSetupResolver.ResolvePageSizePoints(sheet);

        landscapeW.Should().BeApproximately(portraitH, 0.01, "landscape width = portrait height");
        landscapeH.Should().BeApproximately(portraitW, 0.01, "landscape height = portrait width");
    }

    // -----------------------------------------------------------------------
    // Margins → content rect via ResolveOptions
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveOptions_DerivesPageDimensionsFromPaperAndOrientation()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = new WorksheetPageMargins(1.0, 1.0, 1.0, 1.0);

        var opts = SheetPdfPageSetupResolver.ResolveOptions(sheet);

        opts.PageWidthPoints.Should().BeApproximately(8.5  * Pts, 0.5, "Letter width");
        opts.PageHeightPoints.Should().BeApproximately(11.0 * Pts, 0.5, "Letter height");
    }

    [Fact]
    public void ResolveOptions_LandscapeDerivesSwappedDimensions()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageMargins     = WorksheetPageMargins.Narrow;

        var opts = SheetPdfPageSetupResolver.ResolveOptions(sheet);

        // A4 landscape: width = 11.69", height = 8.27"
        opts.PageWidthPoints.Should().BeGreaterThan(opts.PageHeightPoints,
            "landscape width exceeds height");
        opts.PageWidthPoints.Should().BeApproximately(11.69 * Pts, 0.5);
        opts.PageHeightPoints.Should().BeApproximately(8.27  * Pts, 0.5);
    }

    [Fact]
    public void ResolveOptions_CustomMarginsReflectedInUniformMarginPoints()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = new WorksheetPageMargins(Left: 0.5, Right: 0.5, Top: 1.0, Bottom: 1.0);

        var opts = SheetPdfPageSetupResolver.ResolveOptions(sheet);

        // Uniform margin = min(all four) = 0.5" × 72 = 36 pt.
        opts.MarginPoints.Should().BeApproximately(0.5 * Pts, 0.5);
    }

    // -----------------------------------------------------------------------
    // Capacity: page-count for fit-to-width
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveCapacity_FitToWidthOne_CollapsesAllColumnsOntoOnePage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;

        // FitToWidth=1 means all columns on one horizontal page.
        sheet.ScaleToFit = new WorksheetScaleToFit(null, FitToPagesWide: 1, null);

        // 20 columns of default width — each is ~60 px ≈ impossible on one page normally.
        var range = GridRange.Parse("A1:T50", sheet.Id);
        AddCells(workbook, sheet, range);

        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        // FitToWidth=1: all 20 body columns fit on 1 column-page.
        capacity.ColumnsPerPage.Should().Be(20, "all columns on one horizontal page");
    }

    [Fact]
    public void ResolveCapacity_ExplicitScalePercent50_DoublesItemsPerPage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit  = WorksheetScaleToFit.Default; // 100%

        var range = GridRange.Parse("A1:Z100", sheet.Id);
        var at100 = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: 50, null, null);
        var at50 = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        // At 50% scale each page holds roughly 2× the rows/columns as at 100%.
        at50.RowsPerPage.Should().BeGreaterThan(at100.RowsPerPage,
            "50% scale allows more rows per page than 100%");
        at50.ColumnsPerPage.Should().BeGreaterThan(at100.ColumnsPerPage,
            "50% scale allows more columns per page than 100%");
    }

    [Fact]
    public void ResolveCapacity_DefaultA4Narrow_ProducesPositiveCapacity()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit      = WorksheetScaleToFit.Default;

        var range = GridRange.Parse("A1:J50", sheet.Id);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().BeGreaterThan(0u);
        capacity.ColumnsPerPage.Should().BeGreaterThan(0u);
    }

    [Fact]
    public void ResolveCapacity_LandscapeFitsMoreColumnsThanPortrait()
    {
        var workbook = new Workbook("W");

        var portraitSheet = workbook.AddSheet("Portrait");
        portraitSheet.PaperSize       = WorksheetPaperSize.A4;
        portraitSheet.PageOrientation = WorksheetPageOrientation.Portrait;
        portraitSheet.PageMargins     = WorksheetPageMargins.Narrow;

        var landscapeSheet = workbook.AddSheet("Landscape");
        landscapeSheet.PaperSize       = WorksheetPaperSize.A4;
        landscapeSheet.PageOrientation = WorksheetPageOrientation.Landscape;
        landscapeSheet.PageMargins     = WorksheetPageMargins.Narrow;

        var range = GridRange.Parse("A1:Z50", portraitSheet.Id);
        var portraitCap  = SheetPdfPageSetupResolver.ResolveCapacity(portraitSheet,  range);

        var rangeL = GridRange.Parse("A1:Z50", landscapeSheet.Id);
        var landscapeCap = SheetPdfPageSetupResolver.ResolveCapacity(landscapeSheet, rangeL);

        landscapeCap.ColumnsPerPage.Should().BeGreaterThan(portraitCap.ColumnsPerPage,
            "landscape orientation makes more columns fit per page");
    }

    // -----------------------------------------------------------------------
    // CreatePlanFromPageSetup: page count via the planner
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatePlanFromPageSetup_FitToWidthOne_ProducesOneColumnPage()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("Data");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit  = new WorksheetScaleToFit(null, FitToPagesWide: 1, FitToPagesTall: null);

        // Add enough cells that without fit-to-width we'd get multiple column pages.
        for (var col = 1u; col <= 20u; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new TextValue($"C{col}"));

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.SheetPlans.Should().HaveCount(1);
        plan.SheetPlans[0].ColumnPageCount.Should().Be(1,
            "FitToWidth=1 must collapse all columns to 1 horizontal page");
    }

    [Fact]
    public void CreatePlanFromPageSetup_LandscapeA4_ProducesValidPlan()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("Sheet1");
        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageMargins     = new WorksheetPageMargins(1.0, 0.5, 1.0, 0.5);

        for (var row = 1u; row <= 10u; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));

        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            WorkbookExportPrintSurface.MacOs);

        plan.IsReady.Should().BeTrue();
        plan.SheetPlans.Should().HaveCount(1);
        plan.SheetPlans[0].PageCount.Should().BeGreaterThan(0);
    }

    // -----------------------------------------------------------------------
    // WorkbookPdfContentBuilder.BuildWithPageSetup: page dimensions
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildWithPageSetup_PageDimensionsMatchSheetPaperAndOrientation()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageMargins     = WorksheetPageMargins.Narrow;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, 0);
        var document   = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);

        document.Pages.Should().HaveCountGreaterThan(0);
        var page = document.Pages[0];

        // A4 landscape: width ≈ 841.68 pt, height ≈ 595.44 pt
        page.WidthPoints.Should().BeApproximately(11.69 * 72, 1.0,
            "A4 landscape width in points");
        page.HeightPoints.Should().BeApproximately(8.27  * 72, 1.0,
            "A4 landscape height in points");
        page.WidthPoints.Should().BeGreaterThan(page.HeightPoints,
            "landscape page should be wider than tall");
    }

    [Fact]
    public void BuildWithPageSetup_LetterPortrait_CorrectMediaBox()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = WorksheetPageMargins.Normal;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, 0);
        var document   = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);

        document.Pages[0].WidthPoints.Should().BeApproximately(8.5  * 72, 0.5);
        document.Pages[0].HeightPoints.Should().BeApproximately(11.0 * 72, 0.5);
    }

    [Fact]
    public void BuildWithPageSetup_GridlinesOff_NoGridlineOpsEmitted()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PrintGridlines = false;
        sheet.PageMargins    = WorksheetPageMargins.Narrow;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, 0);
        var document   = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);

        document.Pages.Should().HaveCountGreaterThan(0);
        var lineOps = document.Pages[0].Ops.OfType<Free.Shared.Pdf.PdfLine>().ToList();
        lineOps.Should().BeEmpty("PrintGridlines=false must not emit any PdfLine ops");
    }

    [Fact]
    public void BuildWithPageSetup_GridlinesOn_EmitsGridlineOps()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PrintGridlines = true;
        sheet.PageMargins    = WorksheetPageMargins.Narrow;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("C"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, 0);
        var document   = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);

        document.Pages.Should().HaveCountGreaterThan(0);
        var lineOps = document.Pages[0].Ops.OfType<Free.Shared.Pdf.PdfLine>().ToList();
        lineOps.Should().NotBeEmpty("PrintGridlines=true must emit PdfLine ops for gridlines");
    }

    [Fact]
    public void BuildWithPageSetup_HeaderFooter_EmitsTextOpsForNonEmptyBands()
    {
        var workbook = new Workbook("Book");
        var sheet    = workbook.AddSheet("Data");
        sheet.PageMargins = WorksheetPageMargins.Normal;
        // Header: left="Left", center="Page &P", right="Right"
        sheet.PageHeader = new WorksheetHeaderFooter("Left", "Page &P", "Right");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Footer", "");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, 0);
        var document   = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);

        document.Pages.Should().HaveCountGreaterThan(0);
        var allText = document.Pages[0].Ops
            .OfType<Free.Shared.Pdf.PdfText>()
            .Select(t => t.Text)
            .ToList();

        allText.Should().Contain("Left",   "left header section should appear");
        allText.Should().Contain("Right",  "right header section should appear");
        allText.Should().Contain("Footer", "footer center section should appear");
        // &P expands to page number "1"
        allText.Should().Contain(t => t.Contains("Page") || t.Contains("1"),
            "header center should contain page-number text");
    }

    // -----------------------------------------------------------------------
    // R96-services-pagesetup-header-band-1: header/footer band model must agree with the WPF
    // print-preview path (PagePaginationPlanner), which sits the header/footer margin WITHIN the
    // top/bottom margin (Excel's own model), not additionally on top of it.
    // -----------------------------------------------------------------------

    [Theory]
    // Both header/footer margins comfortably within the top/bottom margin -- Excel's own default
    // page setup (0.75in margins, 0.3in header/footer margins) reserves nothing extra for the band.
    [InlineData(0.75, 0.75, 0.3, 0.3, "both, within margin (Excel's universal default)")]
    // Header only (footer margin is 0 -- "neither" for the bottom edge).
    [InlineData(0.75, 0.75, 0.3, 0.0, "header-only")]
    // Footer only (header margin is 0 -- "neither" for the top edge).
    [InlineData(0.75, 0.75, 0.0, 0.3, "footer-only")]
    // Neither header nor footer margin configured.
    [InlineData(0.75, 0.75, 0.0, 0.0, "neither")]
    // Header/footer margin LARGER than the top/bottom margin -- the body must shrink further to
    // max(margin, headerMargin), not margin + headerMargin.
    [InlineData(0.3, 0.3, 0.75, 0.75, "both, header/footer margin larger than the margin (reverse)")]
    // Margin larger than the header/footer margin by a wide gap (the mirror image of the above).
    [InlineData(1.0, 1.0, 0.1, 0.1, "both, margin much larger than header/footer margin")]
    public void ResolveCapacity_HeaderFooterBandModel_MatchesWpfPagePaginationPlanner(
        double topMargin, double bottomMargin, double headerMargin, double footerMargin, string scenario)
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = new WorksheetPageMargins(Left: 0.5, Right: 0.5, Top: topMargin, Bottom: bottomMargin);
        sheet.HeaderMargin    = headerMargin;
        sheet.FooterMargin    = footerMargin;

        var range = GridRange.Parse("A1:J50", sheet.Id);
        AddCells(workbook, sheet, range);

        var pdfCapacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);
        var wpfCapacity = PagePaginationPlanner.CalculatePageCapacity(
            range,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowHeights,
            sheet.DefaultRowHeight,
            sheet.ColumnWidths,
            sheet.DefaultColumnWidth,
            sheet.HeaderMargin,
            sheet.FooterMargin);

        pdfCapacity.RowsPerPage.Should().Be(wpfCapacity.RowsPerPage,
            $"[{scenario}] the PDF-export page capacity must match the WPF print-preview page capacity for the same page setup");
        pdfCapacity.ColumnsPerPage.Should().Be(wpfCapacity.ColumnsPerPage,
            $"[{scenario}] the PDF-export page capacity must match the WPF print-preview page capacity for the same page setup");
    }

    [Fact]
    public void ResolveCapacity_HeaderMarginLargerThanTopMargin_BodyShrinksToHeaderMarginNotTheSum()
    {
        // top margin 0.3in, header margin 1.0in: Excel's model puts the body top at
        // max(0.3, 1.0) = 1.0in, NOT 0.3 + 1.0 = 1.3in. Pin the exact expected rows/page by comparing
        // against a sheet whose top margin is set directly to that same 1.0in with no separate header
        // band (headerMargin=0, so bodyTop = max(1.0, 0) = 1.0in too) -- the two must produce identical
        // capacity since Excel's own body-top formula collapses them to the same effective margin.
        var workbookA = new Workbook("A");
        var sheetA    = workbookA.AddSheet("S");
        sheetA.PaperSize       = WorksheetPaperSize.A4;
        sheetA.PageOrientation = WorksheetPageOrientation.Portrait;
        sheetA.PageMargins     = new WorksheetPageMargins(Left: 0.5, Right: 0.5, Top: 0.3, Bottom: 0.75);
        sheetA.HeaderMargin    = 1.0;
        sheetA.FooterMargin    = 0.0;
        var rangeA = GridRange.Parse("A1:J50", sheetA.Id);
        AddCells(workbookA, sheetA, rangeA);

        var workbookB = new Workbook("B");
        var sheetB    = workbookB.AddSheet("S");
        sheetB.PaperSize       = WorksheetPaperSize.A4;
        sheetB.PageOrientation = WorksheetPageOrientation.Portrait;
        sheetB.PageMargins     = new WorksheetPageMargins(Left: 0.5, Right: 0.5, Top: 1.0, Bottom: 0.75);
        sheetB.HeaderMargin    = 0.0;
        sheetB.FooterMargin    = 0.0;
        var rangeB = GridRange.Parse("A1:J50", sheetB.Id);
        AddCells(workbookB, sheetB, rangeB);

        var capacityA = SheetPdfPageSetupResolver.ResolveCapacity(sheetA, rangeA);
        var capacityB = SheetPdfPageSetupResolver.ResolveCapacity(sheetB, rangeB);

        capacityA.RowsPerPage.Should().Be(capacityB.RowsPerPage,
            "a 1.0in header margin over a 0.3in top margin must shrink the body exactly as much as a plain 1.0in top margin -- " +
            "not 0.3in (margin) + 1.0in (header) = 1.3in of reservation");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PortablePdfExportPlan CreateExportPlanFromPageSetup(Workbook workbook, int sheetIndex)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: sheetIndex),
            WorkbookExportPrintSurface.MacOs);

        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        return PortablePdfExportPlanner.CreatePlan(printPlan);
    }

    private static void AddCells(Workbook workbook, Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"R{row}C{col}"));
    }
}
