using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A window's own worksheet view mode and zoom level for one sheet.
/// </summary>
public readonly record struct WorksheetViewStateSnapshot(WorksheetViewMode ViewMode, int ZoomPercent);

/// <summary>
/// Remembers each worksheet's view mode and zoom level within a single window, independent of
/// any other window viewing the same document (Excel "View &gt; New Window"). All windows over
/// one document share the very same <see cref="Sheet"/> object graph -- and therefore the same
/// <see cref="Sheet.ViewMode"/>/<see cref="Sheet.ZoomPercent"/> fields, which recalculation and
/// save still treat as the persisted values -- but each open window must keep displaying
/// whatever view mode/zoom IT last set, even after a sibling window changes those shared fields
/// (R83-app-view-modes-5-1). One instance per window, exactly like <see cref="WorksheetSelectionStore"/>.
/// </summary>
public sealed class WorksheetViewStateStore
{
    private readonly Dictionary<SheetId, WorksheetViewStateSnapshot> _bySheet = new();

    /// <summary>
    /// This window's view mode/zoom for <paramref name="sheet"/>, lazily seeded from the sheet's
    /// current (shared) values the first time this window renders it. Once seeded, a later
    /// change to <see cref="Sheet.ViewMode"/>/<see cref="Sheet.ZoomPercent"/> made by ANOTHER
    /// window never overwrites this window's remembered snapshot -- only this window's own
    /// <see cref="Set"/> call (after it executes a view-mode/zoom command itself) does.
    /// </summary>
    public WorksheetViewStateSnapshot GetOrSeed(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (_bySheet.TryGetValue(sheet.Id, out var existing))
            return existing;

        var seeded = new WorksheetViewStateSnapshot(sheet.ViewMode, sheet.ZoomPercent);
        _bySheet[sheet.Id] = seeded;
        return seeded;
    }

    /// <summary>Records this window's own view-mode/zoom change for a sheet.</summary>
    public void Set(SheetId sheetId, WorksheetViewStateSnapshot snapshot) => _bySheet[sheetId] = snapshot;

    /// <summary>Drops a sheet's remembered view state (e.g. when the sheet is deleted).</summary>
    public void Remove(SheetId sheetId) => _bySheet.Remove(sheetId);

    /// <summary>Forgets every remembered view state (e.g. when a new workbook is loaded).</summary>
    public void Clear() => _bySheet.Clear();
}
