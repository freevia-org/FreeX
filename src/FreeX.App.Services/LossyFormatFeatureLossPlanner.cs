using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Decides whether Save-As to a plain/single-sheet lossy format (CSV/TXT/PRN/SLK/DIF/DBF, ...) is
/// about to silently discard content the workbook actually has -- extra sheets, charts, or other
/// content those formats simply cannot represent -- so the host can confirm with the user before
/// writing, the same way <c>ConfirmUnsupportedXlsxFeatureSave</c> already gates a lossy .xlsx save.
/// </summary>
public static class LossyFormatFeatureLossPlanner
{
    // The single-sheet/plain-text Save-As targets this codebase registers adapters for. The .xlsx
    // family has its own dedicated ConfirmUnsupportedXlsxFeatureSave gate (driven by the loaded
    // XlsxFeatureReport) and is intentionally excluded here. .xml, .html/.mht, and .pdf are not
    // (yet) checked at all and are a known gap, not something this planner already covers.
    private static readonly HashSet<string> LossyPlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".txt", ".prn", ".slk", ".dif", ".dbf", ".tab", ".tsv"
    };

    /// <summary>
    /// True if saving <paramref name="workbook"/> to <paramref name="extension"/> would silently drop
    /// content the format can't hold. For the plain/single-sheet formats (CSV/TXT/PRN/SLK/DIF/DBF/...)
    /// that means more than one worksheet, any chart/drawing object (chart, autoshape/textbox, or
    /// picture) on any sheet, or any annotation layer beside the grid -- comments (classic or
    /// threaded), hyperlinks, merged regions, data validations, conditional formats. A single-sheet
    /// workbook with none of those loses nothing there. The annotation half was added in r408 after
    /// measuring that a comment, a hyperlink and a merge all vanished through a .csv round trip with
    /// no confirmation shown.
    /// <see cref="Core.IO.DelimitedTextWorkbookWriter"/> and the SLK/DIF/DBF/PRN writers only ever
    /// enumerate cell values, so <see cref="Sheet.Charts"/>, <see cref="Sheet.DrawingShapes"/>,
    /// <see cref="Sheet.Pictures"/>, and <see cref="Sheet.TextBoxes"/> are all equally unrepresentable
    /// and equally silently discarded by every one of these writers -- none is special-cased over the
    /// others here. For .ods (OdsFileAdapter has no VBA-project support at all) that means a workbook
    /// carrying a VBA project, whose macros would be silently discarded.
    /// </summary>
    public static bool RequiresFeatureLossConfirmation(Workbook workbook, string extension)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var normalizedExtension = FileFormatResolver.NormalizeExtension(extension);

        if (normalizedExtension.Equals(".ods", StringComparison.OrdinalIgnoreCase))
            return workbook.HasVbaProjectPackage;

        if (!LossyPlainTextExtensions.Contains(normalizedExtension))
            return false;

        return workbook.Sheets.Count > 1 || workbook.Sheets.Any(HasUnrepresentableContent);
    }

    private static bool HasUnrepresentableContent(Sheet sheet) =>
        HasUnrepresentableDrawingObject(sheet) || HasUnrepresentableAnnotation(sheet);

    private static bool HasUnrepresentableDrawingObject(Sheet sheet) =>
        sheet.Charts.Count > 0
        || sheet.DrawingShapes.Count > 0
        || sheet.Pictures.Count > 0
        || sheet.TextBoxes.Count > 0;

    /// <summary>
    /// Content that lives beside the grid rather than in it, and that the plain/single-sheet writers
    /// discard just as silently as they discard a chart.
    /// </summary>
    /// <remarks>
    /// r408: this gate's own contract is "would silently drop content the format can't hold", but it
    /// enumerated only sheets and drawing objects. Measured: a SINGLE-sheet workbook carrying a
    /// comment, a hyperlink and a merged region saved to .csv and reloaded came back with 0 comments,
    /// 0 hyperlinks and 0 merges -- and no confirmation was shown, because none of those was on the
    /// list. The writers enumerate cell values only (see the note above on
    /// DelimitedTextWorkbookWriter), so these are exactly as unrepresentable as the drawing objects
    /// already covered; the omission was in the check, not in any belief that they survived.
    /// </remarks>
    private static bool HasUnrepresentableAnnotation(Sheet sheet) =>
        sheet.Comments.Count > 0
        || sheet.ThreadedComments.Count > 0
        || sheet.Hyperlinks.Count > 0
        || sheet.MergedRegions.Count > 0
        || sheet.DataValidations.Count > 0
        || sheet.ConditionalFormats.Count > 0;
}
