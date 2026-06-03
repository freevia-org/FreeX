using System.Windows;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
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
    public void RenderWorksheet_AttachesTextOverlaysToHeaderFooterText()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("HeaderFooterBook.xlsx");
            var sheet = workbook.AddSheet("Summary");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Printed"));
            sheet.PageHeader = new WorksheetHeaderFooter(
                "Left page &[Page]",
                "Center pages &[Pages]",
                "Right file &[File] &[Picture]");
            sheet.PageFooter = new WorksheetHeaderFooter(
                "Left tab &[Tab]",
                "Center footer",
                "Right footer");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlayTexts.Should().Contain("Left page 1");
            overlayTexts.Should().Contain("Center pages 1");
            overlayTexts.Should().Contain("Right file HeaderFooterBook.xlsx");
            overlayTexts.Should().Contain("Left tab Summary");
            overlayTexts.Should().Contain("Center footer");
            overlayTexts.Should().Contain("Right footer");
            overlayTexts.Should().NotContain(text => text.Contains("&[Picture]", StringComparison.Ordinal));
            overlayTexts.Should().NotContain(text => text.Contains("&G", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RenderWorksheet_BoundsLongHeaderFooterTextOverlaysToVisiblePrintText()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Long header print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Printed"));
            sheet.PageHeader = new WorksheetHeaderFooter(
                $"{new string('x', 300)} hidden-tail-token",
                "",
                "");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlayTexts.Should().Contain(text => text.EndsWith("\u2026", StringComparison.Ordinal));
            overlayTexts.Should().NotContain(text => text.Contains("hidden-tail-token", StringComparison.Ordinal));
        });
    }
}
