using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
    /// <summary>Applies an Excel-style window-number suffix (e.g. " - 2", or "" for a lone window).</summary>
    void ApplyWindowTitleSuffix(string suffix);

    /// <summary>Refreshes the viewport/status from the shared workbook after a cross-window change.</summary>
    void RefreshFromSharedWorkbook();

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
}

/// <summary>
/// Tracks the live workbook windows that all view the single shared workbook (Excel-style
/// "New Window"). The registry is a thin coordinator: every ordering decision (which window to
/// switch to, how to number titles, which windows to refresh) is delegated to the pure, unit-tested
/// <see cref="WorkbookWindowOrdering"/> helper; geometry decisions are delegated to
/// <see cref="WindowResetPositionPlanner"/>, <see cref="ArrangeAllLayoutPlanner"/>, and <see cref="SideBySideLayoutPlanner"/>.
///
/// Registered as a DI singleton so all windows over the shared workbook see the same registry.
/// </summary>
public sealed class WorkbookWindowRegistry
{
    private readonly List<IWorkbookWindow> _windows = [];
    private readonly HashSet<IWorkbookWindow> _hidden = [];

    // Side-by-side / synchronous-scroll state. The pair is the two windows that were tiled together.
    private IWorkbookWindow? _sideBySidePrimary;
    private IWorkbookWindow? _sideBySidePartner;
    private bool _synchronousScroll;
    private bool _applyingBroadcast;

    /// <summary>Live windows in registration order.</summary>
    public IReadOnlyList<IWorkbookWindow> Windows => _windows;

    public int Count => _windows.Count;

    /// <summary>Number of registered windows that are currently visible (not hidden).</summary>
    public int VisibleCount => _windows.Count(w => !_hidden.Contains(w));

    /// <summary>Currently-hidden windows, in registration order.</summary>
    public IReadOnlyList<IWorkbookWindow> HiddenWindows => _windows.Where(_hidden.Contains).ToList();

    /// <summary>True when View Side by Side is currently tiling a pair of windows.</summary>
    public bool IsSideBySideActive => _sideBySidePrimary is not null && _sideBySidePartner is not null;

    /// <summary>True when scrolling one side-by-side window mirrors into its partner.</summary>
    public bool IsSynchronousScrollActive => IsSideBySideActive && _synchronousScroll;

    /// <summary>True when the window is registered and not hidden.</summary>
    public bool IsVisible(IWorkbookWindow window) => _windows.Contains(window) && !_hidden.Contains(window);

    /// <summary>
    /// A window can be hidden only when it is registered, currently visible, and at least one
    /// other window would remain visible (you cannot hide the last visible window).
    /// </summary>
    public bool CanHide(IWorkbookWindow window) =>
        window is not null && IsVisible(window) && VisibleCount > 1;

    /// <summary>Hides the window if <see cref="CanHide"/> allows. Returns true if it was hidden.</summary>
    public bool Hide(IWorkbookWindow window)
    {
        if (!CanHide(window))
            return false;

        _hidden.Add(window);
        window.SetWindowVisible(false);
        return true;
    }

    /// <summary>Restores a hidden window and activates it. Returns true if it was unhidden.</summary>
    public bool Unhide(IWorkbookWindow window)
    {
        if (window is null || !_hidden.Remove(window))
            return false;

        window.SetWindowVisible(true);
        window.ActivateWindow();
        return true;
    }

    /// <summary>
    /// True once at least one window exists; lets a window decide whether it is the first window
    /// (create the workbook) or a secondary view (adopt the existing shared workbook) before it loads.
    /// </summary>
    public bool HasWindows => _windows.Count > 0;

    /// <summary>Adds a window and renumbers every window's Excel-style title suffix.</summary>
    public void Register(IWorkbookWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Contains(window))
            return;

        _windows.Add(window);
        RenumberTitles();
    }

    /// <summary>Removes a closing window and renumbers the survivors.</summary>
    public void Unregister(IWorkbookWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _hidden.Remove(window);
        if (ReferenceEquals(window, _sideBySidePrimary) || ReferenceEquals(window, _sideBySidePartner))
            DisableSideBySide();
        if (_windows.Remove(window))
            RenumberTitles();
    }

    /// <summary>Index of <paramref name="window"/> in registration order, or -1 if not registered.</summary>
    public int IndexOf(IWorkbookWindow window) => _windows.IndexOf(window);

    /// <summary>
    /// The next window to activate when cycling Switch Windows from <paramref name="currentWindow"/>.
    /// Returns null when there is no other window to switch to.
    /// </summary>
    public IWorkbookWindow? NextWindowTarget(IWorkbookWindow currentWindow)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);
        if (_windows.Count == 0)
            return null;

        var currentIndex = _windows.IndexOf(currentWindow);
        var nextIndex = WorkbookWindowOrdering.NextWindowIndex(currentIndex, _windows.Count);
        if (nextIndex == WorkbookWindowOrdering.NoTarget)
            return null;

        var target = _windows[nextIndex];
        return ReferenceEquals(target, currentWindow) ? null : target;
    }

    /// <summary>Activates the next window in the cycle, if there is one. Returns true if it switched.</summary>
    public bool SwitchToNextWindow(IWorkbookWindow currentWindow)
    {
        var target = NextWindowTarget(currentWindow);
        if (target is null)
            return false;

        target.ActivateWindow();
        return true;
    }

    /// <summary>
    /// Tells every window other than <paramref name="origin"/> to refresh its viewport/status from
    /// the shared workbook, so an edit (or undo/redo) in one window appears in the others.
    /// </summary>
    public void NotifyWorkbookChanged(IWorkbookWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (_windows.Count <= 1)
            return;

        var originIndex = _windows.IndexOf(origin);
        foreach (var index in WorkbookWindowOrdering.IndicesToNotify(originIndex, _windows.Count))
            _windows[index].RefreshFromSharedWorkbook();
    }

    // Arrange All

    /// <summary>
    /// Applies an Arrange All layout to every visible workbook window. Hidden windows are left as-is,
    /// matching Excel's distinction between Hide/Unhide and live window arrangement.
    /// </summary>
    public bool ArrangeVisibleWindows(
        WorkbookWindowArrangement arrangement,
        double workAreaWidth,
        double workAreaHeight)
    {
        if (!Enum.IsDefined(arrangement))
            return false;

        var visibleWindows = _windows.Where(w => !_hidden.Contains(w)).ToList();
        if (visibleWindows.Count == 0)
            return false;

        var bounds = ArrangeAllLayoutPlanner.Arrange(
            arrangement,
            workAreaWidth,
            workAreaHeight,
            visibleWindows.Count);
        if (bounds.Count != visibleWindows.Count)
            return false;

        DisableSideBySide();
        for (var index = 0; index < visibleWindows.Count; index++)
            visibleWindows[index].TileToWorkArea(bounds[index]);

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
        primary.TileToWorkArea(primaryBounds);
        partner.TileToWorkArea(partnerBounds);

        _sideBySidePrimary = primary;
        _sideBySidePartner = partner;
        return true;
    }

    /// <summary>Stops side-by-side mode. Layout is left as-is; synchronous scrolling is also turned off.</summary>
    public void DisableSideBySide()
    {
        _sideBySidePrimary = null;
        _sideBySidePartner = null;
        _synchronousScroll = false;
    }

    /// <summary>
    /// Enables or disables synchronous scrolling. Synchronous scrolling is only meaningful while
    /// side-by-side is active; enabling it without an active pair is refused.
    /// </summary>
    public bool SetSynchronousScroll(bool active)
    {
        if (active && !IsSideBySideActive)
            return false;

        _synchronousScroll = active;
        return true;
    }

    /// <summary>
    /// When side-by-side + synchronous scrolling are active, pushes <paramref name="offset"/> from the
    /// originating window into its paired window. Guarded so the partner applying the offset cannot
    /// loop the broadcast back into the origin.
    /// </summary>
    public void BroadcastScrollOffset(IWorkbookWindow origin, WorkbookScrollOffset offset)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!IsSynchronousScrollActive || _applyingBroadcast)
            return;

        var target = SideBySidePartnerOf(origin);
        if (target is null)
            return;

        _applyingBroadcast = true;
        try
        {
            target.SetScrollOffset(offset);
        }
        finally
        {
            _applyingBroadcast = false;
        }
    }

    /// <summary>The side-by-side partner of <paramref name="window"/>, or null if it is not part of the pair.</summary>
    private IWorkbookWindow? SideBySidePartnerOf(IWorkbookWindow window)
    {
        if (!IsSideBySideActive)
            return null;
        if (ReferenceEquals(window, _sideBySidePrimary))
            return _sideBySidePartner;
        if (ReferenceEquals(window, _sideBySidePartner))
            return _sideBySidePrimary;
        return null;
    }

    /// <summary>The next visible window after <paramref name="window"/> in the switch cycle, skipping hidden windows.</summary>
    private IWorkbookWindow? NextVisibleWindow(IWorkbookWindow window)
    {
        if (_windows.Count <= 1)
            return null;

        var startIndex = _windows.IndexOf(window);
        for (var step = 1; step < _windows.Count; step++)
        {
            var candidate = _windows[(startIndex + step) % _windows.Count];
            if (!ReferenceEquals(candidate, window) && !_hidden.Contains(candidate))
                return candidate;
        }

        return null;
    }

    private void RenumberTitles()
    {
        var total = _windows.Count;
        for (var i = 0; i < total; i++)
        {
            var suffix = WorkbookWindowOrdering.FormatWindowTitleSuffix(position: i + 1, totalWindowCount: total);
            _windows[i].ApplyWindowTitleSuffix(suffix);
        }
    }
}
