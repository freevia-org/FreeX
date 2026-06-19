namespace FreeX.App.Presentation.Import;

/// <summary>
/// A remembered Get Data import source: the file path plus the options it was imported with and the
/// destination it was written to. The host keeps the most recent one so Data ▸ Refresh can re-run the
/// same import cheaply without reopening the dialog.
/// </summary>
public sealed record ImportDataSource(
    string FilePath,
    ImportDataOptions Options,
    ImportDestinationKind ResolvedDestination)
{
    /// <summary>
    /// True when a refresh can be re-run without prompting: the path is non-empty and the destination is a
    /// concrete target (the current sheet at a fixed anchor), so re-importing simply overwrites the same
    /// block. A new-sheet import is also cheap — it just adds another sheet.
    /// </summary>
    public bool CanRefresh => !string.IsNullOrWhiteSpace(FilePath);
}
