using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R127-services-print-draft-quality-1: Sheet.PrintDraftQuality ("Draft quality" in Page Setup >
/// Sheet) is fully round-tripped through the model/XLSX/XLS/JSON adapters and exposed in the Page
/// Setup dialog on both shells, and the WPF native print/PDF path (PrintRenderer.HeaderFooter.cs /
/// HeaderFooterDrawing.cs) already suppresses charts, sheet pictures, and header/footer (&amp;G)
/// pictures when it is set -- but the portable Skia PDF-export path
/// (<see cref="WorkbookPdfContentBuilder"/>), the ONLY Save-As-PDF path on Linux/macOS, drew all
/// three unconditionally, so the same workbook with the same checkbox produced visibly different PDF
/// output depending on which OS/shell exported it. These tests drive the real product entry point,
/// <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>.
/// </summary>
public sealed class R127_PrintDraftQualitySuppressesPdfExportTests
{
    // ---------------------------------------------------------------------------------------------
    // THE FIX: charts.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BuildWithPageSetup_DraftQuality_SuppressesChartBarFills()
    {
        var (workbook, sheet) = CreateWorkbookWithColumnChart();
        sheet.PrintDraftQuality = true;

        var page = BuildPdfPage(workbook, sheet);

        ChartBarFills(page).Should().BeEmpty(
            "Excel's Draft Quality print option suppresses charts, but this portable PDF-export path " +
            "never consulted Sheet.PrintDraftQuality at all and always drew the chart's bars");
    }

    [Fact]
    public void BuildWithPageSetup_NoDraftQuality_StillRendersChartBarFills()
    {
        // No-regression sibling: an ordinary export (PrintDraftQuality false, the default) must keep
        // drawing the chart exactly as before this fix.
        var (workbook, sheet) = CreateWorkbookWithColumnChart();
        sheet.PrintDraftQuality.Should().BeFalse("default");

        var page = BuildPdfPage(workbook, sheet);

        ChartBarFills(page).Should().HaveCount(2, "2 data points must still render as bar fills when Draft Quality is off");
    }

    // ---------------------------------------------------------------------------------------------
    // Sheet pictures (Insert > Pictures).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BuildWithPageSetup_DraftQuality_SuppressesSheetPicture()
    {
        var workbook = new Workbook { Name = "DraftPicture.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.PrintDraftQuality = true;
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            ImageBytes = [137, 80, 78, 71, 1, 2, 3, 4],
            ContentType = "image/png",
        });

        var page = BuildPdfPage(workbook, sheet);

        page.Ops.OfType<PdfImage>().Should().BeEmpty(
            "Excel's Draft Quality print option suppresses raster pictures too, matching the WPF " +
            "PrintRenderer.HeaderFooter.cs `!draftQuality` guard around DrawPrintedPictures");
    }

    [Fact]
    public void BuildWithPageSetup_NoDraftQuality_StillRendersSheetPicture()
    {
        // No-regression sibling, mirrors R92_PicturePdfExportTests: the picture must still draw when
        // Draft Quality is off (the default).
        var workbook = new Workbook { Name = "NoDraftPicture.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        var imageBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 };
        sheet.Pictures.Add(new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            ImageBytes = imageBytes,
            ContentType = "image/png",
        });

        var page = BuildPdfPage(workbook, sheet);

        page.Ops.OfType<PdfImage>().Should().ContainSingle(img => img.ImageBytes.SequenceEqual(imageBytes));
    }

    // ---------------------------------------------------------------------------------------------
    // Header/footer (&G) pictures.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BuildWithPageSetup_DraftQuality_SuppressesHeaderFooterPicture()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var workbook = new Workbook("DraftLogo");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintDraftQuality = true;
        sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Left: new WorksheetHeaderFooterPicture(imageBytes, "image/png", Width: 96, Height: 48),
            Center: null,
            Right: null);

        var page = BuildPdfPage(workbook, sheet, addDefaultCell: true);

        page.Ops.OfType<PdfImage>().Should().BeEmpty(
            "Excel's Draft Quality print option suppresses header/footer &G pictures too, matching the " +
            "WPF PrintRenderer.HeaderFooterDrawing.cs `!draftQuality` guard around leftPicture/" +
            "centerPicture/rightPicture");
    }

    [Fact]
    public void BuildWithPageSetup_NoDraftQuality_StillRendersHeaderFooterPicture()
    {
        // No-regression sibling, mirrors R87_HeaderFooterPdfTests: the header picture must still draw
        // when Draft Quality is off (the default).
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var workbook = new Workbook("NoDraftLogo");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageHeader = new WorksheetHeaderFooter("&G", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Left: new WorksheetHeaderFooterPicture(imageBytes, "image/png", Width: 96, Height: 48),
            Center: null,
            Right: null);

        var page = BuildPdfPage(workbook, sheet, addDefaultCell: true);

        page.Ops.OfType<PdfImage>().Should().ContainSingle(img => img.ImageBytes.SequenceEqual(imageBytes));
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling family member the fix must NOT touch: text boxes are vector text content, not
    // "graphics" -- Excel's Draft Quality does not suppress them, and neither does the WPF path
    // (PrintRenderer.HeaderFooter.cs draws DrawPrintedTextBoxes unconditionally, outside the
    // `!draftQuality` block that guards charts/pictures).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BuildWithPageSetup_DraftQuality_StillRendersTextBoxText()
    {
        var workbook = new Workbook { Name = "DraftTextBox.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.PrintDraftQuality = true;
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 40,
            Text = "Draft note",
        });

        var page = BuildPdfPage(workbook, sheet);

        page.Ops.OfType<PdfText>().Should().Contain(t => t.Text == "Draft note",
            "text boxes are vector text, not 'graphics' -- Draft Quality must not suppress them, " +
            "matching the WPF path's unconditional DrawPrintedTextBoxes call");
    }

    private static List<PdfFillRect> ChartBarFills(PdfContentPage page) =>
        // Mirrors R113_ChartPdfExportEmbeddedFallbackTests.ChartBarFills: the chart's own background
        // fill (always solid white for this fixture, no explicit chart-area fill) is excluded so only
        // the actual data-bar fills remain.
        page.Ops.OfType<PdfFillRect>()
            .Where(r => r.Width > 0 && r.Height > 0 && r.Color is not { R: 0xFF, G: 0xFF, B: 0xFF })
            .ToList();

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbookWithColumnChart()
    {
        var workbook = new Workbook("DraftChart");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2)],
            Left = 300,
            Top = 20,
            Width = 200,
            Height = 120,
        });

        return (workbook, sheet);
    }

    private static PdfContentPage BuildPdfPage(Workbook workbook, Sheet sheet, bool addDefaultCell = false)
    {
        if (addDefaultCell)
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hi"));

        var sheetIndex = workbook.Sheets.ToList().IndexOf(sheet);
        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: sheetIndex);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();
        return doc.Pages[0];
    }
}
