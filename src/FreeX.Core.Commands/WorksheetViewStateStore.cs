using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// A window's own worksheet view mode, zoom level, View tab display toggles, and Freeze/Split
/// pane state for one sheet.
/// </summary>
/// <remarks>
/// <see cref="ShowGridlines"/>/<see cref="ShowHeadings"/>/<see cref="ShowRulers"/> extend the
/// R83 ViewMode/ZoomPercent per-window independence to the rest of the WPF host's View tab
/// toggles (R87-order-guard-window-state-sweep-1), mirroring the Avalonia shell's R86
/// <c>_viewShowGridlinesOverrides</c>/<c>_viewShowHeadingsOverrides</c> sweep.
/// <see cref="FrozenRows"/>/<see cref="FrozenCols"/>/<see cref="SplitRow"/>/<see cref="SplitColumn"/>
/// extend it again to Freeze Panes/Window &gt; Split (R89-freeze-split-per-window-1): the shared
/// rendering engine (<c>FreeX.Core.Calc.ViewportService</c>) already accepts a per-viewport
/// <c>FrozenRowsOverride</c>/<c>FrozenColsOverride</c>/<c>SplitOverride</c> on <c>ViewportRequest</c>
/// (added for the Avalonia shell's own per-view overrides in <c>WorkbookSession</c>), so the WPF
/// host only needed to start populating those from this store instead of always reading the
/// shared <see cref="Sheet"/> fields directly -- see <c>MainWindow.Viewport.cs</c>'s
/// <c>CreateViewport</c>/<c>GetSplitPaneViewportOffsets</c> and the scroll-math call sites that
/// now route through <c>GetEffectiveViewState</c>. <see cref="ShowFormulas"/> completes the sweep
/// (R89-show-formulas-per-window-1): <c>ViewportRequest</c> now also carries a
/// <c>ShowFormulasOverride</c> that <c>ViewportService.GetDisplayText</c> consults instead of
/// unconditionally reading the shared <see cref="Sheet.ShowFormulas"/> field, so this window's own
/// remembered Show Formulas toggle can flow through the same per-view override plumbing as
/// Freeze Panes/Split above.
/// </remarks>
public readonly record struct WorksheetViewStateSnapshot(
    WorksheetViewMode ViewMode,
    int ZoomPercent,
    bool ShowGridlines = true,
    bool ShowHeadings = true,
    bool ShowRulers = true,
    uint FrozenRows = 0,
    uint FrozenCols = 0,
    uint? SplitRow = null,
    uint? SplitColumn = null,
    bool ShowFormulas = false);

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
            sheet.ShowRulers,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ShowFormulas);
        _bySheet[sheet.Id] = seeded;
        return seeded;
    }

    /// <summary>Records this window's own view-mode/zoom change for a sheet.</summary>
    public void Set(SheetId sheetId, WorksheetViewStateSnapshot snapshot) => _bySheet[sheetId] = snapshot;

    /// <summary>
    /// Every sheet this window has rendered (and therefore has its own remembered view-state
    /// snapshot for), keyed by sheet id. Used by the WPF host's save path
    /// (R120-corewriter-persist-saving-window-view-1) to reconcile this window's own view onto the
    /// shared <see cref="Sheet"/> fields immediately before serialization, so Ctrl+S from THIS
    /// window persists what THIS window is actually displaying rather than whatever a sibling
    /// "New Window" last left in the shared model.
    /// </summary>
    public IReadOnlyDictionary<SheetId, WorksheetViewStateSnapshot> Snapshots => _bySheet;

    /// <summary>Drops a sheet's remembered view state (e.g. when the sheet is deleted).</summary>
    public void Remove(SheetId sheetId) => _bySheet.Remove(sheetId);

    /// <summary>Forgets every remembered view state (e.g. when a new workbook is loaded).</summary>
    public void Clear() => _bySheet.Clear();
}
