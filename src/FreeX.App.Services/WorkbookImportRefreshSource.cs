using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Renderer-neutral state for re-running the most recent adapter-backed Get Data import.
/// Native hosts own file picking and status chrome; this record owns the workbook/anchor identity
/// and previous written extent that make Refresh All deterministic and safe.
/// </summary>
public sealed record WorkbookImportRefreshSource(
    WorkbookId WorkbookId,
    string FilePath,
    string Extension,
    IFileAdapter Adapter,
    string? FormatName,
    CellAddress Anchor,
    uint LastRowCount,
    uint LastColCount)
{
    /// <summary>
    /// Whether this source can refresh the displayed workbook without prompting. A source from a
    /// replaced workbook or a deleted target sheet must never write into the new active document.
    /// File existence remains a host check so platform file systems can report native errors.
    /// </summary>
    public bool CanRefresh(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return !string.IsNullOrWhiteSpace(FilePath)
            && WorkbookId == workbook.Id
            && workbook.GetSheet(Anchor.Sheet) is not null;
    }

    /// <summary>
    /// Returns the stale-cell cleanup extent only for the exact workbook and anchor that produced it.
    /// A normal Get Data import into another destination is a new source, not a refresh.
    /// </summary>
    public (uint RowCount, uint ColCount)? PreviousExtentFor(
        WorkbookId workbookId,
        CellAddress destination) =>
        WorkbookId == workbookId && Anchor == destination
            ? (LastRowCount, LastColCount)
            : null;

    public WorkbookImportRefreshSource WithWrittenExtent(uint rowCount, uint colCount) =>
        this with { LastRowCount = rowCount, LastColCount = colCount };
}
