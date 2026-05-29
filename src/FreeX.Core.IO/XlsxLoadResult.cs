using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Result of loading an XLSX file, containing the workbook and any non-fatal warnings
/// collected during the load (e.g. features that failed to parse but did not abort the load).
/// </summary>
/// <param name="Workbook">The loaded workbook. Never null; partial content is returned even when warnings are present.</param>
/// <param name="Warnings">
/// Diagnostic messages for feature-loading failures that were recovered from.
/// Empty when the file loaded without any issues. Non-empty indicates data that could
/// not be restored (e.g. conditional formatting, data validation, merged regions, named ranges).
/// </param>
public sealed record XlsxLoadResult(Workbook Workbook, IReadOnlyList<string> Warnings)
{
    /// <summary>Returns <c>true</c> if any warnings were collected during loading.</summary>
    public bool HasWarnings => Warnings.Count > 0;
}
