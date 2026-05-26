using System.Windows;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PrintRendererPageSetupTests
{
    [Fact]
    public void ExpandHeaderFooterText_ExpandsExcelHeaderFooterTokens()
    {
        var now = new DateTime(2026, 5, 22, 13, 45, 0);

        PrintRenderer.ExpandHeaderFooterText(
                "&[Date] &[Time] &[File] &[Path] &[Tab] &[Page]/&[Pages] &D &T &F &Z &A &P/&N &[Picture]",
                pageNumber: 2,
                totalPages: 5,
                workbookName: "Budget.xlsx",
                sheetName: "Summary",
                now)
            .Should()
            .Be($"{now:d} {now:t} Budget.xlsx Budget.xlsx Summary 2/5 {now:d} {now:t} Budget.xlsx Budget.xlsx Summary 2/5 ");
    }

    [Fact]
    public void ExpandHeaderFooterText_RemovesPictureTokensSoRendererCanDrawImages()
    {
        PrintRenderer.ExpandHeaderFooterText(
                "Logo &[Picture] &G",
                pageNumber: 1,
                totalPages: 1,
                workbookName: "Book.xlsx",
                sheetName: "Sheet1",
                new DateTime(2026, 5, 22))
            .Should()
            .Be("Logo  ");
    }

    [Fact]
    public void HeaderFooterPictureLayout_ReservesPictureHeightAndSideTextSpace()
    {
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 96, 42);
        var header = new WorksheetHeaderFooter("Logo &[Picture]", "", "");
        var pictures = new WorksheetHeaderFooterPictureSet(picture, null, null);
        var section = new Rect(24, 10, 200, PrintRenderer.CalculateHeaderFooterLineHeight(header, pictures));

        PrintRenderer.CalculateHeaderFooterLineHeight(header, pictures).Should().Be(42);
        PrintRenderer.CalculateHeaderFooterPictureRect(picture, section, TextAlignment.Left)
            .Should()
            .Be(new Rect(26, 10, 96, 42));
        PrintRenderer.CalculateHeaderFooterTextRect(section, picture, TextAlignment.Left)
            .Should()
            .Be(new Rect(124, 10, 100, 42));
    }

    [Fact]
    public void HeaderFooterPictureLayout_IgnoresPicturesWithoutPictureTokens()
    {
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", 96, 42);

        PrintRenderer.CalculateHeaderFooterLineHeight(
                new WorksheetHeaderFooter("Logo", "", ""),
                new WorksheetHeaderFooterPictureSet(picture, null, null))
            .Should()
            .Be(18);
    }

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

    [Fact]
    public void RenderWorksheet_PrintsThreadedCommentsAtEnd()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Threaded comment print");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.ThreadedComments[a1] = new ThreadedComment("Review total", "Quinn");
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Should().HaveCount(2);
        });
    }
}
