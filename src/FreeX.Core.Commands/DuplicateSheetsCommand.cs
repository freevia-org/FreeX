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
            CommandOutcome outcome;
            try
            {
                outcome = duplicate.Apply(ctx);
            }
            catch (Exception ex)
            {
                // R175-auditB-F1: duplicate can throw PARTWAY through its own multi-step sheet-copy
                // mutation (cell/style/drawing/table cloning) rather than merely returning a failed
                // CommandOutcome. This is worse than the ordinary rollback-family gap elsewhere in
                // this round: Apply has no outer try/catch of its own, so an uncaught exception here
                // would skip the RevertDuplicates(appliedDuplicates) call below entirely -- losing
                // every EARLIER successful duplicate too, not just this one -- and _applied is only
                // set true at the very end of a fully successful Apply, so even the caller's
                // best-effort Revert(ctx) would no-op on its `if (!_applied) return;` guard and never
                // touch any of it. Mirror CompositeWorkbookCommand's fix: best-effort revert the
                // throwing child first, then unwind every already-applied sibling here (the same
                // order the mutations actually happened in, LIFO via RevertDuplicates), and return a
                // failure outcome that carries the original exception rather than losing it.
                try { duplicate.Revert(ctx); } catch { }
                RevertDuplicates(ctx, appliedDuplicates);
                return new CommandOutcome(false, $"{Label}: {ex.Message}");
            }
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
        CommandOutcome moveOutcome;
        try
        {
            moveOutcome = _moveCopies.Apply(ctx);
        }
        catch (Exception ex)
        {
            // R175-auditB-F1: same reasoning as the duplicate-loop catch above -- _moveCopies can
            // throw partway through its own multi-step move, and since _applied is still false at
            // this point, the caller's Revert(ctx) would otherwise no-op and leave every duplicate
            // sheet this Apply already created permanently in the workbook.
            try { _moveCopies.Revert(ctx); } catch { }
            RevertDuplicates(ctx, appliedDuplicates);
            return new CommandOutcome(false, $"{Label}: {ex.Message}");
        }
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
