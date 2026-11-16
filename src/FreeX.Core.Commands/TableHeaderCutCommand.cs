using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Runs a composite cut with a single table-header transaction for all its cells.</summary>
public sealed class TableHeaderCutCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    private readonly IWorkbookCommand _inner;
    private readonly IReadOnlyList<GridRange> _ranges;
    private TableHeaderRenameTransaction? _headerRename;
    private IReadOnlyList<CellAddress> _affected = [];
    private bool _applied;

    public TableHeaderCutCommand(IWorkbookCommand inner, IReadOnlyList<GridRange> ranges)
    {
        _inner = inner;
        _ranges = ranges;
    }

    public string Label => _inner.Label;
    public IReadOnlyList<CellAddress> AffectedCells => _affected;
    public int EstimatedBytes => (int)Math.Min(int.MaxValue,
        (long)(_inner is IEstimatesMemory estimated ? estimated.EstimatedBytes : 200) + (_headerRename?.EstimatedBytes ?? 0));

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _applied = false;
        var headers = _ranges.SelectMany(range =>
        {
            var sheet = ctx.GetSheet(range.Start.Sheet);
            return TableHeaderRenameTransaction.IntersectingHeaders(sheet, range);
        });
        _headerRename = TableHeaderRenameTransaction.Capture(ctx, headers);
        CommandOutcome outcome;
        try { outcome = TableHeaderRenameTransaction.Suppress(() => _inner.Apply(ctx)); }
        catch
        {
            TableHeaderRenameTransaction.Suppress(() => _inner.Revert(ctx));
            throw;
        }
        if (!outcome.Success || outcome.IsNoOp) return outcome;
        _applied = true;
        try { _headerRename.Apply(ctx); }
        catch (InvalidOperationException ex)
        {
            Revert(ctx);
            return new CommandOutcome(false, ex.Message);
        }
        _affected = (outcome.AffectedCells ?? []).Concat(_headerRename.Affected).Distinct().ToArray();
        return outcome with { AffectedCells = _affected };
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        _headerRename?.Revert();
        TableHeaderRenameTransaction.Suppress(() => _inner.Revert(ctx));
        _applied = false;
    }
}
