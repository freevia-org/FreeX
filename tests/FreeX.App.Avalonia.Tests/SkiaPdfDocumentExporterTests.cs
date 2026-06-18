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

    private static int ResolveSheetIndex(Workbook workbook, Sheet sheet)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
            if (workbook.Sheets[index].Id == sheet.Id)
                return index;
        return 0;
    }
}
