using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for round-12 bucket Q7 findings.
/// </summary>
public sealed class FreeXR12Q7Tests
{
    /// <summary>
    /// R12-wpf-print-export-deep-1: a header/footer run colored via the &amp;K Excel format code
    /// (e.g. "&amp;KFF0000Confidential") must carry its real color into the PDF selectable-text
    /// overlay, not the hard-coded Colors.Black that previously made every non-black header/footer
    /// run render black in the selectable-text layer regardless of the raster color underneath.
    /// </summary>
    [Fact]
    public void RenderWorksheet_ColoredHeaderRunOverlay_UsesRunColorNotHardCodedBlack()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Colored header print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("body"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            // &KFF0000 sets a red run color via Excel's header/footer color format code.
            sheet.PageHeader = new WorksheetHeaderFooter("", "&KFF0000Confidential", "");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page)
                .Where(overlay => overlay.Text.Contains("Confidential", StringComparison.Ordinal))
                .ToList();

            overlays.Should().ContainSingle();
            // Pre-fix this was hardcoded to Colors.Black regardless of the run's &K color.
            overlays[0].Color.Should().Be(Color.FromRgb(0xFF, 0x00, 0x00));
        });
    }
}
