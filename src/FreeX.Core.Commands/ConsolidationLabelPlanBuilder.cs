using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class ConsolidationLabelPlanBuilder
{
    public static IReadOnlyList<(CellAddress Address, ScalarValue Value, string? FormulaText)> Build(
        ICommandContext ctx,
        IReadOnlyList<GridRange> sourceRanges,
        CellAddress destination,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData,
        uint rowCount,
        uint colCount)
    {
        var bodyStartRow = useTopRowLabels ? 1u : 0u;
        var bodyStartCol = useLeftColumnLabels ? 1u : 0u;
        var rows = new List<string>();
        var cols = new List<string>();
        var buckets = new Dictionary<(string Row, string Col), ConsolidationBucket>();

        foreach (var range in sourceRanges)
            CollectRange(ctx, range, rows, cols, buckets, useTopRowLabels, useLeftColumnLabels, bodyStartRow, bodyStartCol, rowCount, colCount);

        return BuildWrites(
            ctx.Workbook,
            destination,
            rows,
            cols,
            buckets,
            function,
            useTopRowLabels,
            useLeftColumnLabels,
            createLinksToSourceData);
    }

    private static void CollectRange(
        ICommandContext ctx,
        GridRange range,
        List<string> rows,
        List<string> cols,
        Dictionary<(string Row, string Col), ConsolidationBucket> buckets,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        uint bodyStartRow,
        uint bodyStartCol,
        uint rowCount,
        uint colCount)
    {
        var sourceSheet = ctx.GetSheet(range.Start.Sheet);
        for (uint rowOffset = bodyStartRow; rowOffset < rowCount; rowOffset++)
        {
            var rowLabel = useLeftColumnLabels
                ? ConsolidationRules.LabelText(sourceSheet.GetValue(range.Start.Row + rowOffset, range.Start.Col))
                : ConsolidationRules.RowPositionLabel(rowOffset - bodyStartRow);
            ConsolidationRules.AddUnique(rows, rowLabel);

            for (uint colOffset = bodyStartCol; colOffset < colCount; colOffset++)
            {
                var colLabel = useTopRowLabels
                    ? ConsolidationRules.LabelText(sourceSheet.GetValue(range.Start.Row, range.Start.Col + colOffset))
                    : ConsolidationRules.ColumnPositionLabel(colOffset - bodyStartCol);
                ConsolidationRules.AddUnique(cols, colLabel);
                AddCellToBucket(ctx, range, buckets, rowLabel, colLabel, rowOffset, colOffset);
            }
        }
    }

    private static void AddCellToBucket(
        ICommandContext ctx,
        GridRange range,
        Dictionary<(string Row, string Col), ConsolidationBucket> buckets,
        string rowLabel,
        string colLabel,
        uint rowOffset,
        uint colOffset)
    {
        var key = (rowLabel, colLabel);
        if (!buckets.TryGetValue(key, out var bucket))
        {
            bucket = new ConsolidationBucket();
            buckets[key] = bucket;
        }

        var sourceSheet = ctx.GetSheet(range.Start.Sheet);
        var sourceAddress = new CellAddress(range.Start.Sheet, range.Start.Row + rowOffset, range.Start.Col + colOffset);
        var value = sourceSheet.GetValue(sourceAddress.Row, sourceAddress.Col);
        if (value is not BlankValue)
            bucket.NonEmptyCount++;
        if (value is NumberValue number)
            bucket.Values.Add(number.Value);
        bucket.SourceAddresses.Add(sourceAddress);
    }

    private static IReadOnlyList<(CellAddress Address, ScalarValue Value, string? FormulaText)> BuildWrites(
        Workbook workbook,
        CellAddress destination,
        IReadOnlyList<string> rows,
        IReadOnlyList<string> cols,
        IReadOnlyDictionary<(string Row, string Col), ConsolidationBucket> buckets,
        ConsolidateFunction function,
        bool useTopRowLabels,
        bool useLeftColumnLabels,
        bool createLinksToSourceData)
    {
        var writes = new List<(CellAddress Address, ScalarValue Value, string? FormulaText)>();
        var rowLabelColumnOffset = useLeftColumnLabels ? 1u : 0u;
        var columnLabelRowOffset = useTopRowLabels ? 1u : 0u;
        if (useTopRowLabels && useLeftColumnLabels)
            writes.Add((destination, BlankValue.Instance, null));

        if (useTopRowLabels)
        {
            for (var index = 0; index < cols.Count; index++)
                writes.Add((new CellAddress(destination.Sheet, destination.Row, destination.Col + rowLabelColumnOffset + (uint)index), new TextValue(cols[index]), null));
        }

        if (useLeftColumnLabels)
        {
            for (var index = 0; index < rows.Count; index++)
                writes.Add((new CellAddress(destination.Sheet, destination.Row + columnLabelRowOffset + (uint)index, destination.Col), new TextValue(rows[index]), null));
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var colIndex = 0; colIndex < cols.Count; colIndex++)
            {
                var bucket = buckets.TryGetValue((rows[rowIndex], cols[colIndex]), out var found)
                    ? found
                    : ConsolidationBucket.Empty;
                writes.Add((
                    new CellAddress(
                        destination.Sheet,
                        destination.Row + columnLabelRowOffset + (uint)rowIndex,
                        destination.Col + rowLabelColumnOffset + (uint)colIndex),
                    new NumberValue(ConsolidationRules.Aggregate(bucket.Values, bucket.NonEmptyCount, function)),
                    createLinksToSourceData
                        ? ConsolidationRules.CreateSourceLinkFormula(workbook, bucket.SourceAddresses, destination.Sheet, function)
                        : null));
            }
        }

        return writes;
    }

    private sealed class ConsolidationBucket
    {
        public static ConsolidationBucket Empty { get; } = new();

        public List<double> Values { get; } = [];

        public int NonEmptyCount { get; set; }

        public List<CellAddress> SourceAddresses { get; } = [];
    }
}
