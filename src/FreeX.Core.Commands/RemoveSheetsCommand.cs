using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Removes an ordered worksheet selection as one undoable structural operation.
/// </summary>
public sealed class RemoveSheetsCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand, IEstimatesMemory
{
    private readonly CompositeWorkbookCommand _composite;

    public RemoveSheetsCommand(IReadOnlyList<SheetId> sheetIds)
    {
        ArgumentNullException.ThrowIfNull(sheetIds);
        _composite = new CompositeWorkbookCommand(
            sheetIds.Count == 1 ? "Delete Sheet" : "Delete Sheets",
            sheetIds
                .Distinct()
                .Select(static sheetId => (IWorkbookCommand)new RemoveSheetCommand(sheetId))
                .ToArray());
    }

    public string Label => _composite.Label;

    public int EstimatedBytes => _composite.EstimatedBytes;

    public CommandOutcome Apply(ICommandContext ctx) => _composite.Apply(ctx);

    public void Revert(ICommandContext ctx) => _composite.Revert(ctx);
}
