using System.IO;
using System.Text;
using FluentAssertions;
using FreeX.App.Avalonia.Pdf;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class SkiaPdfDocumentExporterTests
{
    [Fact]
    public void Save_EmbedsFontsAndRendersNonWinAnsiUnicodeText()
    {
        var workbook = new Workbook("Юникод");
        var sheet = workbook.AddSheet("Лист");
        // Text outside WinAnsi (Cyrillic + Greek) — the portable WinAnsi exporter flags these as
        // unsupported; the Skia exporter must render them via an automatically embedded font.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Привет"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Ελληνικά"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Σ"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:B2", sheet.Id));

        using var stream = new MemoryStream();
        var result = SkiaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        result.PageCount.Should().BeGreaterThan(0);
        var bytes = stream.ToArray();
        bytes.Length.Should().BeGreaterThan(2000, "an embedded font subset makes the PDF non-trivial");

        var content = Encoding.Latin1.GetString(bytes);
        content.Should().StartWith("%PDF-", "output must be a valid PDF");
        content.Should().Contain("FontFile", "Skia must embed the font program (FontFile2/FontFile3)");
        // Unicode text in PDF uses a composite (Type0) font.
        content.Should().Contain("/Type0");
    }

    [Fact]
    public void Save_RendersCjkTextViaFontFallbackWithoutError()
    {
        var workbook = new Workbook("CJK");
        var sheet = workbook.AddSheet("表");
        // CJK is outside any Latin default typeface; the exporter's per-codepoint font fallback
        // must resolve a system CJK face and still produce a valid embedded-font PDF.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("日本語"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("中文测试"));

        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:B1", sheet.Id));

        using var stream = new MemoryStream();
        var result = SkiaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        result.PageCount.Should().BeGreaterThan(0);
        var content = Encoding.Latin1.GetString(stream.ToArray());
        content.Should().StartWith("%PDF-");
        content.Should().Contain("FontFile");
    }

    [Fact]
    public void AvaloniaExportRoute_UsesSkiaAndEmbedsUnicodeFont_WhenSkiaAvailable()
    {
        // Exercises the same routing seam MainWindow's File → Export to PDF uses: when Skia can run,
        // the export must go through Skia and produce a Unicode-capable PDF (embedded font + /Type0
        // composite) for non-WinAnsi text (Cyrillic + Greek) that the portable WinAnsi writer cannot.
        var workbook = new Workbook("Юникод");
        var sheet = workbook.AddSheet("Лист");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Привет"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Ελληνικά"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Σ"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:B2", sheet.Id));

        using var stream = new MemoryStream();
        var outcome = AvaloniaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        // The CI lanes (Windows dev host + linux-app with the Skia native asset present) all have Skia,
        // so the route must select it; the portable fallback is for environments without the asset.
        outcome.Backend.Should().Be(AvaloniaPdfExportBackend.Skia);
        outcome.Result.PageCount.Should().BeGreaterThan(0);

        var content = Encoding.Latin1.GetString(stream.ToArray());
        content.Should().StartWith("%PDF-", "output must be a valid PDF");
        content.Should().Contain("FontFile", "Skia must embed the font program (FontFile2/FontFile3)");
        content.Should().Contain("/Type0", "Unicode text in PDF uses a composite (Type0) font");
    }

    [Fact]
    public void AvaloniaExportRoute_SurfacesImageDiagnostics_WhenHeaderPictureBytesAreUndecodable()
    {
        // R133-imageDiagnostics-wiring: an embedded header/footer picture with bytes the PDF writer
        // cannot decode (corrupt/unrecognized format) used to be silently omitted from the page with
        // no trace anywhere -- the shared writer's imageDiagnostics sink existed since r132 but no
        // production caller ever passed a collection in. This exercises the exact seam MainWindow's
        // File -> Export to PDF uses (AvaloniaPdfDocumentExporter.Save, options: null) and asserts the
        // loss reaches the one user-visible surface FreeX already uses for export status.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.PageHeader = new WorksheetHeaderFooter("", "&G", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            Left: null,
            Center: new WorksheetHeaderFooterPicture([0x00, 0x01, 0x02, 0x03, 0x04], "image/png"),
            Right: null);

        var exportPlan = CreateExportPlanFromPageSetup(workbook, sheetIndex: 0);

        using var stream = new MemoryStream();
        var outcome = AvaloniaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        outcome.Result.ImageDiagnostics.Should().NotBeEmpty(
            "the header picture's undecodable bytes must be surfaced, not silently dropped");
        outcome.Result.StatusText.Should().Contain("image warning",
            "the export status text is the only user-visible surface FreeX has for this kind of loss");
    }

    [Fact]
    public void AvaloniaExportRoute_NoImageDiagnostics_WhenNoPicturesAreEmbedded()
    {
        // Sibling no-regression: an export with no embedded pictures at all must not spuriously report
        // image warnings -- the diagnostics collection stays empty and the status text is unchanged.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, sheetIndex: 0);

        using var stream = new MemoryStream();
        var outcome = AvaloniaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        outcome.Result.ImageDiagnostics.Should().BeEmpty();
        outcome.Result.StatusText.Should().NotContain("image warning");
    }

    [Fact]
    public void Save_RejectsNonWritableStream()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var exportPlan = CreateExportPlan(workbook, sheet, GridRange.Parse("A1:A1", sheet.Id));

        using var readOnly = new MemoryStream(new byte[16], writable: false);
        var act = () => SkiaPdfDocumentExporter.Save(workbook, exportPlan, readOnly);

        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Page-setup fixture: A4 landscape, 1.0"/0.5" margins, FitToWidth=1,
    // gridlines on, header with &P.  Exercises the page-setup-aware path
    // and validates the MediaBox against the expected A4 landscape dimensions.
    // -----------------------------------------------------------------------

    [Fact]
    public void PageSetup_LandscapeA4_FitToWidthGridlinesHeader_ProducesCorrectMediaBox()
    {
        // Fixture: Landscape A4, 1.0" L/R margins, 0.5" T/B margins,
        // FitToWidth=1, gridlines on, header with &P token, enough rows
        // to span 2+ pages, 15 columns.
        const int rows = 80;
        const int cols = 15;

        var workbook = new Workbook("PageSetupFixture");
        var sheet = workbook.AddSheet("Data");

        sheet.PaperSize       = WorksheetPaperSize.A4;
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PageMargins     = new WorksheetPageMargins(Left: 1.0, Right: 1.0, Top: 0.5, Bottom: 0.5);
        sheet.ScaleToFit      = new WorksheetScaleToFit(null, FitToPagesWide: 1, FitToPagesTall: null);
        sheet.PrintGridlines  = true;
        sheet.PageHeader      = new WorksheetHeaderFooter("Left", "Center", "Page &P");

        for (var row = 1u; row <= rows; row++)
        for (var col = 1u; col <= cols; col++)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"R{row}C{col}"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, sheetIndex: 0);

        using var stream = new MemoryStream();
        var outcome = AvaloniaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        outcome.Result.PageCount.Should().BeGreaterThan(1,
            "80 rows should span more than 1 page on A4 landscape with default row height");

        var pdfBytes = stream.ToArray();
        var pdfText  = Encoding.Latin1.GetString(pdfBytes);

        pdfText.Should().StartWith("%PDF-", "output must be a valid PDF");

        // A4 landscape: width = 11.69" × 72 ≈ 841.68 pt, height = 8.27" × 72 ≈ 595.44 pt.
        // The MediaBox must reflect landscape dimensions (width > height).
        // Parse the first MediaBox from the PDF bytes.
        var mediaBox = ExtractFirstMediaBox(pdfText);
        mediaBox.Should().NotBeNull("PDF must contain a MediaBox entry");

        // The width dimension (index 2) must be ≈ 841 pt and height (index 3) ≈ 595 pt.
        mediaBox![2].Should().BeApproximately(841.68, 2.0,
            "A4 landscape PDF page width should be ~841 pts");
        mediaBox[3].Should().BeApproximately(595.44, 2.0,
            "A4 landscape PDF page height should be ~595 pts");
        mediaBox![2].Should().BeGreaterThan(mediaBox[3],
            "landscape page must be wider than tall");
    }

    [Fact]
    public void PageSetup_LetterPortrait_DefaultMargins_ProducesCorrectMediaBox()
    {
        var workbook = new Workbook("LetterPortrait");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize       = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PageMargins     = WorksheetPageMargins.Normal;

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

        var exportPlan = CreateExportPlanFromPageSetup(workbook, 0);

        using var stream = new MemoryStream();
        AvaloniaPdfDocumentExporter.Save(workbook, exportPlan, stream);

        var pdfText  = Encoding.Latin1.GetString(stream.ToArray());
        var mediaBox = ExtractFirstMediaBox(pdfText);
        mediaBox.Should().NotBeNull("PDF must contain a MediaBox entry");

        // Letter portrait: 8.5" × 11" → 612 pt × 792 pt
        mediaBox![2].Should().BeApproximately(612, 1.0, "Letter portrait width ~612 pts");
        mediaBox[3].Should().BeApproximately(792, 1.0, "Letter portrait height ~792 pts");
        mediaBox[3].Should().BeGreaterThan(mediaBox[2], "portrait page must be taller than wide");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PortablePdfExportPlan CreateExportPlan(Workbook workbook, Sheet sheet, GridRange range)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlan(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: ResolveSheetIndex(workbook, sheet)),
            new WorkbookExportPrintPageCapacity(RowsPerPage: 20, ColumnsPerPage: 5),
            WorkbookExportPrintSurface.MacOs);

        printPlan.IsReady.Should().BeTrue();
        return PortablePdfExportPlanner.CreatePlan(printPlan);
    }

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

    private static int ResolveSheetIndex(Workbook workbook, Sheet sheet)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
            if (workbook.Sheets[index].Id == sheet.Id)
                return index;
        return 0;
    }

    /// <summary>
    /// Parses the first MediaBox array from the PDF text and returns [x0, y0, width, height].
    /// Returns null if not found.
    /// </summary>
    private static double[]? ExtractFirstMediaBox(string pdfText)
    {
        // MediaBox format: /MediaBox [0 0 WidthPt HeightPt]
        var idx = pdfText.IndexOf("/MediaBox [", StringComparison.Ordinal);
        if (idx < 0) idx = pdfText.IndexOf("/MediaBox[", StringComparison.Ordinal);
        if (idx < 0) return null;

        var start = pdfText.IndexOf('[', idx);
        if (start < 0) return null;
        var end = pdfText.IndexOf(']', start);
        if (end < 0) return null;

        var values = pdfText[(start + 1)..end].Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 4) return null;

        if (double.TryParse(values[0], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var x0) &&
            double.TryParse(values[1], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var y0) &&
            double.TryParse(values[2], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var w) &&
            double.TryParse(values[3], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var h))
        {
            return [x0, y0, w, h];
        }
        return null;
    }
}
