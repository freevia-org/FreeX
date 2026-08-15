using Free.Shared.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Shell;

public enum WorkbookWindowCycleDirection
{
    Forward,
    Backward,
}

public enum WorkbookWindowNotificationAudience
{
    SameDocument,
    SameDocumentExceptOrigin,
    AllExceptOrigin,
}

public sealed record WorkbookWindowArrangementTarget<TWindow>(
    TWindow Window,
    ShellRect Bounds);

/// <summary>
/// Renderer-neutral ownership of live workbook-window registration, document grouping, title
/// numbering, notification audiences, and switch-window cycling. The registry deliberately holds
/// strong references until <see cref="Unregister"/> so native shells retain their existing explicit
/// close/unregister lifetime contract.
/// </summary>
public sealed class WorkbookWindowRegistryCore<TWindow>
    where TWindow : class
{
    private readonly List<TWindow> _windows = [];
    private readonly Func<TWindow, WorkbookId> _documentId;
    private readonly Func<TWindow, bool> _isVisible;
    private readonly Action<TWindow, string> _applyTitleSuffix;

    public WorkbookWindowRegistryCore(
        Func<TWindow, WorkbookId> documentId,
        Func<TWindow, bool> isVisible,
        Action<TWindow, string> applyTitleSuffix)
    {
        _documentId = documentId ?? throw new ArgumentNullException(nameof(documentId));
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        _applyTitleSuffix = applyTitleSuffix ?? throw new ArgumentNullException(nameof(applyTitleSuffix));
    }

    public IReadOnlyList<TWindow> Windows => _windows;

    public IReadOnlyList<TWindow> VisibleWindows => _windows.Where(_isVisible).ToArray();

    public int Count => _windows.Count;

    public bool HasWindows => _windows.Count > 0;

    public bool Register(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Contains(window))
            return false;

        _windows.Add(window);
        RefreshWindowNumbering();
        return true;
    }

    public bool Unregister(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!_windows.Remove(window))
            return false;

        RefreshWindowNumbering();
        return true;
    }

    public int IndexOf(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _windows.IndexOf(window);
    }

    public bool HasOtherWindowForDocument(TWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var documentId = _documentId(window);
        return _windows.Any(candidate =>
            !ReferenceEquals(candidate, window) && _documentId(candidate) == documentId);
    }

    public bool HasWindowForDocument(WorkbookId documentId) =>
        _windows.Any(candidate => _documentId(candidate) == documentId);

    /// <summary>
    /// Selects visible windows in registration order and pairs them with the shared Arrange All
    /// geometry. Hidden windows are never returned, preserving the separate Hide/Unhide workflow.
    /// </summary>
    public IReadOnlyList<WorkbookWindowArrangementTarget<TWindow>> PlanVisibleArrangement(
        ShellWindowArrangement arrangement,
        double workAreaWidth,
        double workAreaHeight,
        Func<TWindow, bool>? include = null)
    {
        if (!Enum.IsDefined(arrangement))
            return [];

        var windows = _windows
            .Where(_isVisible)
            .Where(window => include?.Invoke(window) ?? true)
            .ToArray();
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            arrangement,
            workAreaWidth,
            workAreaHeight,
            windows.Length);
        if (bounds.Count != windows.Length)
            return [];

        return windows
            .Select((window, index) => new WorkbookWindowArrangementTarget<TWindow>(window, bounds[index]))
            .ToArray();
    }

    public IReadOnlyList<TWindow> NotificationTargets(
        TWindow origin,
        WorkbookWindowNotificationAudience audience)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(audience))
            throw new ArgumentOutOfRangeException(nameof(audience));

        var documentId = _documentId(origin);
        return _windows
            .Where(candidate => audience switch
            {
                WorkbookWindowNotificationAudience.SameDocument =>
                    _documentId(candidate) == documentId,
                WorkbookWindowNotificationAudience.SameDocumentExceptOrigin =>
                    !ReferenceEquals(candidate, origin) && _documentId(candidate) == documentId,
                WorkbookWindowNotificationAudience.AllExceptOrigin =>
                    !ReferenceEquals(candidate, origin),
                _ => false,
            })
            .ToArray();
    }

    public void Notify(
        TWindow origin,
        WorkbookWindowNotificationAudience audience,
        Action<TWindow> notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        foreach (var target in NotificationTargets(origin, audience))
            notification(target);
    }

    public TWindow? NextWindowTarget(
        TWindow currentWindow,
        WorkbookWindowCycleDirection direction)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));

        var visibleWindows = _windows.Where(_isVisible).ToList();
        if (visibleWindows.Count <= 1)
            return null;

        var currentIndex = visibleWindows.IndexOf(currentWindow);
        if (currentIndex < 0)
            return null;

        var targetIndex = direction == WorkbookWindowCycleDirection.Forward
            ? WorkbookWindowOrdering.NextWindowIndex(currentIndex, visibleWindows.Count)
            : WorkbookWindowOrdering.PreviousWindowIndex(currentIndex, visibleWindows.Count);
        if (targetIndex == WorkbookWindowOrdering.NoTarget)
            return null;

        var target = visibleWindows[targetIndex];
        return ReferenceEquals(target, currentWindow) ? null : target;
    }

    public bool SwitchToWindow(
        TWindow currentWindow,
        WorkbookWindowCycleDirection direction,
        Action<TWindow> activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        var target = NextWindowTarget(currentWindow, direction);
        if (target is null)
            return false;

        activate(target);
        return true;
    }

    public void RefreshWindowNumbering()
    {
        var totals = new Dictionary<WorkbookId, int>();
        foreach (var window in _windows)
        {
            var documentId = _documentId(window);
            totals.TryGetValue(documentId, out var total);
            totals[documentId] = total + 1;
        }

        var positions = new Dictionary<WorkbookId, int>();
        foreach (var window in _windows)
        {
            var documentId = _documentId(window);
            positions.TryGetValue(documentId, out var previousPosition);
            var position = previousPosition + 1;
            positions[documentId] = position;
            _applyTitleSuffix(window, WorkbookWindowOrdering.FormatWindowTitleSuffix(
                position,
                totals[documentId]));
        }
    }
}
