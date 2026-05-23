using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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

public sealed class ConsolidateCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<GridRange> _sourceRanges;
    private readonly CellAddress _destination;
    private readonly ConsolidateFunction _function;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Consolidate";

    public ConsolidateCommand(
        IReadOnlyList<GridRange> sourceRanges,
        CellAddress destination,
        ConsolidateFunction function = ConsolidateFunction.Sum)
    {
        _sourceRanges = sourceRanges;
        _destination = destination;
        _function = function;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRanges.Count == 0)
            return new CommandOutcome(false, "At least one source range is required.");

        var rowCount = _sourceRanges[0].RowCount;
        var colCount = _sourceRanges[0].ColCount;
        if (_sourceRanges.Any(r => r.RowCount != rowCount || r.ColCount != colCount))
            return new CommandOutcome(false, "Consolidate source ranges must be the same size.");

        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        if (destinationSheet.IsProtected)
        {
            for (uint rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                for (uint colOffset = 0; colOffset < colCount; colOffset++)
                {
                    var address = new CellAddress(_destination.Sheet, _destination.Row + rowOffset, _destination.Col + colOffset);
                    if (!CommandGuards.CanEditCell(ctx.Workbook, destinationSheet, address))
                        return new CommandOutcome(false, "The sheet is protected.");
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
                foreach (var range in _sourceRanges)
                {
                    var sourceSheet = ctx.GetSheet(range.Start.Sheet);
                    var value = sourceSheet.GetValue(range.Start.Row + rowOffset, range.Start.Col + colOffset);
                    if (value is not BlankValue)
                        nonEmptyCount++;
                    if (value is NumberValue number)
                        values.Add(number.Value);
                }

                var destinationAddress = new CellAddress(
                    _destination.Sheet,
                    _destination.Row + rowOffset,
                    _destination.Col + colOffset);
                _snapshot.Add((destinationAddress, destinationSheet.GetCell(destinationAddress)?.Clone()));

                var newCell = Cell.FromValue(new NumberValue(Aggregate(values, nonEmptyCount, _function)));
                if (destinationSheet.GetCell(destinationAddress) is { } oldCell)
                    newCell.StyleId = oldCell.StyleId;
                destinationSheet.SetCell(destinationAddress, newCell);
                affected.Add(destinationAddress);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    private static double Aggregate(IReadOnlyList<double> values, int nonEmptyCount, ConsolidateFunction function) =>
        function switch
        {
            ConsolidateFunction.Count => nonEmptyCount,
            ConsolidateFunction.Average => values.Count == 0 ? 0 : values.Average(),
            ConsolidateFunction.Max => values.Count == 0 ? 0 : values.Max(),
            ConsolidateFunction.Min => values.Count == 0 ? 0 : values.Min(),
            ConsolidateFunction.Product => values.Count == 0 ? 0 : values.Aggregate(1.0, (product, value) => product * value),
            ConsolidateFunction.CountNumbers => values.Count,
            ConsolidateFunction.StdDev => StandardDeviation(values, sample: true),
            ConsolidateFunction.StdDevp => StandardDeviation(values, sample: false),
            ConsolidateFunction.Var => Variance(values, sample: true),
            ConsolidateFunction.Varp => Variance(values, sample: false),
            _ => values.Sum()
        };

    private static double StandardDeviation(IReadOnlyList<double> values, bool sample) =>
        Math.Sqrt(Variance(values, sample));

    private static double Variance(IReadOnlyList<double> values, bool sample)
    {
        var denominator = sample ? values.Count - 1 : values.Count;
        if (denominator <= 0)
            return 0;

        var average = values.Average();
        return values.Sum(value => Math.Pow(value - average, 2)) / denominator;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        foreach (var (address, oldCell) in _snapshot)
        {
            if (oldCell is null)
                destinationSheet.ClearCell(address);
            else
                destinationSheet.SetCell(address, oldCell.Clone());
        }
    }
}
