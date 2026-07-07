using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED11 finding P98: header/footer run measurement must use
/// each run's own &amp;nn font size (not the fixed 9pt PrintFontSize) both when laying out center/right
/// alignment and when recording the PDF selectable-text overlay, so a styled header like
/// "&amp;18Quarterly Report" is centered/right-aligned correctly and its PDF overlay geometry matches
/// the rasterized 18pt text instead of assuming 9pt.
/// </summary>
public sealed class FreeXCleanupMED11Tests
{
    [Fact]
    public void RenderWorksheet_HeaderRunOverlay_UsesRunFontSizeNotFixedPrintFontSize()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Header font size print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("body"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            // &18 sets an 18pt run size, distinct from the fixed 9pt PrintFontSize the pre-fix
            // measurement/overlay code hardcoded regardless of the run's actual styled size.
            sheet.PageHeader = new WorksheetHeaderFooter("", "&18Quarterly Report", "");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page)
                .Where(overlay => overlay.Text.Contains("Quarterly Report", StringComparison.Ordinal))
                .ToList();

            overlays.Should().ContainSingle();
            // Pre-fix this was hardcoded to PrintFontSize (9.0) regardless of the run's &18 size.
            overlays[0].FontSize.Should().Be(18.0);
        });
    }
}
