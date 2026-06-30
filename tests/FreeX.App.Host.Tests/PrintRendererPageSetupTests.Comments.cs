using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
{
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

            CountColorCommentChromePixels(normalPage).Should().BeGreaterThan(0);
            CountColorCommentChromePixels(draftPage).Should().Be(0);
        });
    }

    [Fact]
    public void RenderWorksheet_AttachesTextOverlaysToDisplayedComments()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Displayed comment overlays");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Printed"));
            sheet.Comments[a1] = "Displayed note PDF text";
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            overlays.Should().ContainEquivalentOf(new
            {
                Text = "Displayed note PDF text",
                FontSize = 9.0,
                Bold = false
            });
        });
    }

    [Fact]
    public void RenderWorksheet_DraftQualitySkipsDisplayedCommentTextOverlays()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Draft comment overlays");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Printed"));
            sheet.Comments[a1] = "Draft hidden note text";
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
            sheet.PrintDraftQuality = true;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);

            overlays.Select(overlay => overlay.Text).Should().NotContain("Draft hidden note text");
        });
    }

    [Fact]
    public void RenderWorksheet_BlackAndWhiteUsesNeutralDisplayedCommentChrome()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Black and white comments");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Printed"));
            sheet.Comments[a1] = "Visible note";
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;

            sheet.PrintBlackAndWhite = false;
            var colorDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var colorPage = colorDocument.Pages[0].GetPageRoot(forceReload: false)!;

            sheet.PrintBlackAndWhite = true;
            var bwDocument = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var bwPage = bwDocument.Pages[0].GetPageRoot(forceReload: false)!;

            CountColorCommentChromePixels(colorPage).Should().BeGreaterThan(0);
            CountColorCommentChromePixels(bwPage).Should().Be(0);
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
    public void RenderWorksheet_IncludesCommentsAtEndPagesInHeaderFooterTotalPages()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Comment total pages");
            var sheet = workbook.AddSheet("Sheet1");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Printed"));
            sheet.Comments[address] = "Summary note";
            sheet.PrintComments = WorksheetPrintComments.AtEnd;
            sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page] of &[Pages]", "");

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var firstPage = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(firstPage)
                .Select(overlay => overlay.Text)
                .ToList();

            document.Pages.Should().HaveCount(2);
            overlayTexts.Should().Contain("Page 1 of 2");
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
    public void RenderWorksheet_AttachesTextOverlaysToCommentSummaryPage()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Comment summary overlays");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b2 = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.Comments[a1] = "Visible note";
            sheet.ThreadedComments[b2] = new ThreadedComment("Review total", "Quinn");
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var summaryPage = document.Pages[1].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(summaryPage);

            overlays.Select(overlay => overlay.Text)
                .Should()
                .ContainInOrder(
                    "Comments",
                    "A1: Visible note",
                    "B2: Quinn: Review total");

            overlays.Should().ContainEquivalentOf(
                new { Text = "Comments", X = 48.0, Y = 48.0, FontSize = 14.0, Bold = true });
            overlays.Should().ContainEquivalentOf(
                new { Text = "A1: Visible note", X = 48.0, Y = 82.0, FontSize = 9.0, Bold = false });
            overlays.Should().ContainEquivalentOf(
                new { Text = "B2: Quinn: Review total", X = 48.0, Y = 100.0, FontSize = 9.0, Bold = false });
        });
    }

    [Fact]
    public void RenderWorksheet_BoundsLongCommentSummaryTextOverlaysToRenderedLines()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Long comment summary overlays");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.Comments[a1] = string.Join(
                " ",
                Enumerable.Repeat("visible-comment-text", 80).Append("hidden-tail-token"));
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var summaryPage = document.Pages[1].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(summaryPage)
                .Select(overlay => overlay.Text)
                .ToList();

            overlays.Should().StartWith("Comments");
            overlays.Where(text => text != "Comments").Should().HaveCount(3);
            overlays.Should().NotContain(text => text.Contains("hidden-tail-token", StringComparison.Ordinal));
            overlays[^1].Should().EndWith("\u2026");
        });
    }

    [Fact]
    public void RenderWorksheet_BoundsMultilineCommentSummaryTextOverlaysToRenderedLines()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Multiline comment summary overlays");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.Comments[a1] = "line one\nline two\nline three\nhidden-tail-token";
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var summaryPage = document.Pages[1].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(summaryPage)
                .Select(overlay => overlay.Text)
                .ToList();

            overlays.Should().ContainInOrder("Comments", "A1: line one", "line two", "line three\u2026");
            overlays.Should().NotContain(text => text.Contains("hidden-tail-token", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RenderWorksheet_BoundsLongUnbrokenCommentSummaryTokenBeforeLaterWords()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Long token comment summary overlays");
            var sheet = workbook.AddSheet("Sheet1");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, new TextValue("Total"));
            sheet.Comments[a1] = $"{new string('x', 400)} hidden-tail-token";
            sheet.PrintComments = WorksheetPrintComments.AtEnd;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var summaryPage = document.Pages[1].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(summaryPage)
                .Select(overlay => overlay.Text)
                .ToList();

            overlays.Should().StartWith("Comments");
            overlays.Should().NotContain(text => text.Contains("hidden-tail-token", StringComparison.Ordinal));
            overlays[^1].Should().EndWith("\u2026");
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
    public void PrintCommentSummaryPlanner_IncludesOverflowComments()
    {
        var sheetId = SheetId.New();
        var comments = Enumerable.Range(1, 90)
            .ToDictionary(
                row => new CellAddress(sheetId, (uint)row, 1),
                row => $"Comment {row}");

        var pages = PrintCommentSummaryPlanner.BuildPages(
            comments,
            new Dictionary<CellAddress, ThreadedComment>(),
            pageHeight: 11 * 96,
            marginTop: 0.75 * 96);

        pages.SelectMany(page => page.Entries)
            .Select(entry => entry.Address.Row)
            .Should()
            .Equal(Enumerable.Range(1, 90).Select(row => (uint)row));
        pages.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void PrintRenderer_DelegatesCommentSummaryPlanningToPresentation()
    {
        var rendererSource = DialogSourceTestSupport.ReadHostSources("PrintRenderer.cs");
        var commentsSource = DialogSourceTestSupport.ReadHostSources("PrintRenderer.Comments.cs");

        rendererSource.Should().Contain("PrintCommentSummaryPlanner.BuildPages(");
        commentsSource.Should().Contain("PrintCommentSummaryPlanner.WrapOverlayText(");
        commentsSource.Should().NotContain(".Chunk(");
        commentsSource.Should().NotContain(".Concat(threadedComments");
        commentsSource.Should().NotContain(".OrderBy(pair => pair.Key.Row)");
        commentsSource.Should().NotContain("result.Sort(static (left, right) =>");
    }

    private static int CountColorCommentChromePixels(FrameworkElement page)
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
}
