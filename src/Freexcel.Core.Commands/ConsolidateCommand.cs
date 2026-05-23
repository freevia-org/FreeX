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
    private readonly bool _useTopRowLabels;
    private readonly bool _useLeftColumnLabels;
    private readonly bool _createLinksToSourceData;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Consolidate";

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

        var rowCount = _sourceRanges[0].RowCount;
        var colCount = _sourceRanges[0].ColCount;
        if (_sourceRanges.Any(r => r.RowCount != rowCount || r.ColCount != colCount))
            return new CommandOutcome(false, "Consolidate source ranges must be the same size.");

        if (_useTopRowLabels || _useLeftColumnLabels)
            return ApplyByLabels(ctx, rowCount, colCount);

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
                _snapshot.Add((destinationAddress, destinationSheet.GetCell(destinationAddress)?.Clone()));

                var newCell = Cell.FromValue(new NumberValue(Aggregate(values, nonEmptyCount, _function)));
                if (_createLinksToSourceData)
                    newCell.FormulaText = CreateSourceLinkFormula(ctx.Workbook, sourceAddresses, _destination.Sheet, _function);
                if (destinationSheet.GetCell(destinationAddress) is { } oldCell)
                    newCell.StyleId = oldCell.StyleId;
                destinationSheet.SetCell(destinationAddress, newCell);
                affected.Add(destinationAddress);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    private CommandOutcome ApplyByLabels(ICommandContext ctx, uint rowCount, uint colCount)
    {
        var bodyStartRow = _useTopRowLabels ? 1u : 0u;
        var bodyStartCol = _useLeftColumnLabels ? 1u : 0u;
        if (bodyStartRow >= rowCount || bodyStartCol >= colCount)
            return new CommandOutcome(false, "Consolidate source ranges must include data cells.");

        var rows = new List<string>();
        var cols = new List<string>();
        var buckets = new Dictionary<(string Row, string Col), (List<double> Values, int NonEmptyCount, List<CellAddress> SourceAddresses)>();

        foreach (var range in _sourceRanges)
        {
            var sourceSheet = ctx.GetSheet(range.Start.Sheet);
            for (uint rowOffset = bodyStartRow; rowOffset < rowCount; rowOffset++)
            {
                var rowLabel = _useLeftColumnLabels
                    ? LabelText(sourceSheet.GetValue(range.Start.Row + rowOffset, range.Start.Col))
                    : RowPositionLabel(rowOffset - bodyStartRow);
                AddUnique(rows, rowLabel);

                for (uint colOffset = bodyStartCol; colOffset < colCount; colOffset++)
                {
                    var colLabel = _useTopRowLabels
                        ? LabelText(sourceSheet.GetValue(range.Start.Row, range.Start.Col + colOffset))
                        : ColumnPositionLabel(colOffset - bodyStartCol);
                    AddUnique(cols, colLabel);

                    var key = (rowLabel, colLabel);
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = ([], 0, []);
                        buckets[key] = bucket;
                    }

                    var sourceAddress = new CellAddress(range.Start.Sheet, range.Start.Row + rowOffset, range.Start.Col + colOffset);
                    var value = sourceSheet.GetValue(sourceAddress.Row, sourceAddress.Col);
                    if (value is not BlankValue)
                        bucket.NonEmptyCount++;
                    if (value is NumberValue number)
                        bucket.Values.Add(number.Value);
                    bucket.SourceAddresses.Add(sourceAddress);
                    buckets[key] = bucket;
                }
            }
        }

        var writes = new List<(CellAddress Address, ScalarValue Value, string? FormulaText)>();
        var rowLabelColumnOffset = _useLeftColumnLabels ? 1u : 0u;
        var columnLabelRowOffset = _useTopRowLabels ? 1u : 0u;
        if (_useTopRowLabels && _useLeftColumnLabels)
            writes.Add((_destination, BlankValue.Instance, null));

        if (_useTopRowLabels)
        {
            for (var index = 0; index < cols.Count; index++)
                writes.Add((new CellAddress(_destination.Sheet, _destination.Row, _destination.Col + rowLabelColumnOffset + (uint)index), new TextValue(cols[index]), null));
        }

        if (_useLeftColumnLabels)
        {
            for (var index = 0; index < rows.Count; index++)
                writes.Add((new CellAddress(_destination.Sheet, _destination.Row + columnLabelRowOffset + (uint)index, _destination.Col), new TextValue(rows[index]), null));
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var colIndex = 0; colIndex < cols.Count; colIndex++)
            {
                var bucket = buckets.TryGetValue((rows[rowIndex], cols[colIndex]), out var found)
                    ? found
                    : ([], 0, []);
                writes.Add((
                    new CellAddress(
                        _destination.Sheet,
                        _destination.Row + columnLabelRowOffset + (uint)rowIndex,
                        _destination.Col + rowLabelColumnOffset + (uint)colIndex),
                    new NumberValue(Aggregate(bucket.Values, bucket.NonEmptyCount, _function)),
                    _createLinksToSourceData
                        ? CreateSourceLinkFormula(ctx.Workbook, bucket.SourceAddresses, _destination.Sheet, _function)
                        : null));
            }
        }

        return WriteCells(ctx, writes);
    }

    private CommandOutcome WriteCells(ICommandContext ctx, IReadOnlyList<(CellAddress Address, ScalarValue Value, string? FormulaText)> writes)
    {
        var destinationSheet = ctx.GetSheet(_destination.Sheet);
        if (destinationSheet.IsProtected)
        {
            foreach (var (address, _, _) in writes)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, destinationSheet, address))
                    return new CommandOutcome(false, "The sheet is protected.");
            }
        }

        _snapshot = [];
        var affected = new List<CellAddress>();
        foreach (var (address, value, formulaText) in writes)
        {
            _snapshot.Add((address, destinationSheet.GetCell(address)?.Clone()));
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

    private static void AddUnique(List<string> labels, string label)
    {
        if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            labels.Add(label);
    }

    private static string RowPositionLabel(uint offset) => $"Row {offset + 1}";

    private static string ColumnPositionLabel(uint offset) => $"Column {offset + 1}";

    private static string LabelText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value.Trim(),
            NumberValue number => number.Value.ToString("G15", System.Globalization.CultureInfo.CurrentCulture),
            DateTimeValue date => date.Value.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            ErrorValue error => error.Code,
            _ => ""
        };

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

    private static string CreateSourceLinkFormula(
        Workbook workbook,
        IReadOnlyList<CellAddress> sourceAddresses,
        SheetId destinationSheetId,
        ConsolidateFunction function)
    {
        var functionName = function switch
        {
            ConsolidateFunction.Count => "COUNTA",
            ConsolidateFunction.CountNumbers => "COUNT",
            ConsolidateFunction.StdDev => "STDEV",
            ConsolidateFunction.StdDevp => "STDEVP",
            ConsolidateFunction.Var => "VAR",
            ConsolidateFunction.Varp => "VARP",
            _ => function.ToString().ToUpperInvariant()
        };

        var arguments = sourceAddresses
            .Select(address => FormatFormulaReference(workbook, address, destinationSheetId));
        return $"{functionName}({string.Join(",", arguments)})";
    }

    private static string FormatFormulaReference(Workbook workbook, CellAddress address, SheetId destinationSheetId)
    {
        var reference = CellAddress.NumberToColumnName(address.Col) + address.Row;
        if (address.Sheet == destinationSheetId)
            return reference;

        var sheetName = workbook.GetSheet(address.Sheet)?.Name ?? "Sheet";
        return $"{QuoteSheetName(sheetName)}!{reference}";
    }

    private static string QuoteSheetName(string sheetName)
    {
        var escaped = sheetName.Replace("'", "''", StringComparison.Ordinal);
        return sheetName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_')
            ? $"'{escaped}'"
            : escaped;
    }

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
