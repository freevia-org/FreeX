using FreeX.Core.Model;

namespace FreeX.App.Presentation.Import;

/// <summary>
/// A remembered Get Data import source: the file path plus the options it was imported with, the
/// resolved destination kind, and the exact anchor cell (sheet + address) the original import wrote to.
/// The host keeps the most recent one so Data ▸ Refresh can re-run the same import cheaply without
/// reopening the dialog — and, critically, back into <see cref="Anchor"/> rather than wherever the
/// selection happens to be at refresh time (see R88-io-text-import-wizard-5-1).
/// </summary>
/// <param name="LastRowCount">
/// The row extent the most recent import/refresh actually wrote at <see cref="Anchor"/> (the source's
/// used-range row count at that time). Fed back into the next refresh's
/// <see cref="FreeX.Core.Commands.ImportSheetCommand"/> as its previous-extent, so that command can
/// clear the leftover cells when the source has since lost rows -- otherwise those cells keep
/// whatever value the prior, larger import wrote and are indistinguishable from freshly imported data
/// (round 134 fix). Persisted for as long as this record lives (i.e. across every refresh in the
/// current app session, in the same <c>_lastImportSource</c> field the anchor and file path already
/// live in) -- not written to the workbook file, matching the rest of the remembered-source state.
/// </param>
/// <param name="LastColCount">The column-extent counterpart of <see cref="LastRowCount"/>.</param>
public sealed record ImportDataSource(
    string FilePath,
    ImportDataOptions Options,
    ImportDestinationKind ResolvedDestination,
    CellAddress Anchor,
    uint LastRowCount = 0,
    uint LastColCount = 0)
{
    /// <summary>
    /// True when a refresh can be re-run without prompting: the path is non-empty and the destination is a
    /// concrete target (a fixed anchor cell), so re-importing simply overwrites the same block. A
    /// new-sheet import is also cheap — refresh re-targets the same sheet the original import created.
    /// </summary>
    public bool CanRefresh => !string.IsNullOrWhiteSpace(FilePath);
}
