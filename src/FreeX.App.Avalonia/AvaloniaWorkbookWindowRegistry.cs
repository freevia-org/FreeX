using FreeX.App.Presentation.Shell;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Coordinates Avalonia workbook views. The WPF host has an equivalent registry, but its window
/// contract is WPF-specific; keeping this coordinator here lets both shells share the same pure
/// ordering and layout planners without making the cross-platform shell depend on WPF.
/// </summary>
internal sealed class AvaloniaWorkbookWindowRegistry
{
    private readonly List<MainWindow> _windows = [];

    internal IReadOnlyList<MainWindow> Windows => _windows;

    internal IReadOnlyList<IFormulaPointModeWorkbookWindow> FormulaPointModeWindows =>
        _windows
            .Where(static window => window.IsVisible)
            .Cast<IFormulaPointModeWorkbookWindow>()
            .ToArray();

    internal IReadOnlyList<MainWindow> VisibleWindows =>
        _windows.Where(static window => window.IsVisible).ToArray();

    internal void Register(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Contains(window))
            return;

        _windows.Add(window);
        RenumberTitles();
    }

    internal void Unregister(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Remove(window))
            RenumberTitles();
    }

    internal bool HasOtherWindowForDocument(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _windows.Any(candidate =>
            !ReferenceEquals(candidate, window) && candidate.DocumentId == window.DocumentId);
    }

    internal void NotifyWorkbookChanged(MainWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        foreach (var window in _windows.ToArray())
        {
            if (ReferenceEquals(window, origin) || window.DocumentId != origin.DocumentId)
                continue;

            window.RefreshFromSharedWorkbook();
        }

        RenumberTitles();
    }

    internal void RefreshWindowNumbering() => RenumberTitles();

    internal MainWindow? NextWindowTarget(MainWindow currentWindow, bool forward)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);

        var visibleWindows = VisibleWindows.ToList();
        if (visibleWindows.Count <= 1)
            return null;

        var currentIndex = visibleWindows.IndexOf(currentWindow);
        if (currentIndex < 0)
            return null;

        var nextIndex = (currentIndex + (forward ? 1 : -1) + visibleWindows.Count) % visibleWindows.Count;
        var target = visibleWindows[nextIndex];
        return ReferenceEquals(target, currentWindow) ? null : target;
    }

    internal bool SwitchToWindow(MainWindow currentWindow, bool forward)
    {
        var target = NextWindowTarget(currentWindow, forward);
        if (target is null)
            return false;

        target.ActivateWorkbookWindow();
        return true;
    }

    private void RenumberTitles()
    {
        var totals = new Dictionary<WorkbookId, int>();
        foreach (var window in _windows)
        {
            totals.TryGetValue(window.DocumentId, out var total);
            totals[window.DocumentId] = total + 1;
        }

        var positions = new Dictionary<WorkbookId, int>();
        foreach (var window in _windows)
        {
            positions.TryGetValue(window.DocumentId, out var previous);
            var position = previous + 1;
            positions[window.DocumentId] = position;
            window.ApplyWindowTitleSuffix(WorkbookWindowOrdering.FormatWindowTitleSuffix(
                position,
                totals[window.DocumentId]));
        }
    }
}
