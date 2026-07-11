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
        bool createLinksToSourceData)
    {
        var bodyStartRow = useTopRowLabels ? 1u : 0u;
        var bodyStartCol = useLeftColumnLabels ? 1u : 0u;
        var rows = new List<string>();
        var cols = new List<string>();
        // Row/column labels are deduped case-insensitively (ConsolidationRules.AddUnique), matching
        // Excel's category-label merge. The bucket dictionary must key on the same case-insensitive
        // equality, otherwise a source label that differs only in case (e.g. "Apples" vs "apples")
        // lands in its own orphan bucket that BuildWrites never looks up, silently dropping its values.
        var buckets = new Dictionary<(string Row, string Col), ConsolidationBucket>(LabelKeyComparer);

        // Each range contributes its own RowCount/ColCount (rather than a size shared across all
        // ranges), so real Excel's "consolidate by category" scenario -- differently-shaped/sized
        // source ranges matched by label text -- reads every range over its own full extent instead
        // of being clipped to (or overrunning) another range's size.
        foreach (var range in sourceRanges)
            CollectRange(ctx, range, rows, cols, buckets, useTopRowLabels, useLeftColumnLabels, bodyStartRow, bodyStartCol);

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
        uint bodyStartCol)
    {
        var sourceSheet = ctx.GetSheet(range.Start.Sheet);
        for (uint rowOffset = bodyStartRow; rowOffset < range.RowCount; rowOffset++)
        {
            var rowLabel = useLeftColumnLabels
                ? ConsolidationRules.LabelText(sourceSheet.GetValue(range.Start.Row + rowOffset, range.Start.Col))
                : ConsolidationRules.RowPositionLabel(rowOffset - bodyStartRow);
            ConsolidationRules.AddUnique(rows, rowLabel);

            for (uint colOffset = bodyStartCol; colOffset < range.ColCount; colOffset++)
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

    private static readonly IEqualityComparer<(string Row, string Col)> LabelKeyComparer = new CaseInsensitiveLabelKeyComparer();

    private sealed class CaseInsensitiveLabelKeyComparer : IEqualityComparer<(string Row, string Col)>
    {
        public bool Equals((string Row, string Col) x, (string Row, string Col) y) =>
            string.Equals(x.Row, y.Row, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Col, y.Col, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Row, string Col) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Row),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Col));
    }
}
