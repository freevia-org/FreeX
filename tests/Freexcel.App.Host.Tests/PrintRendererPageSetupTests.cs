using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PrintRendererPageSetupTests
{
    [Fact]
    public void RenderWorksheet_UsesLandscapeLetterPageSetupForExport()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Print setup");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.PaperSize = WorksheetPaperSize.Letter;
            sheet.PageOrientation = WorksheetPageOrientation.Landscape;
            sheet.PageMargins = new WorksheetPageMargins(0.25, 0.75, 0.5, 1.0);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Printed"));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.DocumentPaginator.PageSize.Width.Should().BeGreaterThan(document.DocumentPaginator.PageSize.Height);
            document.DocumentPaginator.PageSize.Width.Should().BeApproximately(11.0 * 96.0, 0.01);
            document.DocumentPaginator.PageSize.Height.Should().BeApproximately(8.5 * 96.0, 0.01);
            document.Pages.Should().HaveCount(1);
        });
    }

    [Fact]
    public void RenderWorksheet_UsesExplicitPrintRangeForSelectionExport()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Selection export");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Outside"));
            sheet.SetCell(new CellAddress(sheet.Id, 40, 20), new TextValue("Selected"));
            var selectedRange = new GridRange(
                new CellAddress(sheet.Id, 40, 20),
                new CellAddress(sheet.Id, 40, 20));

            var document = PrintRenderer.RenderWorksheet(
                workbook,
                sheet.Id,
                new ViewportService(),
                printRangeOverride: selectedRange);

            document.Pages.Should().HaveCount(1);
        });
    }
}
