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

public enum FormulaPointModeSelectionMode
{
    Replace,
    Append,
}

/// <summary>
/// Host-neutral selection plan prepared for the workbook that owns the formula edit. The owner
/// receives the already-normalized external workbook qualifier and only applies the text edit.
/// </summary>
public readonly record struct FormulaPointModeEditSelection(
    string SheetName,
    GridRange Range,
    string? ExternalWorkbookName,
    FormulaPointModeSelectionMode Mode,
    bool ExtendSelection);

public enum FormulaPointModeCommand
{
    Commit,
    Cancel,
    CycleReference,
}

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

    bool AcceptFormulaPointModeSelection(FormulaPointModeEditSelection selection);

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
    public static bool IsActive(
        bool hasRangeEditor,
        bool pointMode,
        bool hasFormulaEditCell) =>
        hasRangeEditor && pointMode && hasFormulaEditCell;

    public static bool TryCreateSelection(
        Workbook workbook,
        GridRange range,
        out FormulaPointModeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheet = workbook.GetSheet(range.Start.Sheet);
        if (sheet is null)
        {
            selection = default;
            return false;
        }

        selection = new FormulaPointModeSelection(
            workbook.Id,
            workbook.Name,
            sheet.Name,
            range);
        return true;
    }

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

        var editSelection = new FormulaPointModeEditSelection(
            selection.SheetName,
            selection.Range,
            selection.WorkbookId == owner.DocumentId ? null : selection.WorkbookName,
            append ? FormulaPointModeSelectionMode.Append : FormulaPointModeSelectionMode.Replace,
            extendSelection);
        var accepted = owner.AcceptFormulaPointModeSelection(editSelection);
        if (accepted && !ReferenceEquals(owner, sourceWindow))
            sourceWindow.ShowFormulaPointModeSourceSelection(selection.Range);

        return accepted;
    }

    public static bool TryRouteCommand(
        IEnumerable<IFormulaPointModeWorkbookWindow> windows,
        IFormulaPointModeWorkbookWindow sourceWindow,
        FormulaPointModeCommand command)
    {
        var owner = ResolveOwner(windows, sourceWindow);
        if (owner is null)
            return false;

        return command switch
        {
            FormulaPointModeCommand.Commit => owner.CommitOwnedFormulaPointModeEdit(),
            FormulaPointModeCommand.Cancel => owner.CancelOwnedFormulaPointModeEdit(),
            FormulaPointModeCommand.CycleReference => owner.CycleOwnedFormulaPointModeReference(),
            _ => false,
        };
    }
}
