namespace FreeX.App.Host;

internal static partial class ExportPlanner
{
    public static string DescribeOptions(ExportOptions options)
    {
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => UiText.Get("Export_ScopeActiveSheet"),
            ExportContentScope.Selection => UiText.Get("Export_ScopeSelection"),
            ExportContentScope.EntireWorkbook => UiText.Get("Export_ScopeEntireWorkbook"),
            _ => UiText.Get("Export_ScopeActiveSheet")
        };
        var pageRange = options.PageRange is null
            ? null
            : options.PageRange.ToString();
        var quality = DescribeQuality(options.Quality);
        var printAreas = options.IgnorePrintAreas
            ? UiText.Get("Export_PrintAreasIgnored")
            : null;
        var initialView = DescribeInitialView(options.InitialView);
        var openMode = DescribeOpenMode(options.OpenMode);
        var properties = options.IncludeDocumentProperties
            ? UiText.Get("Export_DocumentPropertiesIncluded")
            : UiText.Get("Export_DocumentPropertiesNotIncluded");
        var bookmarks = DescribeBookmarkMode(options.EffectiveBookmarkMode, ExportFormat.Pdf);
        var bitmapText = options.BitmapTextWhenFontsMayNotBeEmbedded
            ? UiText.Get("Export_BitmapTextWhenFontsMayNotBeEmbedded")
            : null;
        var language = DescribePdfLanguage(options.PdfLanguage, ExportFormat.Pdf);
        var conformance = DescribePdfConformance(options.PdfConformance, ExportFormat.Pdf);
        var tags = DescribeDocumentStructureTags(options.IncludeDocumentStructureTags, ExportFormat.Pdf);
        var open = options.OpenAfterPublish
            ? UiText.Get("Export_OpenAfterPublishing")
            : null;

        return JoinOptionParts(scope, pageRange, quality, printAreas, initialView, openMode, properties, bookmarks, bitmapText, language, conformance, tags, open);
    }

    public static string DescribeOptions(ExportOptions options, ExportFormat format) =>
        DescribeOptionsForFormat(options, format);

    public static string DescribeRequest(ExportRequest request)
    {
        var options = DescribeOptionsForFormat(request.Options, request.Format);
        return request.UsesXpsFallback
            ? UiText.Format("Export_RequestDescriptionWithFallback", PdfFallbackMessage, options)
            : UiText.Format("Export_RequestDescription", options);
    }

    private static string DescribeOptionsForFormat(ExportOptions options, ExportFormat format)
    {
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => UiText.Get("Export_ScopeActiveSheet"),
            ExportContentScope.Selection => UiText.Get("Export_ScopeSelection"),
            ExportContentScope.EntireWorkbook => UiText.Get("Export_ScopeEntireWorkbook"),
            _ => UiText.Get("Export_ScopeActiveSheet")
        };
        var pageRange = options.PageRange is null
            ? null
            : options.PageRange.ToString();
        var quality = DescribeQualityForFormat(options.Quality, format);
        var printAreas = options.IgnorePrintAreas
            ? UiText.Get("Export_PrintAreasIgnored")
            : null;
        var initialView = DescribeInitialViewForFormat(options.InitialView, format);
        var openMode = DescribeOpenModeForFormat(options.OpenMode, format);
        var properties = (options.IncludeDocumentProperties, format) switch
        {
            (true, ExportFormat.Pdf) => UiText.Get("Export_DocumentPropertiesIncluded"),
            (true, ExportFormat.Xps) => UiText.Get("Export_DocumentPropertiesIncluded"),
            _ => UiText.Get("Export_DocumentPropertiesNotIncluded")
        };
        var bookmarks = DescribeBookmarkMode(options.EffectiveBookmarkMode, format);
        var bitmapText = DescribeBitmapTextOption(options.BitmapTextWhenFontsMayNotBeEmbedded, format);
        var language = DescribePdfLanguage(options.PdfLanguage, format);
        var conformance = DescribePdfConformance(options.PdfConformance, format);
        var tags = DescribeDocumentStructureTags(options.IncludeDocumentStructureTags, format);
        var open = options.OpenAfterPublish
            ? UiText.Get("Export_OpenAfterPublishing")
            : null;

        return JoinOptionParts(scope, pageRange, quality, printAreas, initialView, openMode, properties, bookmarks, bitmapText, language, conformance, tags, open);
    }


    private static string JoinOptionParts(params string?[] parts) =>
        UiText.Format(
            "Export_OptionsSentence",
            string.Join(UiText.Get("Export_OptionsSeparator"), parts.Where(part => !string.IsNullOrWhiteSpace(part))));

    private static string DescribeQuality(ExportQuality quality) =>
        quality == ExportQuality.MinimumSize
            ? UiText.Get("Export_QualityMinimumSize")
            : UiText.Get("Export_QualityStandard");

    private static string DescribeQualityForFormat(ExportQuality quality, ExportFormat format) =>
        quality == ExportQuality.MinimumSize && format == ExportFormat.Xps
            ? UiText.Get("Export_QualityMinimumSizePdfOnly")
            : DescribeQuality(quality);

    private static string? DescribeBookmarkMode(PdfBookmarkMode bookmarkMode, ExportFormat format)
    {
        if (bookmarkMode == PdfBookmarkMode.None)
            return null;

        if (format == ExportFormat.Xps)
            return UiText.Get("Export_BookmarksPdfOnly");

        return bookmarkMode switch
        {
            PdfBookmarkMode.PrintTitles => UiText.Get("Export_BookmarksPrintTitles"),
            PdfBookmarkMode.PageNumbers => UiText.Get("Export_BookmarksPageNumbers"),
            _ => UiText.Get("Export_BookmarksSheetNames")
        };
    }

    private static string? DescribeBitmapTextOption(bool bitmapTextWhenFontsMayNotBeEmbedded, ExportFormat format)
    {
        if (!bitmapTextWhenFontsMayNotBeEmbedded)
            return null;

        return format == ExportFormat.Xps
            ? UiText.Get("Export_BitmapTextPdfOnly")
            : UiText.Get("Export_BitmapTextWhenFontsMayNotBeEmbedded");
    }

    private static string? DescribePdfLanguage(string? pdfLanguage, ExportFormat format)
    {
        var normalized = NormalizePdfLanguage(pdfLanguage);
        if (string.Equals(normalized, DefaultPdfLanguage, StringComparison.OrdinalIgnoreCase))
            return null;

        return format == ExportFormat.Xps
            ? UiText.Get("Export_PdfLanguagePdfOnly")
            : UiText.Format("Export_PdfLanguage", normalized);
    }

    private static string? DescribePdfConformance(PdfConformance conformance, ExportFormat format)
    {
        if (conformance == PdfConformance.Standard)
            return null;

        return format == ExportFormat.Xps
            ? UiText.Get("Export_PdfAPdfOnlyUnsupported")
            : UiText.Get("Export_PdfANotSupported");
    }

    private static string? DescribeDocumentStructureTags(bool includeDocumentStructureTags, ExportFormat format)
    {
        if (!includeDocumentStructureTags)
            return null;

        return format == ExportFormat.Xps
            ? UiText.Get("Export_TaggedPdfPdfOnlyUnsupported")
            : UiText.Get("Export_TaggedPdfNotSupported");
    }

    private static string? DescribeInitialView(PdfInitialView initialView) =>
        initialView switch
        {
            PdfInitialView.OneColumn => UiText.Get("Export_InitialViewOneColumn"),
            PdfInitialView.TwoColumnLeft => UiText.Get("Export_InitialViewTwoColumnLeft"),
            PdfInitialView.TwoColumnRight => UiText.Get("Export_InitialViewTwoColumnRight"),
            _ => null
        };

    private static string? DescribeInitialViewForFormat(PdfInitialView initialView, ExportFormat format)
    {
        if (initialView == PdfInitialView.SinglePage)
            return null;

        return format == ExportFormat.Xps
            ? UiText.Get("Export_InitialViewPdfOnly")
            : DescribeInitialView(initialView);
    }

    private static string? DescribeOpenMode(PdfOpenMode openMode) =>
        openMode switch
        {
            PdfOpenMode.Outlines => UiText.Get("Export_OpenModeOutlines"),
            PdfOpenMode.FullScreen => UiText.Get("Export_OpenModeFullScreen"),
            _ => null
        };

    private static string? DescribeOpenModeForFormat(PdfOpenMode openMode, ExportFormat format)
    {
        if (openMode == PdfOpenMode.Normal)
            return null;

        return format == ExportFormat.Xps
            ? UiText.Get("Export_OpenModePdfOnly")
            : DescribeOpenMode(openMode);
    }
}
