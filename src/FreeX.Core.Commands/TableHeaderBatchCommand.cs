using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Runs a composite cell operation with one table-header transaction for all its cells.</summary>
public sealed class TableHeaderBatchCommand : IWorkbookCommand, IAffectedCellsCommand, IEstimatesMemory
{
    private readonly IWorkbookCommand _inner;
    private readonly IReadOnlyList<GridRange> _ranges;
    private TableHeaderRenameTransaction? _headerRename;
    private IReadOnlyList<CellAddress> _affected = [];
    private bool _applied;

    public TableHeaderBatchCommand(IWorkbookCommand inner, IReadOnlyList<GridRange> ranges)
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
        }).Distinct().ToArray();
        _headerRename = TableHeaderRenameTransaction.Capture(ctx, headers);
        var originalHeaders = headers.Select(address =>
        {
            var sheet = ctx.GetSheet(address.Sheet);
            return (Address: address, Formula: sheet.GetCell(address)?.FormulaText, Value: sheet.GetValue(address));
        }).ToArray();
        CommandOutcome outcome;
        try { outcome = TableHeaderRenameTransaction.Suppress(() => _inner.Apply(ctx)); }
        catch
        {
            try { TableHeaderRenameTransaction.Suppress(() => _inner.Revert(ctx)); }
            catch { /* Preserve the original apply failure. */ }
            throw;
        }
        if (!outcome.Success || outcome.IsNoOp) return outcome;
        _applied = true;
        // A formatting-only batch may touch table headers without changing their content.
        // It must not normalize those headers or reject unrelated malformed table metadata.
        var changedHeaders = originalHeaders.Where(original =>
        {
            var sheet = ctx.GetSheet(original.Address.Sheet);
            return sheet.GetCell(original.Address)?.FormulaText != original.Formula ||
                !Equals(sheet.GetValue(original.Address), original.Value);
        }).Select(original => original.Address).ToHashSet();
        if (changedHeaders.Count == 0)
        {
            _affected = outcome.AffectedCells ?? [];
            return outcome;
        }
        try { _headerRename.Apply(ctx, changedHeaders); }
        catch (InvalidOperationException ex)
        {
            try { Revert(ctx); }
            catch { /* Preserve the original header reconciliation failure. */ }
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
