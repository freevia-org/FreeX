using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R111-services-multiline-header-footer-1: Excel's Header/Footer editor lets a user insert a
/// literal line break inside one section (Alt+Enter), producing a <see cref="WorksheetHeaderFooter"/>
/// string with an embedded '\n'. Before this fix, <see cref="WorkbookPdfContentBuilder"/>'s
/// <c>RenderHeaderFooterSection</c> drew every run's raw text (embedded '\n' included) via a single
/// <see cref="PdfText"/> op at one fixed baseline, so a multi-line section's later lines were never
/// drawn on their own row -- they were either invisible (glyph runs generally do not honor embedded
/// newlines) or overdrawn on top of the first line, and the header/footer band never grew to make
/// room either. This is the portable/Skia PDF export tier that both the Avalonia shell's File ▸ Print
/// and File ▸ Export to PDF funnel through (<see cref="PortablePdfExportPlanner"/> +
/// <c>WorkbookPdfContentBuilder.BuildWithPageSetup</c>), so this fix is what makes multi-line
/// header/footer sections reach the Avalonia/Linux/macOS shell, mirroring the WPF-side fix in
/// <c>PrintRenderer.HeaderFooterDrawing.cs</c>/<c>HeaderFooterPictures.cs</c>. These tests render
/// through the real <see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/> entry point (not a
/// hand-built draw-op list) and assert on the actual emitted <see cref="PdfText"/> ops.
/// </summary>
public sealed class R111_MultiLinePdfHeaderFooterTests
{
    [Fact]
    public void BuildWithPageSetup_MultiLineFooterSection_BothLinesEmittedAsSeparatePdfTextOps()
    {
        var page = BuildPageWithFooter("Confidential\nDo Not Distribute");

        // Before the fix, the whole "Confidential\nDo Not Distribute" string was drawn via a single
        // PdfText op at one fixed baseline -- no op ever carried the second line's own exact text.
        var line1 = page.Ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == "Confidential").Subject;
        var line2 = page.Ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == "Do Not Distribute").Subject;

        // The two lines must sit at different Y baselines, not stacked/overlapping at the same point.
        line2.Y.Should().NotBe(line1.Y,
            "the second line of a multi-line footer section must be emitted at its own baseline, not " +
            "silently dropped or overdrawn on top of the first line");
    }

    /// <summary>
    /// No-regression sibling: a plain single-line footer section (no embedded newline) must keep
    /// emitting exactly one PdfText op for its text, unaffected by the multi-line split.
    /// </summary>
    [Fact]
    public void BuildWithPageSetup_SingleLineFooterSection_StillEmitsOnePdfTextOp()
    {
        var page = BuildPageWithFooter("Confidential");

        page.Ops.OfType<PdfText>().Should().ContainSingle(t => t.Text == "Confidential");
    }

    private static PdfContentPage BuildPageWithFooter(string center)
    {
        var workbook = new Workbook("MultiLineFooter");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageFooter = new WorksheetHeaderFooter("", center, "");
        var cell = Cell.FromValue(new TextValue("Hi"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: 0);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();
        return doc.Pages[0];
    }
}
