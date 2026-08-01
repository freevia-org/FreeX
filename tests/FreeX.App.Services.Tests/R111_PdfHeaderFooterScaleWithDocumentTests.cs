using FluentAssertions;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R112-services-headerfooter-scale-with-document-1: Sheet.HeaderFooterScaleWithDocument (Excel's Page
/// Setup &gt; Header/Footer &gt; "Scale with document" checkbox, default checked) is round-tripped and
/// user-editable, and R111 already wired it into the WPF native print/print-preview renderer
/// (<c>PrintRenderer.RenderPageVisual</c>) -- but the portable/Skia PDF export tier
/// (<see cref="WorkbookPdfContentBuilder.BuildWithPageSetup"/>, which both Save-As-PDF on Windows and
/// the Avalonia/Linux/macOS shell go through) never consulted it: header/footer text was always drawn
/// at the fixed 8pt default (or an explicit &amp;-format run size) regardless of the page's own print
/// scale. These tests drive the real PDF export entry point end-to-end (WorkbookExportPrintPlanner -&gt;
/// PortablePdfExportPlanner -&gt; WorkbookPdfContentBuilder.BuildWithPageSetup), matching the shape of the
/// existing R87_HeaderFooterPdfTests in this file's directory, and assert on the actual drawn
/// <see cref="PdfText"/> op's <c>FontSize</c>.
/// </summary>
public sealed class R111_PdfHeaderFooterScaleWithDocumentTests
{
    private const double DefaultHeaderFooterFontSize = 8.0;

    [Fact]
    public void BuildWithPageSetup_ScaleWithDocumentTrueAtHalfPageScale_ShrinksFooterFontSizeToMatch()
    {
        var page = BuildPageWithFooter(center: "Confidential", scalePercent: 50, scaleWithDocument: true);

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Confidential");

        // Before the fix this was always 8.0 (the fixed unscaled default), completely ignoring the
        // 50% page scale -- Excel's real "Scale with document" (default checked) behavior halves the
        // header/footer font size right along with the grid, matching the already-fixed WPF path.
        run.FontSize.Should().BeApproximately(DefaultHeaderFooterFontSize * 0.5, 0.01,
            "Sheet.HeaderFooterScaleWithDocument defaults to true, so a 50% page scale must shrink the " +
            "footer text's font size by the same ratio as the grid, matching Excel and the WPF print path");
    }

    /// <summary>
    /// No-regression sibling: when the user unchecks "Scale with document", Excel keeps header/footer
    /// text at its authored/default size no matter how the page content itself is scaled. This is the
    /// pre-fix (and must remain the post-fix) behavior when the flag is off.
    /// </summary>
    [Fact]
    public void BuildWithPageSetup_ScaleWithDocumentFalseAtHalfPageScale_KeepsFooterFontSizeConstant()
    {
        var page = BuildPageWithFooter(center: "Confidential", scalePercent: 50, scaleWithDocument: false);

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Confidential");

        run.FontSize.Should().BeApproximately(DefaultHeaderFooterFontSize, 0.01,
            "unchecking Scale with document must keep the footer text at its authored/default size " +
            "regardless of the page's own print scale");
    }

    /// <summary>
    /// No-regression sibling: at the (default) 100% print scale, the resolved multiplier is 1.0
    /// regardless of the flag, so header/footer font size is unaffected either way.
    /// </summary>
    [Fact]
    public void BuildWithPageSetup_ScaleWithDocumentTrueAtFullPageScale_FooterFontSizeUnchanged()
    {
        var page = BuildPageWithFooter(center: "Confidential", scalePercent: null, scaleWithDocument: true);

        var run = page.Ops.OfType<PdfText>().Single(t => t.Text == "Confidential");

        run.FontSize.Should().BeApproximately(DefaultHeaderFooterFontSize, 0.01,
            "at 100% print scale the header/footer scale multiplier must resolve to a no-op regardless " +
            "of Scale with document");
    }

    private static PdfContentPage BuildPageWithFooter(string center, int? scalePercent, bool scaleWithDocument)
    {
        var workbook = new Workbook("ScaledFooterPdf");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("DATA")));
        sheet.PageFooter = new WorksheetHeaderFooter("", center, "");
        sheet.HeaderFooterScaleWithDocument = scaleWithDocument;
        if (scalePercent is { } percent)
            sheet.ScaleToFit = new WorksheetScaleToFit(percent, null, null);

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
