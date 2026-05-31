using System.Collections.Generic;
using System.Linq;

namespace FreeX.App.Host;

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
}

/// <summary>
/// Tracks the live workbook windows that all view the single shared workbook (Excel-style
/// "New Window"). The registry is a thin coordinator: every ordering decision (which window to
/// switch to, how to number titles, which windows to refresh) is delegated to the pure, unit-tested
/// <see cref="WorkbookWindowOrdering"/> helper.
///
/// Registered as a DI singleton so all windows over the shared workbook see the same registry.
/// </summary>
public sealed class WorkbookWindowRegistry
{
    private readonly List<IWorkbookWindow> _windows = [];

    /// <summary>Live windows in registration order.</summary>
    public IReadOnlyList<IWorkbookWindow> Windows => _windows;

    public int Count => _windows.Count;

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
