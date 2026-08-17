using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A snapshot of a worksheet's selection: the active-cell anchor, the moving cursor,
/// the primary selected range, and any additional (multi-select) ranges.
/// </summary>
public sealed record WorksheetSelectionSnapshot(
    CellAddress Anchor,
    CellAddress Cursor,
    GridRange PrimaryRange,
    IReadOnlyList<GridRange>? AdditionalRanges)
{
    /// <summary>
    /// Rewrites this snapshot onto <paramref name="targetSheet"/>, preserving row/column
    /// coordinates. Used to mirror the selection across a grouped set of sheets, the way
    /// Excel shows the same selection on every grouped sheet.
    /// </summary>
    public WorksheetSelectionSnapshot Remap(SheetId targetSheet) => new(
        RemapAddress(Anchor, targetSheet),
        RemapAddress(Cursor, targetSheet),
        RemapRange(PrimaryRange, targetSheet),
        AdditionalRanges?.Select(r => RemapRange(r, targetSheet)).ToList());

    private static CellAddress RemapAddress(CellAddress address, SheetId targetSheet)
        => new(targetSheet, address.Row, address.Col);

    private static GridRange RemapRange(GridRange range, SheetId targetSheet)
        => new(RemapAddress(range.Start, targetSheet), RemapAddress(range.End, targetSheet));
}

/// <summary>
/// Remembers each worksheet's selection within a single window so switching sheets restores
/// the selection that was active on the sheet being shown, matching Excel. One instance per
/// window — Excel windows keep independent selections.
/// </summary>
public sealed class WorksheetSelectionStore
{
    private readonly Dictionary<SheetId, WorksheetSelectionSnapshot> _bySheet = new();

    public void Save(SheetId sheetId, WorksheetSelectionSnapshot snapshot) => _bySheet[sheetId] = snapshot;

    public bool TryGet(SheetId sheetId, out WorksheetSelectionSnapshot snapshot)
        => _bySheet.TryGetValue(sheetId, out snapshot!);

    /// <summary>
    /// Every sheet this window has navigated away from (and therefore has its own remembered
    /// selection snapshot for), keyed by sheet id. Used by each shell's save path to reconcile
    /// this window's own active cell/selection onto the shared <see cref="Sheet"/> fields
    /// immediately before serialization, so Ctrl+S from THIS window persists what THIS window is
    /// actually showing rather than whatever a sibling "New Window" last left in the shared model
    /// -- mirrors <see cref="WorksheetViewStateStore.Snapshots"/>.
    /// </summary>
    public IReadOnlyDictionary<SheetId, WorksheetSelectionSnapshot> Snapshots => _bySheet;

    /// <summary>Drops a sheet's remembered selection (e.g. when the sheet is deleted).</summary>
    public void Remove(SheetId sheetId) => _bySheet.Remove(sheetId);

    /// <summary>Forgets every remembered selection (e.g. when a new workbook is loaded).</summary>
    public void Clear() => _bySheet.Clear();
}
