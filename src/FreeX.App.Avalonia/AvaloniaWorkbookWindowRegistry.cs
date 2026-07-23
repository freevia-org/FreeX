using FreeX.App.Presentation.Shell;
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
