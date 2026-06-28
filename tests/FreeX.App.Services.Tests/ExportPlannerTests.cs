using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ExportPlannerTests
{
    [Fact]
    public void CreateEffectiveOptionsForFormat_ClearsPdfOnlyChoicesForXps()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true,
            IgnorePrintAreas: true,
            PageRange: new ExportPageRange(2, 3),
            Quality: ExportQuality.MinimumSize,
            CreateBookmarks: true,
            BookmarkMode: PdfBookmarkMode.PageNumbers,
            InitialView: PdfInitialView.TwoColumnRight,
            OpenMode: PdfOpenMode.Outlines,
            BitmapTextWhenFontsMayNotBeEmbedded: true,
            PdfLanguage: "uk-UA",
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Xps)
            .Should()
            .Be(new ExportOptions(
                ExportContentScope.Selection,
                IncludeDocumentProperties: true,
                OpenAfterPublish: true,
                IgnorePrintAreas: true,
                PageRange: new ExportPageRange(2, 3),
                Quality: ExportQuality.Standard,
                CreateBookmarks: false,
                BookmarkMode: PdfBookmarkMode.None,
                InitialView: PdfInitialView.SinglePage,
                OpenMode: PdfOpenMode.Normal,
                BitmapTextWhenFontsMayNotBeEmbedded: false,
                PdfLanguage: ExportPlanner.DefaultPdfLanguage,
                PdfConformance: PdfConformance.Standard,
                IncludeDocumentStructureTags: false));
    }

    [Theory]
    [InlineData("", "", true, null, null)]
    [InlineData("2", "4", true, 2, 4)]
    [InlineData("0", "3", false, null, null)]
    [InlineData("4", "2", false, null, null)]
    [InlineData("x", "2", false, null, null)]
    public void TryCreatePageRange_ValidatesOptionalOneBasedPageRange(
        string fromText,
        string toText,
        bool expectedSuccess,
        int? expectedFrom,
        int? expectedTo)
    {
        var success = ExportPlanner.TryCreatePageRange(fromText, toText, out var range, out var error);

        success.Should().Be(expectedSuccess);
        if (expectedSuccess && expectedFrom is not null && expectedTo is not null)
        {
            range.Should().Be(new ExportPageRange(expectedFrom.Value, expectedTo.Value));
            error.Should().BeNull();
        }
        else if (expectedSuccess)
        {
            range.Should().BeNull();
            error.Should().BeNull();
        }
        else
        {
            range.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void ResolveSheetIds_ActiveSheetUsesGroupedVisibleSheetsInWorkbookOrder()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var third = workbook.AddSheet("Third");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;

        var result = WorkbookExportSheetSelectionPlanner.ResolveSheetIds(
            workbook,
            new ExportOptions(ExportContentScope.ActiveSheet, false, false),
            second.Id,
            [third.Id, hidden.Id, second.Id]);

        result.Should().Equal(second.Id, third.Id);
    }
}
