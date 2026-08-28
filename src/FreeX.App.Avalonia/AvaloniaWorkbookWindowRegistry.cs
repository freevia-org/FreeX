using FreeX.App.Presentation.Shell;
using FreeX.App.Presentation.FormulaBar;
using Free.Shared.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Adapts Avalonia workbook windows to the shared renderer-neutral registry core. Native
/// visibility, activation, and refresh operations remain local to this shell.
/// </summary>
internal sealed class AvaloniaWorkbookWindowRegistry
{
    private readonly WorkbookWindowRegistryCore<MainWindow> _core = new(
        static window => window.DocumentId,
        static _ => true,
        static (window, suffix) => window.ApplyWindowTitleSuffix(suffix));

    internal IReadOnlyList<MainWindow> Windows => _core.Windows;

    internal IReadOnlyList<IFormulaPointModeWorkbookWindow> FormulaPointModeWindows =>
        _core.VisibleWindows
            .Cast<IFormulaPointModeWorkbookWindow>()
            .ToArray();

    internal IReadOnlyList<MainWindow> VisibleWindows => _core.VisibleWindows;

    internal IReadOnlyList<MainWindow> HiddenWindows => _core.HiddenWindows;

    internal int Count => _core.Count;

    internal int VisibleCount => _core.VisibleCount;

    internal int IndexOf(MainWindow window) => _core.IndexOf(window);

    internal bool CanHide(MainWindow window) => _core.CanHide(window);

    internal bool Hide(MainWindow window)
    {
        if (!_core.Hide(window))
            return false;

        window.Hide();
        return true;
    }

    internal bool Unhide(MainWindow window)
    {
        if (!_core.Unhide(window))
            return false;

        window.ActivateWorkbookWindow();
        window.RefreshWindowVisibilityCommandStates();
        NotifyVisibilityChanged(window);
        return true;
    }

    internal IReadOnlyList<WorkbookWindowArrangementTarget<MainWindow>> PlanVisibleArrangement(
        WorkbookWindowArrangement arrangement,
        double workAreaWidth,
        double workAreaHeight) =>
        _core.PlanVisibleArrangement(
            (ShellWindowArrangement)arrangement,
            workAreaWidth,
            workAreaHeight);

    internal void Register(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_core.Register(window))
            NotifyVisibilityChanged(window);
    }

    internal void Unregister(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_core.Unregister(window))
            NotifyVisibilityChanged(window);
    }

    internal bool HasOtherWindowForDocument(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return _core.HasOtherWindowForDocument(window);
    }

    internal void NotifyWorkbookChanged(MainWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.SameDocumentExceptOrigin,
            static window => window.RefreshFromSharedWorkbook());
        _core.RefreshWindowNumbering();
    }

    internal void RefreshWindowNumbering() => _core.RefreshWindowNumbering();

    internal void NotifyVisibilityChanged(MainWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.AllExceptOrigin,
            static window => window.RefreshWindowVisibilityCommandStates());
    }

    /// <summary>
    /// Broadcasts a Quick Access Toolbar customization (command set or below-ribbon placement) to
    /// every OTHER registered window in the process, regardless of document. The QAT customization
    /// lives in the single process-wide <c>FreeXOptionsRuntimeSession.LiveOptions</c> instance every
    /// window shares -- <see cref="MainWindow"/>'s View &gt; New Window handler passes the SAME
    /// <c>_optionsRuntimeSession</c> into every sibling window it creates -- so a change applied via
    /// one window's QAT context menu or its Options dialog must be reflected live in every sibling
    /// window's toolbar chrome too, instead of only the next time each sibling happens to rebuild its
    /// own QAT for an unrelated reason. Mirrors the WPF host's
    /// <c>WorkbookWindowRegistry.BroadcastQuickAccessToolbarChanged</c> (same shared-options
    /// defect, same fix shape), adapted to this registry's existing pattern of notifying the
    /// concrete <see cref="MainWindow"/> type directly rather than through an <c>IWorkbookWindow</c>
    /// seam.
    /// </summary>
    internal void NotifyQuickAccessToolbarChanged(MainWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        _core.Notify(
            origin,
            WorkbookWindowNotificationAudience.AllExceptOrigin,
            static window => window.ApplyQuickAccessToolbarChanged());
    }

    internal MainWindow? NextWindowTarget(MainWindow currentWindow, bool forward)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);

        return _core.NextWindowTarget(
            currentWindow,
            forward ? WorkbookWindowCycleDirection.Forward : WorkbookWindowCycleDirection.Backward);
    }

    internal bool SwitchToWindow(MainWindow currentWindow, bool forward)
    {
        return _core.SwitchToWindow(
            currentWindow,
            forward ? WorkbookWindowCycleDirection.Forward : WorkbookWindowCycleDirection.Backward,
            static target => target.ActivateWorkbookWindow());
    }
}
