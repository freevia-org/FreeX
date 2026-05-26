using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public void HeaderFooterPictureLayout_IgnoresPicturesForDraftQuality()
    {
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", 96, 42);

        PrintRenderer.CalculateHeaderFooterLineHeight(
                new WorksheetHeaderFooter("Logo &[Picture]", "", ""),
                new WorksheetHeaderFooterPictureSet(picture, null, null),
                draftQuality: true)
            .Should()
            .Be(18);
    }

    [Fact]
    public void RenderWorksheet_DraftQualitySkipsHeaderFooterPictures()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Draft print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Printed"));
            sheet.PageHeader = new WorksheetHeaderFooter("Logo &[Picture]", "", "");
            sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
                new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 96, 42),
                null,
                null);
            sheet.PrintDraftQuality = true;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Should().HaveCount(1);
        });
    }

    [Fact]
    public void RenderWorksheet_DraftQualitySkipsDisplayedCommentGraphics()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Draft comments");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Printed"));
            sheet.Comments[a1] = "Visible note";
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;

            sheet.PrintDraftQuality = false;
            var normalDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var normalPage = normalDocument.Pages[0].GetPageRoot(forceReload: false)!;

            sheet.PrintDraftQuality = true;
            var draftDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var draftPage = draftDocument.Pages[0].GetPageRoot(forceReload: false)!;

            CountCommentChromePixels(normalPage).Should().BeGreaterThan(0);
            CountCommentChromePixels(draftPage).Should().Be(0);
        });
    }

    [Fact]
    public void RenderWorksheet_DraftQualityKeepsCommentsAtEnd()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Draft comment summary");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Printed"));
            sheet.Comments[a1] = "Visible note";
            sheet.PrintComments = WorksheetPrintComments.AtEnd;
            sheet.PrintDraftQuality = true;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Should().HaveCount(2);
        });
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

    private static int CountCommentChromePixels(FrameworkElement page)
    {
        var width = Math.Max(1, (int)Math.Ceiling(page.Width));
        var height = Math.Max(1, (int)Math.Ceiling(page.Height));
        var size = new Size(width, height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];

            var isCommentIndicator = red > 150 && green < 40 && blue < 40;
            var isCommentFill = red > 240 && green > 240 && blue is >= 190 and < 240;
            if (isCommentIndicator || isCommentFill)
                count++;
        }

        return count;
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

    [Fact]
    public void RenderWorksheet_PrintsCommentsAtEndAcrossMultipleSummaryPages()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Comment overflow print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Total"));
            for (uint row = 1; row <= 90; row++)
            {
                var address = new CellAddress(sheet.Id, row, 1);
                sheet.Comments[address] = $"Comment {row}";
            }
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());

            document.Pages.Count.Should().BeGreaterThan(2);
        });
    }

    [Fact]
    public void BuildCommentSummaryPages_IncludesOverflowComments()
    {
        var sheetId = SheetId.New();
        var comments = Enumerable.Range(1, 90)
            .ToDictionary(
                row => new CellAddress(sheetId, (uint)row, 1),
                row => $"Comment {row}");

        var pages = PrintRenderer.BuildCommentSummaryPages(
            comments,
            new Dictionary<CellAddress, ThreadedComment>(),
            pageH: 11 * 96,
            marginTop: 0.75 * 96);

        pages.SelectMany(page => page)
            .Select(pair => pair.Key.Row)
            .Should()
            .Equal(Enumerable.Range(1, 90).Select(row => (uint)row));
        pages.Count.Should().BeGreaterThan(1);
    }
}
