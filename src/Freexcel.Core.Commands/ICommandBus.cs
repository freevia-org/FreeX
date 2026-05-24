using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Every mutation to the workbook goes through this bus as a command.
/// This enables undo/redo and future collaboration/AI action replay.
/// </summary>
public interface ICommandBus
{
    /// <summary>Execute a command and push it onto the undo stack.</summary>
    CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command);

    /// <summary>Execute a command that can be repeated with F4-style semantics.</summary>
    CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory);

    /// <summary>Undo the last command.</summary>
    CommandOutcome Undo(WorkbookId workbookId);

    /// <summary>Redo a previously undone command.</summary>
    CommandOutcome Redo(WorkbookId workbookId);

    /// <summary>Check if undo is available.</summary>
    bool CanUndo(WorkbookId workbookId);

    /// <summary>Check if redo is available.</summary>
    bool CanRedo(WorkbookId workbookId);

    /// <summary>Repeat the last repeatable command.</summary>
    CommandOutcome RepeatLast(WorkbookId workbookId);

    /// <summary>Check if a repeatable command is available.</summary>
    bool CanRepeat(WorkbookId workbookId);
}

/// <summary>A command that can be applied and reverted on a workbook.</summary>
public interface IWorkbookCommand
{
    /// <summary>Human-readable label for undo/redo UI.</summary>
    string Label { get; }

    /// <summary>Apply the command, returning a snapshot for undo.</summary>
    CommandOutcome Apply(ICommandContext ctx);

    /// <summary>Revert the command using the saved snapshot.</summary>
    void Revert(ICommandContext ctx);
}

public interface IAffectedCellsCommand
{
    IReadOnlyList<CellAddress> AffectedCells { get; }
}

/// <summary>Context provided to commands for accessing workbook state.</summary>
public interface ICommandContext
{
    Workbook Workbook { get; }
    Sheet GetSheet(SheetId sheetId);
}

/// <summary>Result of executing a command.</summary>
public sealed record CommandOutcome(
    bool Success,
    string? ErrorMessage = null,
    IReadOnlyList<CellAddress>? AffectedCells = null);
