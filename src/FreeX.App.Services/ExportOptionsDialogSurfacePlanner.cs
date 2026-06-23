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
}
