using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ExportDescriptionPlannerTests
{
    private static readonly ExportPlannerTextResolver Text = new(
        key => key == "Export_OptionsSeparator" ? " | " : $"<{key}>",
        (key, args) => key switch
        {
            "Export_OptionsSentence" => $"Options[{args[0]}]",
            "Export_RequestDescription" => $"Request[{args[0]}]",
            "Export_RequestDescriptionWithFallback" => $"Fallback[{args[0]}::{args[1]}]",
            "Export_PageRangeMultiple" => $"Pages[{args[0]}-{args[1]}]",
            "Export_PageRangeSingle" => $"Page[{args[0]}]",
            "Export_PdfLanguage" => $"Language[{args[0]}]",
            _ => $"<{key}>({string.Join(",", args)})"
        });

    [Fact]
    public void DescribeOptions_UsesSharedResolverForPdfOptionSummary()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true,
            IgnorePrintAreas: true,
            PageRange: new ExportPageRange(2, 4),
            Quality: ExportQuality.MinimumSize,
            BookmarkMode: PdfBookmarkMode.PageNumbers,
            InitialView: PdfInitialView.TwoColumnRight,
            OpenMode: PdfOpenMode.Outlines,
            BitmapTextWhenFontsMayNotBeEmbedded: true,
            PdfLanguage: "uk-UA",
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportDescriptionPlanner.DescribeOptions(options, Text)
            .Should()
            .Be("Options[<Export_ScopeSelection> | Pages[2-4] | <Export_QualityMinimumSize> | <Export_PrintAreasIgnored> | <Export_InitialViewTwoColumnRight> | <Export_OpenModeOutlines> | <Export_DocumentPropertiesIncluded> | <Export_BookmarksPageNumbers> | <Export_BitmapTextWhenFontsMayNotBeEmbedded> | Language[uk-UA] | <Export_PdfANotSupported> | <Export_TaggedPdfNotSupported> | <Export_OpenAfterPublishing>]");
    }

    [Fact]
    public void DescribeOptions_ExplainsPdfOnlyChoicesWhenTargetIsXps()
    {
        var options = new ExportOptions(
            ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false,
            Quality: ExportQuality.MinimumSize,
            BookmarkMode: PdfBookmarkMode.PrintTitles,
            InitialView: PdfInitialView.OneColumn,
            OpenMode: PdfOpenMode.FullScreen,
            BitmapTextWhenFontsMayNotBeEmbedded: true,
            PdfLanguage: "uk-UA",
            PdfConformance: PdfConformance.PdfA1b,
            IncludeDocumentStructureTags: true);

        ExportDescriptionPlanner.DescribeOptions(options, ExportFormat.Xps, Text)
            .Should()
            .Be("Options[<Export_ScopeEntireWorkbook> | <Export_QualityMinimumSizePdfOnly> | <Export_InitialViewPdfOnly> | <Export_OpenModePdfOnly> | <Export_DocumentPropertiesNotIncluded> | <Export_BookmarksPdfOnly> | <Export_BitmapTextPdfOnly> | <Export_PdfLanguagePdfOnly> | <Export_PdfAPdfOnlyUnsupported> | <Export_TaggedPdfPdfOnlyUnsupported>]");
    }

    [Fact]
    public void DescribeRequest_UsesFallbackAndRequestTextFromResolver()
    {
        var request = new ExportRequest(
            @"C:\temp\report.pdf",
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            FallbackPath: @"C:\temp\report.xps");

        ExportDescriptionPlanner.DescribeRequest(request, Text)
            .Should()
            .Be("Fallback[<Export_PdfFallbackMessage>::Options[<Export_ScopeActiveSheet> | <Export_QualityStandard> | <Export_DocumentPropertiesNotIncluded>]]");
    }
}
