using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
{
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
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, 2),
                new TextValue("Overflow blocker"));
            sheet.ColumnWidths[1] = 12.0;
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2));

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

    [Theory]
    [InlineData("Sheet1", "A10", 10u, 1u)]
    [InlineData("Sheet1", "Sheet1!A10", 10u, 1u)]
    [InlineData("Q1 Summary", "'Q1 Summary'!B2", 2u, 2u)]
    public void RenderWorksheet_AttachesLinkOverlayForInternalWorksheetHyperlink(
        string sheetName,
        string target,
        uint targetRow,
        uint targetCol)
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Internal hyperlink print");
            var sheet = workbook.AddSheet(sheetName);
            var address = new CellAddress(sheet.Id, 1, 1);
            var targetAddress = new CellAddress(sheet.Id, targetRow, targetCol);
            sheet.SetCell(address, new TextValue("Jump"));
            sheet.SetCell(targetAddress, new TextValue("Target"));
            sheet.PrintArea = new GridRange(
                address,
                targetAddress);
            sheet.Hyperlinks[address] = target;
            sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
                HyperlinkTargetKind.PlaceInThisDocument);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            var overlay = PdfLinkOverlayExtractor.Extract(page).Should().ContainSingle().Subject;
            overlay.Target.Should().Be(target);
            overlay.TargetKind.Should().Be(HyperlinkTargetKind.PlaceInThisDocument);
            overlay.SourceAddress.Should().Be(address);
            overlay.TargetAddress.Should().Be(targetAddress);

            PdfCellDestinationOverlayExtractor.Extract(page)
                .Should()
                .ContainSingle()
                .Which.Address.Should()
                .Be(targetAddress);
        });
    }

    [Fact]
    public void RenderWorksheet_SkipsLinkOverlayForUnsupportedInternalWorksheetHyperlink()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Unsupported internal hyperlink print");
            var sheet = workbook.AddSheet("Sheet1");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Jump"));
            sheet.Hyperlinks[address] = "Missing Sheet!A10";
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
}
