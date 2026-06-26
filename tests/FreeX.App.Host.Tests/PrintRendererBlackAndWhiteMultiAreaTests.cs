using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Tests for multi-area print area pagination in the WPF PrintRenderer.
/// Each configured print area should produce at least one page (area→page separation).
/// </summary>
public sealed class PrintRendererMultiAreaTests
{
    [Fact]
    public void RenderWorksheet_MultiAreaPrintArea_ProducesOnePagePerArea()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Multi-Area WPF");
            var sheet = workbook.AddSheet("Sheet1");

            // Put content in two non-overlapping areas.
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Area1"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Area2"));

            var area1 = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 3));
            var area2 = new GridRange(
                new CellAddress(sheet.Id, 1, 5),
                new CellAddress(sheet.Id, 2, 7));
            sheet.SetPrintAreas([area1, area2]);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Count.Should().BeGreaterThanOrEqualTo(2,
                "each configured print area should produce at least one page");
        });
    }

    [Fact]
    public void RenderWorksheet_ThreePrintAreas_ProducesAtLeastThreePages()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Three-Area WPF");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("E1"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 9), new TextValue("I1"));

            sheet.SetPrintAreas([
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
                new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 2, 7)),
                new GridRange(new CellAddress(sheet.Id, 1, 9), new CellAddress(sheet.Id, 2, 11)),
            ]);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Count.Should().BeGreaterThanOrEqualTo(3);
        });
    }

    [Fact]
    public void RenderWorksheet_SinglePrintArea_ProducesOnePage()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Single-Area WPF");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Content"));

            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Should().HaveCount(1);
        });
    }

    [Fact]
    public void RenderWorksheet_NoPrintArea_FallsBackToUsedRange()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("No Print Area");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Only cell"));

            // No print area set.
            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Should().HaveCountGreaterThanOrEqualTo(1);
        });
    }
}
