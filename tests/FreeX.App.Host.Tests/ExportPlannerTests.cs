using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void TryValidatePublishOptions_RejectsUnsupportedPdfA()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfConformance: PdfConformance.PdfA1b);

        ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Pdf, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(UiText.Get("Export_PdfAUnsupportedError"));
    }

    [Fact]
    public void TryValidatePublishOptions_RejectsUnsupportedTaggedPdf()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            IncludeDocumentStructureTags: true);

        ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Pdf, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(UiText.Get("Export_TaggedPdfUnsupportedError"));
    }

    [Fact]
    public void TryValidatePublishOptions_AllowsPdfOnlyChoicesForXpsSummary()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportPlanner.TryValidatePublishOptions(options, ExportFormat.Xps, out var error)
            .Should()
            .BeTrue();

        error.Should().BeNull();
        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should()
            .Be(ExportSummary(
                UiText.Get("Export_ScopeActiveSheet"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesNotIncluded"),
                UiText.Get("Export_PdfAPdfOnlyUnsupported"),
                UiText.Get("Export_TaggedPdfPdfOnlyUnsupported")));
    }

    [Fact]
    public void CreateEffectiveOptionsForFormat_PreservesPdfPublishOptions()
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
            PdfLanguage: " uk_ua ");

        ExportPlanner.CreateEffectiveOptionsForFormat(options, ExportFormat.Pdf)
            .Should()
            .Be(options with { PdfLanguage = "uk-UA" });
    }

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

    [Fact]
    public void ResolveExportSheetIds_ActiveSheetUsesGroupedVisibleSheetsInWorkbookOrder()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var third = workbook.AddSheet("Third");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;

        var result = ExportSheetSelectionPlanner.ResolveSheetIds(
            workbook,
            new ExportOptions(ExportContentScope.ActiveSheet, false, false),
            second.Id,
            [third.Id, hidden.Id, second.Id]);

        result.Should().Equal(second.Id, third.Id);
    }

    [Fact]
    public void DescribeRequest_ExplainsPdfFallbackAndSupportedOptions()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf");

        ExportPlanner.DescribeRequest(request).Should().Be(
            UiText.Format(
                "Export_RequestDescription",
                ExportSummary(
                    UiText.Get("Export_ScopeActiveSheet"),
                    UiText.Get("Export_QualityStandard"),
                    UiText.Get("Export_DocumentPropertiesNotIncluded"))));
    }

    [Fact]
    public void DescribeRequest_ForXpsIncludesDocumentProperties()
    {
        var request = ExportPlanner.PlanExport(
            @"C:\temp\report.xps",
            new ExportOptions(
                ExportContentScope.ActiveSheet,
                IncludeDocumentProperties: true,
                OpenAfterPublish: false));

        ExportPlanner.DescribeRequest(request).Should().Be(
            UiText.Format(
                "Export_RequestDescription",
                ExportSummary(
                    UiText.Get("Export_ScopeActiveSheet"),
                    UiText.Get("Export_QualityStandard"),
                    UiText.Get("Export_DocumentPropertiesIncluded"))));
    }
}
