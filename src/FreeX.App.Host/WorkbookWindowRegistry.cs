using System.Linq;
using System.Windows;
using FreeX.App.Presentation.Shell;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// A scroll position shared between side-by-side workbook windows during synchronous scrolling.
/// Expressed in the same units as the worksheet scroll bars (row/column scroll values).
/// </summary>
public readonly record struct WorkbookScrollOffset(double Row, double Column);

/// <summary>
/// The operations the <see cref="WorkbookWindowRegistry"/> drives on a live workbook window.
/// <see cref="MainWindow"/> implements this; tests use a lightweight fake so the registry's
/// registration / numbering / switch / broadcast logic can be exercised without standing up WPF.
/// </summary>
public interface IWorkbookWindow
{
    /// <summary>
    /// Identity of the document this window is currently viewing. Windows opened via
    /// View &gt; New Window share their originating window's document (same id); windows
    /// hosting an independently opened/created workbook have their own id. The registry
    /// uses this to scope refreshes, title numbering, and dirty-state broadcasts to the
    /// windows of one document instead of every window in the process.
    /// </summary>
    WorkbookId DocumentId { get; }

    /// <summary>Applies an Excel-style window-number suffix (e.g. ":2", or "" for a lone window).</summary>
    void ApplyWindowTitleSuffix(string suffix);

    /// <summary>Refreshes the viewport/status from the shared workbook after a cross-window change.</summary>
    void RefreshFromSharedWorkbook();

    /// <summary>
    /// Refreshes this window's title bar to reflect the current shared document state
    /// (dirty indicator, file name).  Called by the registry after a dirty/saved transition
    /// so all windows' title bars stay in sync without a full viewport refresh.
    /// </summary>
    void RefreshTitleBar();

    /// <summary>Brings this window to the foreground (Switch Windows / New Window activation).</summary>
    void ActivateWindow();

    /// <summary>Shows or hides this window (Hide Window / Unhide Window).</summary>
    void SetWindowVisible(bool visible);

    /// <summary>Current worksheet scroll position (for synchronous scrolling).</summary>
    WorkbookScrollOffset GetScrollOffset();

    /// <summary>Applies a worksheet scroll position pushed from the paired window.</summary>
    void SetScrollOffset(WorkbookScrollOffset offset);

    /// <summary>Restores this window to Normal state and positions it at the given work-area bounds.</summary>
    void TileToWorkArea(Rect bounds);

    /// <summary>
    /// Applies a Formula Bar visibility change made in ANOTHER window of this same process
    /// (R83-app-view-modes-5-2: Show Formula Bar is a genuine Excel-instance-wide display
    /// preference, not a per-document one, so every open window -- across every document --
    /// must reflect it immediately, the way real Excel does).
    /// </summary>
    void ApplyFormulaBarVisibility(bool visible);

    /// <summary>
    /// Applies (or releases) an EXTERNAL save-input gate raised by another window viewing the
    /// same document (Excel "New Window" sibling), for the duration of that sibling's
    /// full-workbook save.  Save serializes the LIVE Workbook instance on a background thread
    /// (see MainWindow.Backstage.cs SaveWorkbookToTargetAsync); a New Window sibling shares that
    /// exact Workbook/CommandBus instance, so without this a keystroke in this window while the
    /// OTHER window's background thread enumerates the shared Sheet cell dictionaries could tear
    /// them structurally mid-enumeration (R115-app-host-save-race). Implementations must be
    /// reentrant/hold-counted: this window's own save and any number of sibling saves may overlap,
    /// and input must stay blocked until every hold has released.
    /// </summary>
    void ApplySaveInProgress(bool inProgress);
}

/// <summary>
/// Tracks every live workbook window in the process, across all open documents. Several windows
/// may view the same document (Excel-style "New Window" — same <see cref="IWorkbookWindow.DocumentId"/>);
/// windows over different documents coexist independently (File &gt; Open in one window never
/// affects another document's windows). Workbook-content refreshes, dirty-state broadcasts, and
/// Excel-style title numbering are scoped per document; window switching, hide/unhide, Arrange All,
/// and View Side by Side deliberately span all documents (Excel parity — side-by-side exists to
/// compare two different workbooks).
///
/// The registry is a thin adapter: registration, grouping, title numbering, notification audiences,
/// and switch-window cycling are delegated to the portable, unit-tested
/// <see cref="WorkbookWindowRegistryCore{TWindow}"/>; geometry decisions are delegated to
/// <see cref="WindowResetPositionPlanner"/>, <see cref="ArrangeAllLayoutPlanner"/>, and <see cref="SideBySideLayoutPlanner"/>.
///
/// Registered as a DI singleton so all windows coordinate through one registry.
/// </summary>
public sealed class WorkbookWindowRegistry
{
    private readonly WorkbookWindowRegistryCore<IWorkbookWindow> _core;
    private readonly WorkbookSideBySideCoordinator<IWorkbookWindow> _sideBySide = new();

    public WorkbookWindowRegistry()
    {
        _core = new WorkbookWindowRegistryCore<IWorkbookWindow>(
            static window => window.DocumentId,
            static _ => true,
            static (window, suffix) => window.ApplyWindowTitleSuffix(suffix));
    }

    /// <summary>Live windows in registration order.</summary>
    public IReadOnlyList<IWorkbookWindow> Windows => _core.Windows;

    /// <summary>Registered windows that expose a live formula point-mode session.</summary>
    public IReadOnlyList<IFormulaPointModeWorkbookWindow> FormulaPointModeWindows =>
        _core.Windows.OfType<IFormulaPointModeWorkbookWindow>().ToList();

    /// <summary>Registered windows that are currently visible, in registration order.</summary>
    public IReadOnlyList<IWorkbookWindow> VisibleWindows => _core.VisibleWindows;

    public int Count => _core.Count;

    /// <summary>Number of registered windows that are currently visible (not hidden).</summary>
    public int VisibleCount => _core.VisibleCount;

    /// <summary>Currently-hidden windows, in registration order.</summary>
    public IReadOnlyList<IWorkbookWindow> HiddenWindows => _core.HiddenWindows;

    /// <summary>True when View Side by Side is currently tiling a pair of windows.</summary>
    public bool IsSideBySideActive => _sideBySide.IsActive;

    /// <summary>True when scrolling one side-by-side window mirrors into its partner.</summary>
    public bool IsSynchronousScrollActive => _sideBySide.IsSynchronousScrollActive;

    public bool IsSideBySideActiveFor(IWorkbookWindow requester) =>
        _sideBySide.IsActiveFor(requester);

    public bool IsSynchronousScrollActiveFor(IWorkbookWindow requester) =>
        _sideBySide.IsSynchronousScrollActiveFor(requester);

    public IWorkbookWindow? SideBySidePartnerOf(IWorkbookWindow requester) =>
        _sideBySide.PartnerOf(requester);

    /// <summary>True when the window is registered and not hidden.</summary>
    public bool IsVisible(IWorkbookWindow window) => _core.IsVisible(window);

    /// <summary>
    /// A window can be hidden only when it is registered, currently visible, and at least one
    /// other window would remain visible (you cannot hide the last visible window).
    /// </summary>
    public bool CanHide(IWorkbookWindow window) => _core.CanHide(window);

    /// <summary>Hides the window if <see cref="CanHide"/> allows. Returns true if it was hidden.</summary>
    public bool Hide(IWorkbookWindow window)
    {
        if (!_core.Hide(window))
            return false;

        if (_sideBySide.Contains(window))
            DisableSideBySide();
        window.SetWindowVisible(false);
        return true;
    }

    /// <summary>Restores a hidden window and activates it. Returns true if it was unhidden.</summary>
    public bool Unhide(IWorkbookWindow window)
    {
        if (!_core.Unhide(window))
            return false;

        window.SetWindowVisible(true);
        window.ActivateWindow();
        return true;
    }

    /// <summary>
    /// True once at least one window exists. (Whether a new window adopts an existing document is
    /// decided per document via <see cref="HasWindowForDocument"/>, not by this process-wide flag.)
    /// </summary>
    public bool HasWindows => _core.HasWindows;

    /// <summary>Adds a window and renumbers every window's Excel-style title suffix.</summary>
    public void Register(IWorkbookWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _core.Register(window);
    }

    /// <summary>Removes a closing window and renumbers the survivors.</summary>
    public void Unregister(IWorkbookWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_sideBySide.Contains(window))
            DisableSideBySide();
        _core.Unregister(window);
    }

    /// <summary>Index of <paramref name="window"/> in registration order, or -1 if not registered.</summary>
    public int IndexOf(IWorkbookWindow window) => _core.IndexOf(window);

    /// <summary>
    /// The next window to activate when cycling Switch Windows from <paramref name="currentWindow"/>.
    /// Returns null when there is no other window to switch to.
    /// </summary>
    public IWorkbookWindow? NextWindowTarget(IWorkbookWindow currentWindow)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);
        return _core.NextWindowTarget(currentWindow, WorkbookWindowCycleDirection.Forward);
    }

    /// <summary>
    /// The previous window to activate when cycling backward from <paramref name="currentWindow"/>.
    /// Returns null when there is no other window to switch to.
    /// </summary>
    public IWorkbookWindow? PreviousWindowTarget(IWorkbookWindow currentWindow)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);
        return _core.NextWindowTarget(currentWindow, WorkbookWindowCycleDirection.Backward);
    }

    /// <summary>Activates the next window in the cycle, if there is one. Returns true if it switched.</summary>
    public bool SwitchToNextWindow(IWorkbookWindow currentWindow)
    {
        return _core.SwitchToWindow(
            currentWindow,
            WorkbookWindowCycleDirection.Forward,
            static target => target.ActivateWindow());
    }

    /// <summary>Activates the previous window in the cycle, if there is one. Returns true if it switched.</summary>
    public bool SwitchToPreviousWindow(IWorkbookWindow currentWindow)
    {
        return _core.SwitchToWindow(
            currentWindow,
            WorkbookWindowCycleDirection.Backward,
            static target => target.ActivateWindow());
    }

    /// <summary>
    /// Tells every window viewing the same document as <paramref name="origin"/> to refresh its
    /// title bar after a document-state change (dirty/saved transition). The views of one document
    /// share a <see cref="WorkbookDocumentState"/>, so the dirty flag is already consistent among
    /// them; this call ensures their title bars reflect the new state without a full viewport
    /// refresh. Windows over other documents are left untouched — their dirty state is unrelated.
    /// The <paramref name="origin"/> window has already updated its own title bar before
    /// calling this, but including it in the refresh is safe and simplifies the loop.
    /// </summary>
    public void NotifyDocumentStateChanged(IWorkbookWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.SameDocument,
            static window => window.RefreshTitleBar());
    }

    /// <summary>
    /// Tells every OTHER window viewing the same document as <paramref name="origin"/> to refresh
    /// its viewport/status from the shared workbook, so an edit (or undo/redo) in one view appears
    /// in the sibling views. Windows over other documents are not refreshed — their content did
    /// not change (H39: File &gt; Open in one window must never rebind another document's windows).
    /// </summary>
    public void NotifyWorkbookChanged(IWorkbookWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.SameDocumentExceptOrigin,
            static window => window.RefreshFromSharedWorkbook());
    }

    /// <summary>
    /// Broadcasts a Formula Bar visibility change to every OTHER registered window in the
    /// process, regardless of document (R83-app-view-modes-5-2). Show Formula Bar is a genuine
    /// Excel-instance-wide display preference -- not a per-workbook property saved in the xlsx
    /// (only the separate named "Custom Views" feature has a per-view showFormulaBar flag, via
    /// customWorkbookView) -- so toggling it in one window must be reflected live in every other
    /// open window, exactly like real Excel, instead of only taking effect the next time each
    /// sibling window happens to refresh for an unrelated reason.
    /// </summary>
    public void BroadcastFormulaBarVisibility(IWorkbookWindow origin, bool visible)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.AllExceptOrigin,
            window => window.ApplyFormulaBarVisibility(visible));
    }

    /// <summary>
    /// Extends <paramref name="origin"/>'s save-input gate to every OTHER window viewing the
    /// SAME document (Excel "New Window" siblings share the live Workbook instance origin is
    /// serializing in the background -- see <see cref="IWorkbookWindow.ApplySaveInProgress"/>).
    /// Windows over a different document are untouched: their workbook is a different instance,
    /// unaffected by origin's save. <paramref name="origin"/> itself is skipped -- it has already
    /// applied the gate to its own input directly, before/after invoking the save.
    /// </summary>
    public void BroadcastSaveInProgress(IWorkbookWindow origin, bool inProgress)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.SameDocumentExceptOrigin,
            window => window.ApplySaveInProgress(inProgress));
    }

    /// <summary>
    /// True when at least one registered window other than <paramref name="window"/> views the
    /// same document. Such siblings keep the document alive: replacing this window's document
    /// (File &gt; Open / File &gt; New) must detach into a fresh context instead of mutating the
    /// shared one, and closing this window must neither prompt to save nor tear the document down.
    /// </summary>
    public bool HasOtherWindowsForDocument(IWorkbookWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _core.HasOtherWindowForDocument(window);
    }

    /// <summary>True when any registered window currently views the document <paramref name="documentId"/>.</summary>
    public bool HasWindowForDocument(WorkbookId documentId)
    {
        return _core.HasWindowForDocument(documentId);
    }

    /// <summary>
    /// Recomputes every window's Excel-style title suffix. Register/Unregister renumber
    /// automatically; this entry point is for document swaps (File &gt; Open / File &gt; New in a
    /// window that shared its document with siblings), where a window changes document group
    /// without registering or unregistering.
    /// </summary>
    public void RefreshWindowNumbering() => _core.RefreshWindowNumbering();

    // Arrange All

    /// <summary>
    /// Applies an Arrange All layout to every visible workbook window. Hidden windows are left as-is,
    /// matching Excel's distinction between Hide/Unhide and live window arrangement.
    /// </summary>
    /// <param name="arrangement">The tiling layout to apply.</param>
    /// <param name="workAreaWidth">Work-area width to tile within.</param>
    /// <param name="workAreaHeight">Work-area height to tile within.</param>
    /// <param name="restrictToDocumentId">
    /// When non-null, mirrors the Arrange Windows dialog's "Windows of active workbook" checkbox:
    /// only visible windows viewing this document are tiled, leaving every other open document's
    /// windows exactly where they are. Null (the default) arranges every visible window across
    /// every open document, matching the checkbox left unchecked (R90-app-window-arrange-freeze-ui-5-4).
    /// </param>
    public bool ArrangeVisibleWindows(
        WorkbookWindowArrangement arrangement,
        double workAreaWidth,
        double workAreaHeight,
        WorkbookId? restrictToDocumentId = null)
    {
        var targets = _core.PlanVisibleArrangement(
            (ShellWindowArrangement)arrangement,
            workAreaWidth,
            workAreaHeight,
            restrictToDocumentId is { } documentId
                ? window => window.DocumentId == documentId
                : null);
        if (targets.Count == 0)
            return false;

        // An unrestricted Arrange All always breaks any active side-by-side pairing (unchanged
        // behavior). A restricted one only breaks it if the pair is actually among the windows
        // being re-tiled -- arranging one workbook's windows must not silently un-pair an unrelated
        // side-by-side comparison between two OTHER documents.
        if (restrictToDocumentId is null || targets.Any(target => _sideBySide.Contains(target.Window)))
            DisableSideBySide();

        foreach (var target in targets)
        {
            var b = target.Bounds;
            target.Window.TileToWorkArea(new Rect(b.X, b.Y, b.Width, b.Height));
        }

        return true;
    }

    // View Side by Side / Synchronous Scrolling

    /// <summary>
    /// Tiles <paramref name="primary"/> and the next visible window into the two halves of the work
    /// area (via <see cref="SideBySideLayoutPlanner"/>) and marks side-by-side active. Returns false
    /// (and tiles nothing) when there is no other visible window to pair with.
    /// </summary>
    public bool EnableSideBySide(IWorkbookWindow primary, double workAreaWidth, double workAreaHeight)
    {
        ArgumentNullException.ThrowIfNull(primary);
        if (!IsVisible(primary))
            return false;

        var partner = NextVisibleWindow(primary);
        if (partner is null)
            return false;

        var (primaryBounds, partnerBounds) = SideBySideLayoutPlanner.Tile(workAreaWidth, workAreaHeight);
        primary.TileToWorkArea(new Rect(primaryBounds.X, primaryBounds.Y, primaryBounds.Width, primaryBounds.Height));
        partner.TileToWorkArea(new Rect(partnerBounds.X, partnerBounds.Y, partnerBounds.Width, partnerBounds.Height));

        return _sideBySide.Enable(primary, partner);
    }

    /// <summary>
    /// Excel's "Reset Window Position" (View Side by Side group): restores BOTH windows of the
    /// active side-by-side pair to their original tiled top/bottom (or left/right) halves via
    /// <see cref="SideBySideLayoutPlanner"/> -- the same geometry <see cref="EnableSideBySide"/>
    /// applied -- undoing any manual resize/drag made to either window while comparing them.
    /// Unlike a generic per-window cascade, this never touches a window that is not part of the
    /// active pair, and does nothing (returning false) when side-by-side is not currently active,
    /// matching Excel disabling the command outside of View Side by Side.
    /// </summary>
    public bool ResetSideBySidePair(
        IWorkbookWindow requester,
        double workAreaWidth,
        double workAreaHeight)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (!_sideBySide.TryGetPairFor(requester, out var primary, out var partner))
            return false;

        var (primaryBounds, partnerBounds) = SideBySideLayoutPlanner.Tile(workAreaWidth, workAreaHeight);
        primary.TileToWorkArea(new Rect(primaryBounds.X, primaryBounds.Y, primaryBounds.Width, primaryBounds.Height));
        partner.TileToWorkArea(new Rect(partnerBounds.X, partnerBounds.Y, partnerBounds.Width, partnerBounds.Height));
        return true;
    }

    /// <summary>Stops side-by-side mode. Layout is left as-is; synchronous scrolling is also turned off.</summary>
    public void DisableSideBySide() => _sideBySide.Disable();

    /// <summary>
    /// Stops side-by-side mode only if <paramref name="requester"/> is actually one of the paired
    /// windows (or no pair is active, in which case there is nothing to stop). Guards against an
    /// unrelated third window silently un-pairing and desyncing a pair it was never part of -- e.g.
    /// clicking "View Side by Side" on window C while A and B are already tiled together must not
    /// turn off A/B's pairing (and synchronous scrolling) out from under them.
    /// Returns true if side-by-side was active and owned by <paramref name="requester"/> and was
    /// turned off; false if there was no active pair, or the active pair belongs to other windows
    /// and was left untouched.
    /// </summary>
    public bool DisableSideBySideFor(IWorkbookWindow requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return _sideBySide.DisableFor(requester);
    }

    /// <summary>
    /// Enables or disables synchronous scrolling. Synchronous scrolling is only meaningful while
    /// side-by-side is active; enabling it without an active pair is refused.
    /// </summary>
    public bool SetSynchronousScroll(bool active)
    {
        return _sideBySide.SetSynchronousScroll(active);
    }

    public bool SetSynchronousScrollFor(IWorkbookWindow requester, bool active)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return _sideBySide.SetSynchronousScrollFor(requester, active);
    }

    /// <summary>
    /// When side-by-side + synchronous scrolling are active, pushes <paramref name="offset"/> from the
    /// originating window into its paired window. Guarded so the partner applying the offset cannot
    /// loop the broadcast back into the origin.
    /// </summary>
    public void BroadcastScrollOffset(IWorkbookWindow origin, WorkbookScrollOffset offset)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _sideBySide.ApplyToSynchronousPartner(
            origin,
            offset,
            static (target, synchronizedOffset) => target.SetScrollOffset(synchronizedOffset));
    }

    /// <summary>The next visible window after <paramref name="window"/> in the switch cycle, skipping hidden windows.</summary>
    private IWorkbookWindow? NextVisibleWindow(IWorkbookWindow window)
    {
        return _core.NextWindowTarget(window, WorkbookWindowCycleDirection.Forward);
    }
}
