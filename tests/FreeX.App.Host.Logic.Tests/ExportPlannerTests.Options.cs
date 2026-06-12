using FluentAssertions;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    [Fact]
    public void ExportOptions_DefaultsToActiveSheetWithoutDocumentProperties()
    {
        ExportOptions.ExcelLikeDefault.Should().Be(new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            IgnorePrintAreas: false,
            Quality: ExportQuality.Standard));

        ExportPlanner.DescribeOptions(ExportOptions.ExcelLikeDefault)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeActiveSheet"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesNotIncluded")));
    }

    [Fact]
    public void ExportOptions_DescribeSelectionAndOpenAfterPublish()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true,
            IgnorePrintAreas: true,
            PageRange: new ExportPageRange(2, 4),
            Quality: ExportQuality.MinimumSize,
            BookmarkMode: PdfBookmarkMode.SheetNames,
            BitmapTextWhenFontsMayNotBeEmbedded: true);

        ExportPlanner.DescribeOptions(options)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeSelection"),
                UiText.Format("Export_PageRangeMultiple", 2, 4),
                UiText.Get("Export_QualityMinimumSize"),
                UiText.Get("Export_PrintAreasIgnored"),
                UiText.Get("Export_DocumentPropertiesIncluded"),
                UiText.Get("Export_BookmarksSheetNames"),
                UiText.Get("Export_BitmapTextWhenFontsMayNotBeEmbedded"),
                UiText.Get("Export_OpenAfterPublishing")));
    }

    [Theory]
    [InlineData("sheet", "Export_BookmarksSheetNames")]
    [InlineData("print-title", "Export_BookmarksPrintTitles")]
    [InlineData("page-number", "Export_BookmarksPageNumbers")]
    public void ExportOptions_DescribePdfBookmarkModes(string mode, string expectedPartKey)
    {
        var bookmarkMode = mode switch
        {
            "print-title" => PdfBookmarkMode.PrintTitles,
            "page-number" => PdfBookmarkMode.PageNumbers,
            _ => PdfBookmarkMode.SheetNames
        };
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: true,
            OpenAfterPublish: false,
            BookmarkMode: bookmarkMode);

        ExportPlanner.DescribeOptions(options)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeEntireWorkbook"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesIncluded"),
                UiText.Get(expectedPartKey)));
    }

    [Fact]
    public void ExportOptions_DescribePdfInitialViewAndOpenMode()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            InitialView: PdfInitialView.OneColumn,
            OpenMode: PdfOpenMode.FullScreen);

        ExportPlanner.DescribeOptions(options)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeActiveSheet"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_InitialViewOneColumn"),
                UiText.Get("Export_OpenModeFullScreen"),
                UiText.Get("Export_DocumentPropertiesNotIncluded")));
    }

    [Fact]
    public void ExportOptions_DescribePdfLanguageWhenNotDefault()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfLanguage: "uk-UA");

        ExportPlanner.DescribeOptions(options)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeActiveSheet"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesNotIncluded"),
                UiText.Format("Export_PdfLanguage", "uk-UA")));
    }

    [Fact]
    public void ExportOptions_DescribeUnsupportedPdfPublishOptions()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportPlanner.DescribeOptions(options)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeActiveSheet"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesNotIncluded"),
                UiText.Get("Export_PdfANotSupported"),
                UiText.Get("Export_TaggedPdfNotSupported")));
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatIncludesDocumentProperties()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeSelection"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesIncluded"),
                UiText.Get("Export_OpenAfterPublishing")));
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyBookmarks()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: true,
            OpenAfterPublish: false,
            BookmarkMode: PdfBookmarkMode.PrintTitles);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeEntireWorkbook"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesIncluded"),
                UiText.Get("Export_BookmarksPdfOnly")));
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyLanguage()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            PdfLanguage: "uk-UA");

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeEntireWorkbook"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesNotIncluded"),
                UiText.Get("Export_PdfLanguagePdfOnly")));
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyViewOptions()
    {
        var options = new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            InitialView: PdfInitialView.TwoColumnLeft,
            OpenMode: PdfOpenMode.FullScreen);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeActiveSheet"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_InitialViewPdfOnly"),
                UiText.Get("Export_OpenModePdfOnly"),
                UiText.Get("Export_DocumentPropertiesNotIncluded")));
    }

    [Fact]
    public void ExportOptions_DescribeWithXpsFormatExplainsPdfOnlyMinimumSize()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            Quality: ExportQuality.MinimumSize);

        ExportPlanner.DescribeOptions(options, ExportFormat.Xps)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeSelection"),
                UiText.Get("Export_QualityMinimumSizePdfOnly"),
                UiText.Get("Export_DocumentPropertiesNotIncluded")));
    }

    [Fact]
    public void ExportOptions_DescribeEntireWorkbook()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false);

        ExportPlanner.DescribeOptions(options)
            .Should().Be(ExportSummary(
                UiText.Get("Export_ScopeEntireWorkbook"),
                UiText.Get("Export_QualityStandard"),
                UiText.Get("Export_DocumentPropertiesNotIncluded")));
    }
}
