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
        static window => window.IsVisible,
        static (window, suffix) => window.ApplyWindowTitleSuffix(suffix));

    internal IReadOnlyList<MainWindow> Windows => _core.Windows;

    internal IReadOnlyList<IFormulaPointModeWorkbookWindow> FormulaPointModeWindows =>
        _core.VisibleWindows
            .Cast<IFormulaPointModeWorkbookWindow>()
            .ToArray();

    internal IReadOnlyList<MainWindow> VisibleWindows => _core.VisibleWindows;

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
        _core.Register(window);
    }

    internal void Unregister(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _core.Unregister(window);
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
