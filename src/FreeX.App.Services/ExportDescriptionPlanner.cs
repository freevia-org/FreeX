namespace FreeX.App.Services;

public static class ExportDescriptionPlanner
{
    public static string PdfFallbackMessage(ExportPlannerTextResolver? textResolver = null) =>
        ResolveText(textResolver).Get("Export_PdfFallbackMessage");

    public static string DescribeOptions(
        ExportOptions options,
        ExportPlannerTextResolver? textResolver = null) =>
        DescribeOptions(options, ExportFormat.Pdf, textResolver);

    public static string DescribeOptions(
        ExportOptions options,
        ExportFormat format,
        ExportPlannerTextResolver? textResolver = null)
    {
        var text = ResolveText(textResolver);
        var scope = options.Scope switch
        {
            ExportContentScope.ActiveSheet => text.Get("Export_ScopeActiveSheet"),
            ExportContentScope.Selection => text.Get("Export_ScopeSelection"),
            ExportContentScope.EntireWorkbook => text.Get("Export_ScopeEntireWorkbook"),
            _ => text.Get("Export_ScopeActiveSheet")
        };
        var pageRange = options.PageRange is null
            ? null
            : ExportPlanner.FormatPageRange(options.PageRange, text);
        var quality = DescribeQualityForFormat(options.Quality, format, text);
        var printAreas = options.IgnorePrintAreas
            ? text.Get("Export_PrintAreasIgnored")
            : null;
        var initialView = DescribeInitialViewForFormat(options.InitialView, format, text);
        var openMode = DescribeOpenModeForFormat(options.OpenMode, format, text);
        var properties = options.IncludeDocumentProperties
            ? text.Get("Export_DocumentPropertiesIncluded")
            : text.Get("Export_DocumentPropertiesNotIncluded");
        var bookmarks = DescribeBookmarkMode(options.EffectiveBookmarkMode, format, text);
        var bitmapText = DescribeBitmapTextOption(options.BitmapTextWhenFontsMayNotBeEmbedded, format, text);
        var language = DescribePdfLanguage(options.PdfLanguage, format, text);
        var conformance = DescribePdfConformance(options.PdfConformance, format, text);
        var tags = DescribeDocumentStructureTags(options.IncludeDocumentStructureTags, format, text);
        var open = options.OpenAfterPublish
            ? text.Get("Export_OpenAfterPublishing")
            : null;

        return JoinOptionParts(text, scope, pageRange, quality, printAreas, initialView, openMode, properties, bookmarks, bitmapText, language, conformance, tags, open);
    }

    public static string DescribeRequest(
        ExportRequest request,
        ExportPlannerTextResolver? textResolver = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = ResolveText(textResolver);
        var options = DescribeOptions(request.Options, request.Format, text);
        return request.UsesXpsFallback
            ? text.Format("Export_RequestDescriptionWithFallback", PdfFallbackMessage(text), options)
            : text.Format("Export_RequestDescription", options);
    }

    private static ExportPlannerTextResolver ResolveText(ExportPlannerTextResolver? textResolver) =>
        textResolver ?? ExportPlannerTextResolver.InvariantEnglish;

    private static string JoinOptionParts(ExportPlannerTextResolver text, params string?[] parts) =>
        text.Format(
            "Export_OptionsSentence",
            string.Join(text.Get("Export_OptionsSeparator"), parts.Where(part => !string.IsNullOrWhiteSpace(part))));

    private static string DescribeQuality(ExportQuality quality, ExportPlannerTextResolver text) =>
        quality == ExportQuality.MinimumSize
            ? text.Get("Export_QualityMinimumSize")
            : text.Get("Export_QualityStandard");

    private static string DescribeQualityForFormat(
        ExportQuality quality,
        ExportFormat format,
        ExportPlannerTextResolver text) =>
        quality == ExportQuality.MinimumSize && format == ExportFormat.Xps
            ? text.Get("Export_QualityMinimumSizePdfOnly")
            : DescribeQuality(quality, text);

    private static string? DescribeBookmarkMode(
        PdfBookmarkMode bookmarkMode,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        if (bookmarkMode == PdfBookmarkMode.None)
            return null;

        if (format == ExportFormat.Xps)
            return text.Get("Export_BookmarksPdfOnly");

        return bookmarkMode switch
        {
            PdfBookmarkMode.PrintTitles => text.Get("Export_BookmarksPrintTitles"),
            PdfBookmarkMode.PageNumbers => text.Get("Export_BookmarksPageNumbers"),
            _ => text.Get("Export_BookmarksSheetNames")
        };
    }

    private static string? DescribeBitmapTextOption(
        bool bitmapTextWhenFontsMayNotBeEmbedded,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        if (!bitmapTextWhenFontsMayNotBeEmbedded)
            return null;

        return format == ExportFormat.Xps
            ? text.Get("Export_BitmapTextPdfOnly")
            : text.Get("Export_BitmapTextWhenFontsMayNotBeEmbedded");
    }

    private static string? DescribePdfLanguage(
        string? pdfLanguage,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        var normalized = ExportPlanner.NormalizePdfLanguage(pdfLanguage);
        if (string.Equals(normalized, ExportPlanner.DefaultPdfLanguage, StringComparison.OrdinalIgnoreCase))
            return null;

        return format == ExportFormat.Xps
            ? text.Get("Export_PdfLanguagePdfOnly")
            : text.Format("Export_PdfLanguage", normalized);
    }

    private static string? DescribePdfConformance(
        PdfConformance conformance,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        if (conformance == PdfConformance.Standard)
            return null;

        return format == ExportFormat.Xps
            ? text.Get("Export_PdfAPdfOnlyUnsupported")
            : text.Get("Export_PdfANotSupported");
    }

    private static string? DescribeDocumentStructureTags(
        bool includeDocumentStructureTags,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        if (!includeDocumentStructureTags)
            return null;

        return format == ExportFormat.Xps
            ? text.Get("Export_TaggedPdfPdfOnlyUnsupported")
            : text.Get("Export_TaggedPdfNotSupported");
    }

    private static string? DescribeInitialView(PdfInitialView initialView, ExportPlannerTextResolver text) =>
        initialView switch
        {
            PdfInitialView.OneColumn => text.Get("Export_InitialViewOneColumn"),
            PdfInitialView.TwoColumnLeft => text.Get("Export_InitialViewTwoColumnLeft"),
            PdfInitialView.TwoColumnRight => text.Get("Export_InitialViewTwoColumnRight"),
            _ => null
        };

    private static string? DescribeInitialViewForFormat(
        PdfInitialView initialView,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        if (initialView == PdfInitialView.SinglePage)
            return null;

        return format == ExportFormat.Xps
            ? text.Get("Export_InitialViewPdfOnly")
            : DescribeInitialView(initialView, text);
    }

    private static string? DescribeOpenMode(PdfOpenMode openMode, ExportPlannerTextResolver text) =>
        openMode switch
        {
            PdfOpenMode.Outlines => text.Get("Export_OpenModeOutlines"),
            PdfOpenMode.FullScreen => text.Get("Export_OpenModeFullScreen"),
            _ => null
        };

    private static string? DescribeOpenModeForFormat(
        PdfOpenMode openMode,
        ExportFormat format,
        ExportPlannerTextResolver text)
    {
        if (openMode == PdfOpenMode.Normal)
            return null;

        return format == ExportFormat.Xps
            ? text.Get("Export_OpenModePdfOnly")
            : DescribeOpenMode(openMode, text);
    }
}
