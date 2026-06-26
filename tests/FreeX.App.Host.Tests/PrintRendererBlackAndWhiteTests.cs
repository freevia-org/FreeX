using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Tests for Black-and-White print mode in the WPF PrintRenderer.
/// B&W mode should: render the same page count as color mode, not throw,
/// and render cell text (the WPF print path is text-only so no fills to suppress there).
/// </summary>
public sealed class PrintRendererBlackAndWhiteTests
{
    [Fact]
    public void RenderWorksheet_BlackAndWhite_DoesNotThrow()
    {
        // Smoke test: rendering with PrintBlackAndWhite=true should produce pages without error.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("B&W WPF");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.PrintBlackAndWhite = true;

            var colorStyle = workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(255, 0, 0),
                FontColor = new CellColor(0, 0, 255)
            });
            var cell = Cell.FromValue(new TextValue("Colored"));
            cell.StyleId = colorStyle;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

            var act = () => PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            act.Should().NotThrow();
            PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService())
                .Pages.Should().HaveCountGreaterThanOrEqualTo(1);
        });
    }

    [Fact]
    public void RenderWorksheet_BlackAndWhite_ProducesSamePagesAsColorMode()
    {
        // B&W mode should not change the number of pages — only the rendering style.
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("B&W Page Count");
            var sheet = workbook.AddSheet("Sheet1");
            var colorStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 128, 0) });
            for (uint r = 1; r <= 5; r++)
            {
                var c = Cell.FromValue(new TextValue($"Row {r}"));
                c.StyleId = colorStyle;
                sheet.SetCell(new CellAddress(sheet.Id, r, 1), c);
            }

            sheet.PrintBlackAndWhite = false;
            var colorDoc = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            sheet.PrintBlackAndWhite = true;
            var bwDoc = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            bwDoc.Pages.Count.Should().Be(colorDoc.Pages.Count,
                "B&W mode should not affect the page count, only the rendering colours");
        });
    }
}
