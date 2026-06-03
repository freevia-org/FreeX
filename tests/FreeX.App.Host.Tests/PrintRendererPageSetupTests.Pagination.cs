using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
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

    [Fact]
    public void RenderWorksheet_CanIgnoreConfiguredPrintAreaForExport()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Ignore print area");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Inside print area"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 80), new TextValue("Outside print area"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var printAreaDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var ignoredPrintAreaDocument = PrintRenderer.RenderWorksheet(
                workbook,
                sheet.Id,
                new ViewportService(),
                ignorePrintArea: true);

            printAreaDocument.Pages.Should().HaveCount(1);
            ignoredPrintAreaDocument.Pages.Count.Should().BeGreaterThan(1);
        });
    }

    [Fact]
    public void RenderWorksheet_UsesManualRowPageBreaksForPrintPagination()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Manual row break");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Top"));
            sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("Bottom"));

            var automaticDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            sheet.RowPageBreaks.Add(6);
            var manualBreakDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            automaticDocument.Pages.Should().HaveCount(1);
            manualBreakDocument.Pages.Should().HaveCount(2);
        });
    }

    [Fact]
    public void RenderWorksheet_UsesManualColumnPageBreaksForPrintPagination()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Manual column break");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Left"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 10), new TextValue("Right"));

            var automaticDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            sheet.ColumnPageBreaks.Add(6);
            var manualBreakDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            automaticDocument.Pages.Should().HaveCount(1);
            manualBreakDocument.Pages.Should().HaveCount(2);
        });
    }

    [Fact]
    public void RenderWorksheet_UsesScalePercentForPrintPagination()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Scaled print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Top"));
            sheet.SetCell(new CellAddress(sheet.Id, 80, 1), new TextValue("Bottom"));

            var defaultScaleDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            sheet.ScaleToFit = new WorksheetScaleToFit(50, null, null);
            var scaledDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            defaultScaleDocument.Pages.Should().HaveCount(2);
            scaledDocument.Pages.Should().HaveCount(1);
        });
    }

    [Fact]
    public void RenderWorksheet_UsesFitToPagesWideForPrintPagination()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Fit wide print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Left"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 80), new TextValue("Right"));

            var defaultScaleDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, null);
            var fitWideDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            defaultScaleDocument.Pages.Count.Should().BeGreaterThan(1);
            fitWideDocument.Pages.Should().HaveCount(1);
        });
    }

    [Fact]
    public void RenderWorkbook_CombinesVisibleWorksheetsAndSkipsHiddenSheets()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Workbook export");
            var first = workbook.AddSheet("Sheet1");
            var hidden = workbook.AddSheet("Hidden");
            var second = workbook.AddSheet("Sheet2");
            first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("One"));
            hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("Hidden"));
            second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("Two"));
            hidden.IsHidden = true;

            var document = PrintRenderer.RenderWorkbook(workbook, new ViewportService());
            var paginator = PrintRenderer.CreateWorkbookPaginator(workbook, new ViewportService());

            document.Pages.Should().HaveCount(2);
            paginator.PageCount.Should().Be(2);
        });
    }
}
