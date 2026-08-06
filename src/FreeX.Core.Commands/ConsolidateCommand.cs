using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum ConsolidateFunction
{
    Sum,
    Count,
    Average,
    Max,
    Min,
    Product,
    CountNumbers,
    StdDev,
    StdDevp,
    Var,
    Varp
}

public sealed class ConsolidateCommand : IWorkbookCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _snapshot below captures a (Cell?, StyleId?) pair for every
    // destination cell written by the consolidation, the same shape MoveRangeCommand/CopyRangeCommand
    // use 400 bytes/cell for. Consolidating many source ranges into a large destination should
    // count proportionally, not the flat 200-byte default.
    private const int BytesPerCell = 400;

    private const string SourceBoundsMessage = "Consolidate source ranges must stay inside the worksheet bounds.";
    private const string DestinationBoundsMessage = "Consolidate destination range is outside the worksheet bounds.";

    private readonly IReadOnlyList<GridRange> _sourceRanges;
    private readonly CellAddress _destination;
    private readonly ConsolidateFunction _function;
    private readonly bool _useTopRowLabels;
    private readonly bool _useLeftColumnLabels;
    private readonly bool _createLinksToSourceData;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Consolidate";

    public int EstimatedBytes => (int)Math.Min((long)(_snapshot?.Count ?? 0) * BytesPerCell, int.MaxValue);

    public ConsolidateCommand(
        IReadOnlyList<GridRange> sourceRanges,
        CellAddress destination,
        ConsolidateFunction function = ConsolidateFunction.Sum,
        bool useTopRowLabels = false,
        bool useLeftColumnLabels = false,
        bool createLinksToSourceData = false)
    {
        _sourceRanges = sourceRanges;
        _destination = destination;
        _function = function;
        _useTopRowLabels = useTopRowLabels;
        _useLeftColumnLabels = useLeftColumnLabels;
        _createLinksToSourceData = createLinksToSourceData;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRanges.Count == 0)
            return new CommandOutcome(false, "At least one source range is required.");
        if (ctx.Workbook.GetSheet(_destination.Sheet) is null)
            return new CommandOutcome(false, "Consolidate destination must belong to this workbook.");
        if (_sourceRanges.Any(range => ctx.Workbook.GetSheet(range.Start.Sheet) is null))
            return new CommandOutcome(false, "Consolidate source ranges must belong to this workbook.");
        if (_sourceRanges.Any(range => !WorksheetBounds.IsValidAddress(range.Start) || !WorksheetBounds.IsValidAddress(range.End)))
            return new CommandOutcome(false, SourceBoundsMessage);
        if (!WorksheetBounds.IsValidAddress(_destination))
            return new CommandOutcome(false, DestinationBoundsMessage);

        // Consolidate-by-category (labels) matches source rows/columns by their label text, so
        // real Excel explicitly allows differently-shaped/sized source ranges in that mode (that is
        // the feature's primary use case). Only the position-based mode requires identically-sized
        // ranges, since it aligns cells purely by offset with no label matching to fall back on.
        if (_useTopRowLabels || _useLeftColumnLabels)
            return ApplyByLabels(ctx);

        var rowCount = _sourceRanges[0].RowCount;
        var colCount = _sourceRanges[0].ColCount;
        if (_sourceRanges.Any(r => r.RowCount != rowCount || r.ColCount != colCount))
            return new CommandOutcome(false, "Consolidate source ranges must be the same size.");
        if (!WorksheetBounds.TryGetRectangleEnd(_destination, rowCount, colCount, out _))
            return new CommandOutcome(false, DestinationBoundsMessage);

        return ApplyByPosition(ctx, rowCount, colCount);
    }

    private CommandOutcome ApplyByPosition(ICommandContext ctx, uint rowCount, uint colCount)
    {
        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        if (destinationSheet.IsProtected)
        {
            for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                for (uint colOffset = 0; colOffset < colCount; colOffset++)
                {
                    var address = new CellAddress(_destination.Sheet, _destination.Row + rowOffset, _destination.Col + colOffset);
                    if (!CommandGuards.CanEditCell(ctx.Workbook, destinationSheet, address))
                        return CommandGuards.RejectSheetProtected();
                }
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>();

        for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
        {
            for (uint colOffset = 0; colOffset < colCount; colOffset++)
            {
                var values = new List<double>();
                var nonEmptyCount = 0;
                var sourceAddresses = new List<CellAddress>();
                foreach (var range in _sourceRanges)
                {
                    var sourceSheet = ctx.GetSheet(range.Start.Sheet);
                    var sourceAddress = new CellAddress(
                        range.Start.Sheet,
                        range.Start.Row + rowOffset,
                        range.Start.Col + colOffset);
                    sourceAddresses.Add(sourceAddress);
                    var value = sourceSheet.GetValue(sourceAddress.Row, sourceAddress.Col);
                    if (value is not BlankValue)
                        nonEmptyCount++;
                    if (value is NumberValue number)
                        values.Add(number.Value);
                }

                var destinationAddress = new CellAddress(
                    _destination.Sheet,
                    _destination.Row + rowOffset,
                    _destination.Col + colOffset);
                _snapshot.Add((destinationAddress, destinationSheet.GetCell(destinationAddress)?.Clone(), destinationSheet.GetStyleOnly(destinationAddress.Row, destinationAddress.Col)));

                var newCell = Cell.FromValue(new NumberValue(ConsolidationRules.Aggregate(values, nonEmptyCount, _function)));
                if (_createLinksToSourceData)
                    newCell.FormulaText = ConsolidationRules.CreateSourceLinkFormula(ctx.Workbook, sourceAddresses, _destination.Sheet, _function);
                if (destinationSheet.GetCell(destinationAddress) is { } oldCell)
                    newCell.StyleId = oldCell.StyleId;
                destinationSheet.SetCell(destinationAddress, newCell);
                affected.Add(destinationAddress);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    private CommandOutcome ApplyByLabels(ICommandContext ctx)
    {
        var bodyStartRow = _useTopRowLabels ? 1u : 0u;
        var bodyStartCol = _useLeftColumnLabels ? 1u : 0u;
        // Each source range is checked against its own size (not a shared size), since by-label
        // consolidation allows differently-shaped source ranges.
        if (_sourceRanges.Any(r => bodyStartRow >= r.RowCount || bodyStartCol >= r.ColCount))
            return new CommandOutcome(false, "Consolidate source ranges must include data cells.");

        var writes = ConsolidationLabelPlanBuilder.Build(
            ctx,
            _sourceRanges,
            _destination,
            _function,
            _useTopRowLabels,
            _useLeftColumnLabels,
            _createLinksToSourceData);

        return WriteCells(ctx, writes);
    }

    private CommandOutcome WriteCells(ICommandContext ctx, IReadOnlyList<(CellAddress Address, ScalarValue Value, string? FormulaText)> writes)
    {
        if (writes.Any(write => !WorksheetBounds.IsValidAddress(write.Address)))
            return new CommandOutcome(false, DestinationBoundsMessage);

        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        if (destinationSheet.IsProtected)
        {
            foreach (var (address, _, _) in writes)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, destinationSheet, address))
                    return CommandGuards.RejectSheetProtected();
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>();
        foreach (var (address, value, formulaText) in writes)
        {
            _snapshot.Add((address, destinationSheet.GetCell(address)?.Clone(), destinationSheet.GetStyleOnly(address.Row, address.Col)));
            var newCell = Cell.FromValue(value);
            if (!string.IsNullOrWhiteSpace(formulaText))
                newCell.FormulaText = formulaText;
            if (destinationSheet.GetCell(address) is { } oldCell)
                newCell.StyleId = oldCell.StyleId;
            destinationSheet.SetCell(address, newCell);
            affected.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        foreach (var (address, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                destinationSheet.ClearCell(address);
                if (oldStyleOnly.HasValue)
                    destinationSheet.SetStyleOnly(address.Row, address.Col, oldStyleOnly.Value);
                else
                    destinationSheet.ClearStyleOnly(address.Row, address.Col);
            }
            else
            {
                destinationSheet.SetCell(address, oldCell.Clone());
            }
        }
    }
}
