using FreeX.Core.Model;

namespace FreeX.App.Presentation.Import;

/// <summary>
/// A remembered Get Data import source: the file path plus the options it was imported with, the
/// resolved destination kind, and the exact anchor cell (sheet + address) the original import wrote to.
/// The host keeps the most recent one so Data ▸ Refresh can re-run the same import cheaply without
/// reopening the dialog — and, critically, back into <see cref="Anchor"/> rather than wherever the
/// selection happens to be at refresh time (see R88-io-text-import-wizard-5-1).
/// </summary>
public sealed record ImportDataSource(
    string FilePath,
    ImportDataOptions Options,
    ImportDestinationKind ResolvedDestination,
    CellAddress Anchor)
{
    /// <summary>
    /// True when a refresh can be re-run without prompting: the path is non-empty and the destination is a
    /// concrete target (a fixed anchor cell), so re-importing simply overwrites the same block. A
    /// new-sheet import is also cheap — refresh re-targets the same sheet the original import created.
    /// </summary>
    public bool CanRefresh => !string.IsNullOrWhiteSpace(FilePath);
}
