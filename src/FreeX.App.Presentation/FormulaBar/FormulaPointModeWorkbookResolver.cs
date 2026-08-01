using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

/// <summary>
/// A range selected in a workbook window while another workbook owns a live formula edit.
/// Keeping the workbook and sheet names beside the real range prevents the host from
/// accidentally resolving the source coordinates against the edit owner's workbook.
/// </summary>
public readonly record struct FormulaPointModeSelection(
    WorkbookId WorkbookId,
    string WorkbookName,
    string SheetName,
    GridRange Range);

/// <summary>
/// Host-neutral surface used by WPF and Avalonia to route a point-mode gesture to the window
/// that owns the formula edit. The source window remains responsible for painting its selection;
/// the owner remains responsible for text replacement, commit, and cancellation.
/// </summary>
public interface IFormulaPointModeWorkbookWindow
{
    WorkbookId DocumentId { get; }
    string WorkbookName { get; }
    bool HasActiveFormulaPointMode { get; }

    bool AcceptFormulaPointModeSelection(
        FormulaPointModeSelection selection,
        bool append,
        bool extendSelection);

    void ShowFormulaPointModeSourceSelection(GridRange range);

    bool CommitOwnedFormulaPointModeEdit();

    bool CancelOwnedFormulaPointModeEdit();

    bool CycleOwnedFormulaPointModeReference();
}

/// <summary>
/// Resolves the live edit owner without knowing anything about WPF, Avalonia, or their window
/// controls. A source window is preferred when it owns the edit; otherwise the first registered
/// active owner is used. Registries preserve registration order, so the result is deterministic.
/// </summary>
public static class FormulaPointModeWorkbookResolver
{
    public static IFormulaPointModeWorkbookWindow? ResolveOwner(
        IEnumerable<IFormulaPointModeWorkbookWindow> windows,
        IFormulaPointModeWorkbookWindow sourceWindow)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(sourceWindow);

        if (sourceWindow.HasActiveFormulaPointMode)
            return sourceWindow;

        return windows.FirstOrDefault(window =>
            !ReferenceEquals(window, sourceWindow) && window.HasActiveFormulaPointMode);
    }

    public static bool TryRouteSelection(
        IEnumerable<IFormulaPointModeWorkbookWindow> windows,
        IFormulaPointModeWorkbookWindow sourceWindow,
        FormulaPointModeSelection selection,
        bool append = false,
        bool extendSelection = false)
    {
        var owner = ResolveOwner(windows, sourceWindow);
        if (owner is null)
            return false;

        var accepted = owner.AcceptFormulaPointModeSelection(selection, append, extendSelection);
        if (accepted && !ReferenceEquals(owner, sourceWindow))
            sourceWindow.ShowFormulaPointModeSourceSelection(selection.Range);

        return accepted;
    }

    public static bool TryRouteCommit(
        IEnumerable<IFormulaPointModeWorkbookWindow> windows,
        IFormulaPointModeWorkbookWindow sourceWindow) =>
        ResolveOwner(windows, sourceWindow)?.CommitOwnedFormulaPointModeEdit() == true;

    public static bool TryRouteCancel(
        IEnumerable<IFormulaPointModeWorkbookWindow> windows,
        IFormulaPointModeWorkbookWindow sourceWindow) =>
        ResolveOwner(windows, sourceWindow)?.CancelOwnedFormulaPointModeEdit() == true;

    public static bool TryRouteReferenceCycle(
        IEnumerable<IFormulaPointModeWorkbookWindow> windows,
        IFormulaPointModeWorkbookWindow sourceWindow) =>
        ResolveOwner(windows, sourceWindow)?.CycleOwnedFormulaPointModeReference() == true;
}
