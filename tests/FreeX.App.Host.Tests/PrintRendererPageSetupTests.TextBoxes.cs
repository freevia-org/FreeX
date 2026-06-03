using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
{
    [Fact]
    public void RenderWorksheet_PrintsVisibleTextBoxWithSelectableTextOverlay()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Text box print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Anchor"));
            sheet.TextBoxes.Add(new TextBoxModel
            {
                Anchor = new CellAddress(sheet.Id, 2, 2),
                Text = "Printable callout",
                Width = 96,
                Height = 42,
                FillColor = new CellColor(200, 220, 240),
                OutlineColor = new CellColor(20, 70, 120)
            });
            sheet.TextBoxes.Add(new TextBoxModel
            {
                Anchor = new CellAddress(sheet.Id, 2, 2),
                Text = "Hidden callout",
                IsVisible = false
            });
            sheet.TextBoxes.Add(new TextBoxModel
            {
                Anchor = new CellAddress(sheet.Id, 25, 25),
                Text = "Off-page callout"
            });

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            overlays.Should().ContainEquivalentOf(new
            {
                Text = "Printable callout",
                X = 52.0,
                Y = 52.0,
                FontSize = 9.0,
                Bold = false
            });
            overlays.Select(overlay => overlay.Text).Should().NotContain("Hidden callout");
            overlays.Select(overlay => overlay.Text).Should().NotContain("Off-page callout");
            CountApproximateRgbPixels(page, 200, 220, 240).Should().BeGreaterThan(100);
        });
    }

    [Fact]
    public void RenderWorksheet_BoundsLongTextBoxOverlayBeforeHiddenTail()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Long text box print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Anchor"));
            sheet.TextBoxes.Add(new TextBoxModel
            {
                Anchor = new CellAddress(sheet.Id, 2, 2),
                Text = $"{new string('x', 300)} hidden-tail-token",
                Width = 72,
                Height = 24
            });

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlays.Should().NotContain(text => text.Contains("hidden-tail-token", StringComparison.Ordinal));
            overlays.Should().Contain(text => text.EndsWith("\u2026", StringComparison.Ordinal));
        });
    }
}
