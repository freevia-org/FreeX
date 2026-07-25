using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A window's own worksheet view mode, zoom level, and View tab display toggles for one sheet.
/// </summary>
/// <remarks>
/// <see cref="ShowGridlines"/>/<see cref="ShowHeadings"/>/<see cref="ShowRulers"/> extend the
/// R83 ViewMode/ZoomPercent per-window independence to the rest of the WPF host's View tab
/// toggles (R87-order-guard-window-state-sweep-1), mirroring the Avalonia shell's R86
/// <c>_viewShowGridlinesOverrides</c>/<c>_viewShowHeadingsOverrides</c> sweep. Show Formulas and
/// Freeze/Split panes are NOT included here: their actual on-screen rendering is baked directly
/// off the shared <see cref="Sheet"/> inside <c>FreeX.Core.Calc.ViewportService</c> (used by both
/// shells), so a WPF-host-only override for those would desync the ribbon toggle from what the
/// grid actually shows instead of fixing the leak -- closing that gap requires threading an
/// override through <c>ViewportRequest</c> in the shared rendering engine, out of scope here.
/// </remarks>
public readonly record struct WorksheetViewStateSnapshot(
    WorksheetViewMode ViewMode,
    int ZoomPercent,
    bool ShowGridlines = true,
    bool ShowHeadings = true,
    bool ShowRulers = true);

/// <summary>
/// Remembers each worksheet's view mode, zoom level, and View tab display toggles within a
/// single window, independent of any other window viewing the same document (Excel "View &gt;
/// New Window"). All windows over one document share the very same <see cref="Sheet"/> object
/// graph -- and therefore the same <see cref="Sheet.ViewMode"/>/<see cref="Sheet.ZoomPercent"/>/
/// <see cref="Sheet.ShowGridlines"/>/<see cref="Sheet.ShowHeadings"/>/<see cref="Sheet.ShowRulers"/>
/// fields, which recalculation and save still treat as the persisted values -- but each open
/// window must keep displaying whatever it last set, even after a sibling window changes those
/// shared fields (R83-app-view-modes-5-1, extended to the View tab toggles by
/// R87-order-guard-window-state-sweep-1). One instance per window, exactly like
/// <see cref="WorksheetSelectionStore"/>.
/// </summary>
public sealed class WorksheetViewStateStore
{
    private readonly Dictionary<SheetId, WorksheetViewStateSnapshot> _bySheet = new();

    /// <summary>
    /// This window's view mode/zoom/display-toggles for <paramref name="sheet"/>, lazily seeded
    /// from the sheet's current (shared) values the first time this window renders it. Once
    /// seeded, a later change to the shared <see cref="Sheet"/> fields made by ANOTHER window
    /// never overwrites this window's remembered snapshot -- only this window's own
    /// <see cref="Set"/> call (after it executes the corresponding command itself) does.
    /// </summary>
    public WorksheetViewStateSnapshot GetOrSeed(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (_bySheet.TryGetValue(sheet.Id, out var existing))
            return existing;

        var seeded = new WorksheetViewStateSnapshot(
            sheet.ViewMode,
            sheet.ZoomPercent,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers);
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
