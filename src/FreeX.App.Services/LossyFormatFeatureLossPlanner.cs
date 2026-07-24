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
    /// that means more than one worksheet, or any chart on any sheet -- a single-sheet workbook with no
    /// charts loses nothing there. For .ods (OdsFileAdapter has no VBA-project support at all) that
    /// means a workbook carrying a VBA project, whose macros would be silently discarded.
    /// </summary>
    public static bool RequiresFeatureLossConfirmation(Workbook workbook, string extension)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var normalizedExtension = FileFormatResolver.NormalizeExtension(extension);

        if (normalizedExtension.Equals(".ods", StringComparison.OrdinalIgnoreCase))
            return workbook.HasVbaProjectPackage;

        if (!LossyPlainTextExtensions.Contains(normalizedExtension))
            return false;

        return workbook.Sheets.Count > 1 || workbook.Sheets.Any(sheet => sheet.Charts.Count > 0);
    }
}
