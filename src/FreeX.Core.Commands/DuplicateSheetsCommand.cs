using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Duplicates an ordered worksheet selection and inserts the copies as one contiguous group.
/// The whole operation is one atomic undo/redo entry, unlike renderer-owned duplicate-then-move
/// sequences that can leave a partial copy behind or require two undo steps.
/// </summary>
public sealed class DuplicateSheetsCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand, IEstimatesMemory
{
    private readonly IReadOnlyList<SheetId> _sourceSheetIds;
    private readonly int _insertBeforeIndex;
    private readonly string _label;
    private readonly List<DuplicateSheetCommand> _duplicates = [];
    private MoveSheetsCommand? _moveCopies;
    private bool _applied;

    public DuplicateSheetsCommand(
        IReadOnlyList<SheetId> sourceSheetIds,
        int insertBeforeIndex,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(sourceSheetIds);
        _sourceSheetIds = sourceSheetIds.ToArray();
        _insertBeforeIndex = insertBeforeIndex;
        _label = string.IsNullOrWhiteSpace(label)
            ? (_sourceSheetIds.Count == 1 ? "Move or Copy Sheet" : "Move or Copy Sheets")
            : label;
    }

    public string Label => _label;

    public IReadOnlyList<SheetId> CopySheetIds => _duplicates
        .Select(command => command.CopySheetId)
        .Where(static id => id.HasValue)
        .Select(static id => id!.Value)
        .ToArray();

    public int EstimatedBytes => _duplicates.Sum(static command => command.EstimatedBytes);

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_insertBeforeIndex < 0 || _insertBeforeIndex > ctx.Workbook.Sheets.Count)
            return new CommandOutcome(false, "Sheet index is out of range.");

        var sourceIds = ctx.Workbook.Sheets
            .Select(sheet => sheet.Id)
            .Where(_sourceSheetIds.Contains)
            .ToArray();
        if (sourceIds.Length == 0)
            return new CommandOutcome(false, "Source sheet was not found.");

        var targetSheetId = _insertBeforeIndex < ctx.Workbook.Sheets.Count
            ? ctx.Workbook.Sheets[_insertBeforeIndex].Id
            : (SheetId?)null;

        if (_duplicates.Count == 0)
            _duplicates.AddRange(sourceIds.Select(static id => new DuplicateSheetCommand(id)));

        var appliedDuplicates = new List<DuplicateSheetCommand>(_duplicates.Count);
        foreach (var duplicate in _duplicates)
        {
            var outcome = duplicate.Apply(ctx);
            if (!outcome.Success)
            {
                RevertDuplicates(ctx, appliedDuplicates);
                return outcome;
            }

            appliedDuplicates.Add(duplicate);
        }

        var copyIds = CopySheetIds;
        var targetIndex = targetSheetId is { } target
            ? FindSheetIndex(ctx.Workbook, target)
            : ctx.Workbook.Sheets.Count;
        if (targetIndex < 0)
            targetIndex = ctx.Workbook.Sheets.Count;

        _moveCopies ??= new MoveSheetsCommand(copyIds, targetIndex);
        var moveOutcome = _moveCopies.Apply(ctx);
        if (!moveOutcome.Success)
        {
            RevertDuplicates(ctx, appliedDuplicates);
            return moveOutcome;
        }

        _applied = true;
        return new CommandOutcome(true, IsNoOp: false);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        _moveCopies?.Revert(ctx);
        RevertDuplicates(ctx, _duplicates);
        _applied = false;
    }

    private static void RevertDuplicates(ICommandContext ctx, IReadOnlyList<DuplicateSheetCommand> duplicates)
    {
        for (var index = duplicates.Count - 1; index >= 0; index--)
            duplicates[index].Revert(ctx);
    }

    private static int FindSheetIndex(Workbook workbook, SheetId sheetId)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
            if (workbook.Sheets[index].Id == sheetId)
                return index;

        return -1;
    }
}
