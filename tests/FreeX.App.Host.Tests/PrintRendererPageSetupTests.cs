using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

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

    [Fact]
    public void RenderWorksheet_PrintsVisibleChartBitmap()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Chart print");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateChartSource(sheet);
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 8));
            sheet.Charts.Add(CreatePrintedChart(
                sheet,
                "Printable sales chart",
                left: 24,
                top: 24,
                new CellColor(23, 180, 90)));
            var hiddenChart = CreatePrintedChart(
                sheet,
                "Hidden sales chart",
                left: 24,
                top: 24,
                new CellColor(210, 20, 90));
            hiddenChart.IsVisible = false;
            sheet.Charts.Add(hiddenChart);
            sheet.Charts.Add(CreatePrintedChart(
                sheet,
                "Off-page sales chart",
                left: 10000,
                top: 10000,
                new CellColor(25, 40, 230)));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            CountApproximateRgbPixels(page, 23, 180, 90).Should().BeGreaterThan(100);
            CountApproximateRgbPixels(page, 210, 20, 90).Should().Be(0);
            CountApproximateRgbPixels(page, 25, 40, 230).Should().Be(0);
        });
    }

    [Fact]
    public void RenderWorksheet_BoundsLongCellTextOverlaysToVisiblePrintText()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Long cell print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, 1),
                new TextValue("visible prefix worksheet text hidden-tail-token"));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 12));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlays.Should().Contain(text => text.Contains("\u2026", StringComparison.Ordinal));
            overlays.Should().NotContain(text => text.Contains("hidden-tail-token", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RenderWorksheet_DoesNotEllipsizeCellOverlayWhenOnlyTrailingSpacesOverflow()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Trailing space cell print");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("abcdefg  "));
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlays.Should().Contain("abcdefg");
            overlays.Should().NotContain(text => text.Contains("\u2026", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RenderWorksheet_AttachesLinkOverlayForVisibleExternalHyperlinkCellOnly()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Hyperlink print");
            var sheet = workbook.AddSheet("Sheet1");
            var printedAddress = new CellAddress(sheet.Id, 1, 1);
            var outsideSelectionAddress = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(printedAddress, new TextValue("Docs"));
            sheet.SetCell(outsideSelectionAddress, new TextValue("Hidden"));
            sheet.Hyperlinks[printedAddress] = "https://example.com/freex";
            sheet.Hyperlinks[outsideSelectionAddress] = "https://example.com/outside-selection";

            var document = PrintRenderer.RenderWorksheet(
                workbook,
                sheet.Id,
                new ViewportService(),
                printRangeOverride: new GridRange(printedAddress, printedAddress));
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            var overlay = PdfLinkOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
            overlay.Target.Should().Be("https://example.com/freex");
            overlay.X.Should().BeApproximately(sheet.PageMargins.Left * 96.0, 0.01);
            overlay.Y.Should().BeApproximately(sheet.PageMargins.Top * 96.0, 0.01);
            overlay.Width.Should().BeGreaterThan(40);
            overlay.Height.Should().BeApproximately(20.0, 0.01);
        });
    }

    [Fact]
    public void RenderWorksheet_DoesNotAttachLinkOverlayForInternalWorksheetHyperlink()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Internal hyperlink print");
            var sheet = workbook.AddSheet("Sheet1");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Jump"));
            sheet.Hyperlinks[address] = "Sheet1!A10";
            sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
                HyperlinkTargetKind.PlaceInThisDocument);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            PdfLinkOverlayExtractor.Extract(page).Should().BeEmpty();
        });
    }

    [Fact]
    public void RenderWorkbook_PreservesPrintedCellLinkOverlaysWhenCloningBitmapPages()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Workbook hyperlink export");
            var sheet = workbook.AddSheet("Sheet1");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Mail"));
            sheet.Hyperlinks[address] = "mailto:review@example.com";

            var document = PrintRenderer.RenderWorkbook(workbook, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            PdfLinkOverlayExtractor.Extract(page)
                .Should()
                .ContainSingle()
                .Which.Target.Should()
                .Be("mailto:review@example.com");
        });
    }

    [Theory]
    [InlineData(WorksheetPrintErrorValue.Blank, "")]
    [InlineData(WorksheetPrintErrorValue.Dash, "--")]
    [InlineData(WorksheetPrintErrorValue.NotAvailable, "#N/A")]
    public void RenderWorksheet_AppliesPrintErrorOptionsBeforeCellTextOverlays(
        WorksheetPrintErrorValue printErrorValue,
        string expectedOverlayText)
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Printed error overlays");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.DivByZero);
            sheet.PrintErrorValue = printErrorValue;

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            if (expectedOverlayText.Length == 0)
            {
                overlays.Should().NotContain("#DIV/0!");
            }
            else
            {
                overlays.Should().Contain(expectedOverlayText);
                overlays.Should().NotContain("#DIV/0!");
            }
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

    private static void PopulateChartSource(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
    }

    private static ChartModel CreatePrintedChart(
        Sheet sheet,
        string title,
        double left,
        double top,
        CellColor fillColor)
    {
        return new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Title = title,
            Left = left,
            Top = top,
            Width = 260,
            Height = 180,
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: fillColor,
                    StrokeColor: fillColor)
            ]
        };
    }

    private static int CountApproximateRgbPixels(FrameworkElement page, byte expectedRed, byte expectedGreen, byte expectedBlue)
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

            if (Math.Abs(red - expectedRed) <= 3 &&
                Math.Abs(green - expectedGreen) <= 3 &&
                Math.Abs(blue - expectedBlue) <= 3)
            {
                count++;
            }
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

    [Fact]
    public void BuildCommentSummaryPages_AvoidsLinqPagingAndSortingScaffolding()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintRenderer.Comments.cs"));

        source.Should().NotContain(".Chunk(");
        source.Should().NotContain(".Concat(threadedComments");
        source.Should().NotContain(".OrderBy(pair => pair.Key.Row)");
        source.Should().Contain("result.Sort(static (left, right) =>");
    }
}
