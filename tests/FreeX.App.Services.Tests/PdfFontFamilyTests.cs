using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// F1-font-fallback: the Avalonia/Skia PDF export path (<see cref="WorkbookPdfContentBuilder"/>)
/// must forward each drawn run's actual font family into the emitted <see cref="PdfText.FontFamily"/>
/// instead of leaving it null, which makes <c>SkiaPdfWriter.PdfTypefaceSet.For</c> resolve to the
/// platform default typeface (e.g. Segoe UI) regardless of the workbook's authored font. Covers cell
/// text on both the page-setup-aware path (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>)
/// and the legacy fixed-geometry path (<see cref="WorkbookPdfContentBuilder.Build"/>), a floating text
/// box, and a header/footer formatted run.
/// </summary>
public sealed class PdfFontFamilyTests
{
    [Fact]
    public void BuildWithPageSetup_CellWithCourierNewFont_EmitsCourierNewFontFamily()
    {
        var workbook = new Workbook("FontFamily");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle { FontName = "Courier New" });
        var cell = Cell.FromValue(new TextValue("Monospace"));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var document = BuildPageSetupDocument(workbook);

        var op = document.Pages[0].Ops.OfType<PdfText>().Single(t => t.Text == "Monospace");
        op.FontFamily.Should().Be("Courier New",
            "the cell's authored font must reach the emitted PdfText op instead of leaving " +
            "FontFamily null (which resolves to the platform default typeface at render time)");
    }

    [Fact]
    public void BuildWithPageSetup_CellWithDefaultFont_StillEmitsTheWorkbookDefaultFontFamily()
    {
        // No-regression sibling: a cell using the workbook's ordinary default style (Calibri, never
        // explicitly overridden) must still populate FontFamily with that resolved name -- the fix
        // must not special-case only non-default fonts.
        var workbook = new Workbook("FontFamilyDefault");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Plain"));

        var document = BuildPageSetupDocument(workbook);

        var op = document.Pages[0].Ops.OfType<PdfText>().Single(t => t.Text == "Plain");
        op.FontFamily.Should().Be("Calibri");
    }

    [Fact]
    public void Build_LegacyPath_CellWithComicSansFont_EmitsComicSansFontFamily()
    {
        var workbook = new Workbook("LegacyFontFamily");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle { FontName = "Comic Sans MS" });
        var cell = Cell.FromValue(new TextValue("Playful"));
        cell.StyleId = style;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var options = new PortablePdfDocumentOptions();
        var document = WorkbookPdfContentBuilder.Build(workbook, pdfPlan, options);

        var op = document.Pages[0].Ops.OfType<PdfText>().Single(t => t.Text == "Playful");
        op.FontFamily.Should().Be("Comic Sans MS",
            "the legacy fixed-geometry PDF export path (used when Skia is unavailable) must also " +
            "forward the cell's authored font instead of leaving FontFamily null");
    }

    [Fact]
    public void BuildWithPageSetup_TextBox_EmitsFontFamilyFromTheResolvedTextBoxFont()
    {
        // NOTE: PageTextBoxLayoutPlanner.Build (FreeX.App.Presentation, a separate file from the one
        // this test suite covers) currently hardcodes every text box's resolved PageTextFont.FontFamily
        // to the constant PrintFontFamily ("Segoe UI") regardless of TextBoxModel.TextFontFamily -- a
        // distinct, pre-existing upstream gap. This test proves WorkbookPdfContentBuilder's own defect
        // (never reading Font.FontFamily into the PdfText op at all) is fixed by asserting the emitted
        // op's FontFamily equals whatever the upstream-resolved font family currently is, instead of
        // being left null.
        var workbook = new Workbook { Name = "TextBoxFont.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 4),
            Text = "Titled",
            Width = 140,
            Height = 48,
            HasFill = false
        });

        var document = BuildPageSetupDocument(workbook);

        var op = document.Pages.SelectMany(p => p.Ops).OfType<PdfText>().Single(t => t.Text == "Titled");
        op.FontFamily.Should().NotBeNull(
            "a floating text box's resolved font family must reach its emitted PdfText op instead of " +
            "being left null (which resolves to the platform default typeface at render time)");
    }

    [Fact]
    public void BuildWithPageSetup_HeaderRunWithExplicitFontCode_EmitsThatFontFamily()
    {
        var workbook = new Workbook("HeaderFont");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PageHeader = new WorksheetHeaderFooter("&\"Cambria,Bold\"Report Title", "", "");

        var document = BuildPageSetupDocument(workbook);

        var op = document.Pages[0].Ops.OfType<PdfText>().Single(t => t.Text == "Report Title");
        op.FontFamily.Should().Be("Cambria",
            "a header run's &\"FontName,Style\" format code must forward that font family to the " +
            "emitted PdfText op instead of leaving it null");
    }

    private static PdfContentDocument BuildPageSetupDocument(Workbook workbook)
    {
        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        document.Pages.Should().NotBeEmpty();
        return document;
    }
}
