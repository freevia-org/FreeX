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
    // The single-sheet/plain-text Save-As targets this codebase registers adapters for. Richer
    // formats (.xlsx family, .ods, .xml, .html/.mht, .pdf) are handled by their own dedicated
    // feature-loss checks (or aren't plain/single-sheet at all) and are intentionally excluded here.
    private static readonly HashSet<string> LossyPlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".txt", ".prn", ".slk", ".dif", ".dbf", ".tab", ".tsv"
    };

    /// <summary>
    /// True if saving <paramref name="workbook"/> to <paramref name="extension"/> would silently drop
    /// content the format can't hold: more than one worksheet, or any chart on any sheet. A
    /// single-sheet workbook with no charts loses nothing by moving to a plain/single-sheet format, so
    /// no confirmation is needed for that case.
    /// </summary>
    public static bool RequiresFeatureLossConfirmation(Workbook workbook, string extension)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (!LossyPlainTextExtensions.Contains(FileFormatResolver.NormalizeExtension(extension)))
            return false;

        return workbook.Sheets.Count > 1 || workbook.Sheets.Any(sheet => sheet.Charts.Count > 0);
    }
}
