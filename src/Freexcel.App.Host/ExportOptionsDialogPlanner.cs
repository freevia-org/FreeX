namespace Freexcel.App.Host;

internal enum ExportOptionsFocusTarget
{
    FromPage,
    ToPage,
    PdfLanguage
}

internal static class ExportOptionsDialogPlanner
{
    public static ExportOptions CreateResult(
        ExportContentScope scope,
        bool includeDocumentProperties,
        bool openAfterPublish,
        bool ignorePrintAreas = false,
        ExportPageRange? pageRange = null,
        ExportQuality quality = ExportQuality.Standard,
        bool createBookmarks = false,
        PdfBookmarkMode bookmarkMode = PdfBookmarkMode.None,
        PdfInitialView initialView = PdfInitialView.SinglePage,
        PdfOpenMode openMode = PdfOpenMode.Normal,
        bool bitmapTextWhenFontsMayNotBeEmbedded = false,
        string? pdfLanguage = ExportPlanner.DefaultPdfLanguage,
        PdfConformance pdfConformance = PdfConformance.Standard,
        bool includeDocumentStructureTags = false) =>
        new(
            Enum.IsDefined(scope) ? scope : ExportContentScope.ActiveSheet,
            includeDocumentProperties,
            openAfterPublish,
            ignorePrintAreas,
            pageRange,
            Enum.IsDefined(quality) ? quality : ExportQuality.Standard,
            createBookmarks,
            NormalizeBookmarkMode(createBookmarks, bookmarkMode),
            Enum.IsDefined(initialView) ? initialView : PdfInitialView.SinglePage,
            Enum.IsDefined(openMode) ? openMode : PdfOpenMode.Normal,
            bitmapTextWhenFontsMayNotBeEmbedded,
            ExportPlanner.NormalizePdfLanguage(pdfLanguage),
            Enum.IsDefined(pdfConformance) ? pdfConformance : PdfConformance.Standard,
            includeDocumentStructureTags);

    public static PdfBookmarkMode BookmarkModeFromIndex(int selectedIndex) =>
        selectedIndex switch
        {
            1 => PdfBookmarkMode.PrintTitles,
            2 => PdfBookmarkMode.PageNumbers,
            _ => PdfBookmarkMode.SheetNames
        };

    public static PdfInitialView InitialViewFromIndex(int selectedIndex) =>
        selectedIndex switch
        {
            1 => PdfInitialView.OneColumn,
            2 => PdfInitialView.TwoColumnLeft,
            3 => PdfInitialView.TwoColumnRight,
            _ => PdfInitialView.SinglePage
        };

    public static PdfOpenMode OpenModeFromIndex(int selectedIndex) =>
        selectedIndex switch
        {
            1 => PdfOpenMode.Outlines,
            2 => PdfOpenMode.FullScreen,
            _ => PdfOpenMode.Normal
        };

    public static ExportOptionsFocusTarget ResolveInvalidPageRangeFocusTarget(
        string? error,
        string? fromPageText)
    {
        if (string.Equals(error, "From page must be less than or equal to To page.", StringComparison.Ordinal))
            return ExportOptionsFocusTarget.ToPage;

        if (int.TryParse(
                fromPageText?.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var fromPage)
            && fromPage >= 1)
        {
            return ExportOptionsFocusTarget.ToPage;
        }

        return ExportOptionsFocusTarget.FromPage;
    }

    private static PdfBookmarkMode NormalizeBookmarkMode(bool createBookmarks, PdfBookmarkMode bookmarkMode)
    {
        if (!createBookmarks)
            return PdfBookmarkMode.None;

        return Enum.IsDefined(bookmarkMode) && bookmarkMode != PdfBookmarkMode.None
            ? bookmarkMode
            : PdfBookmarkMode.SheetNames;
    }
}
