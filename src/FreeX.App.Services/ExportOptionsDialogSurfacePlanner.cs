using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum ExportOptionsDialogFocusTarget
{
    FromPage,
    ToPage,
    PdfLanguage
}

public sealed record ExportOptionsDialogFormatAvailability(
    bool PdfBookmarksEnabled,
    bool PdfInitialViewEnabled,
    bool PdfOpenModeEnabled,
    bool PdfLanguageEnabled,
    bool PdfBitmapTextEnabled,
    bool MinimumSizeEnabled);

public static class ExportOptionsDialogSurfacePlanner
{
    public const string TitleResourceKey = "ExportOptions_ExportOptions";
    public const string DialogAutomationId = "ExportOptionsDialog";
    public const double Width = 430;
    public const double CaptureWidth = Width;
    public const double CaptureHeight = 552;
    public const double MaxHeight = 560;

    public static ExportOptionsDialogFormatAvailability CreateFormatAvailability(ExportFileFormat format) =>
        format == ExportFileFormat.Xps
            ? new ExportOptionsDialogFormatAvailability(
                PdfBookmarksEnabled: false,
                PdfInitialViewEnabled: false,
                PdfOpenModeEnabled: false,
                PdfLanguageEnabled: false,
                PdfBitmapTextEnabled: false,
                MinimumSizeEnabled: false)
            : new ExportOptionsDialogFormatAvailability(
                PdfBookmarksEnabled: true,
                PdfInitialViewEnabled: true,
                PdfOpenModeEnabled: true,
                PdfLanguageEnabled: true,
                PdfBitmapTextEnabled: true,
                MinimumSizeEnabled: true);

    public static ExportOptionsDialogFormatAvailability CreateFormatAvailability(ExportFormat format) =>
        CreateFormatAvailability(ExportPlanner.ToExportFileFormat(format));

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
        bool includeDocumentStructureTags = false,
        ExportFormat format = ExportFormat.Pdf) =>
        ExportPlanner.CreateEffectiveOptionsForFormat(new ExportOptions(
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
            includeDocumentStructureTags),
            format);

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

    public static ExportOptionsDialogFocusTarget ResolveInvalidPageRangeFocusTarget(
        string? error,
        string? fromPageText,
        string fromLessThanToError)
    {
        if (string.Equals(error, fromLessThanToError, StringComparison.Ordinal))
            return ExportOptionsDialogFocusTarget.ToPage;

        if (int.TryParse(
                fromPageText?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var fromPage)
            && fromPage >= 1)
        {
            return ExportOptionsDialogFocusTarget.ToPage;
        }

        return ExportOptionsDialogFocusTarget.FromPage;
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
