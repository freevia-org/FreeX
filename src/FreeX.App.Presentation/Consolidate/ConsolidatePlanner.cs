using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Consolidate;

/// <summary>
/// Portable planner for the Data &gt; Consolidate feature. Given several source ranges' cell grids and a set
/// of <see cref="ConsolidateOptions"/>, it aggregates the sources into a single output grid — pure data in,
/// pure data out. It references no desktop-host or renderer types; a host maps its ranges into
/// <see cref="ConsolidateSource"/> grids, runs the planner, and writes the planned cells back into the
/// destination. The aggregation faithfully mirrors the desktop hosts' Core consolidation: by-position
/// matches cells by row/column offset across equally shaped sources, by-labels takes the union of row and
/// column labels (in first-appearance order, matched case-insensitively) and aggregates the cells that
/// share each label pair.
/// </summary>
public static class ConsolidatePlanner
{
    /// <summary>
    /// Plans the consolidation of <paramref name="sources"/> under <paramref name="options"/>. Returns
    /// <see cref="ConsolidateResult.Empty"/> when there are no sources or nothing to aggregate.
    /// </summary>
    public static ConsolidateResult Plan(IReadOnlyList<ConsolidateSource> sources, ConsolidateOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);

        if (sources.Count == 0)
            return ConsolidateResult.Empty;

        return options.ConsolidateByLabels
            ? PlanByLabels(sources, options)
            : PlanByPosition(sources, options);
    }

    private static ConsolidateResult PlanByPosition(IReadOnlyList<ConsolidateSource> sources, ConsolidateOptions options)
    {
        var rowCount = sources.Max(source => source.RowCount);
        var colCount = sources.Max(source => source.ColumnCount);
        if (rowCount == 0 || colCount == 0)
            return ConsolidateResult.Empty;

        var cells = new List<ConsolidateOutputCell>(rowCount * colCount);
        for (var row = 0; row < rowCount; row++)
        {
            for (var col = 0; col < colCount; col++)
            {
                var values = new List<double>();
                var nonEmptyCount = 0;
                foreach (var source in sources)
                {
                    var cell = source.CellAt(row, col);
                    if (cell.IsNonEmpty)
                        nonEmptyCount++;
                    if (cell.IsNumber)
                        values.Add(cell.Number);
                }

                cells.Add(ConsolidateOutputCell.ForNumber(
                    row,
                    col,
                    ConsolidateAggregation.Aggregate(values, nonEmptyCount, options.Function)));
            }
        }

        return new ConsolidateResult(rowCount, colCount, cells);
    }

    private static ConsolidateResult PlanByLabels(IReadOnlyList<ConsolidateSource> sources, ConsolidateOptions options)
    {
        var bodyStartRow = options.UseTopRowLabels ? 1 : 0;
        var bodyStartCol = options.UseLeftColumnLabels ? 1 : 0;

        var rowLabels = new List<string>();
        var colLabels = new List<string>();
        var buckets = new Dictionary<(string Row, string Col), Bucket>();

        foreach (var source in sources)
            CollectSource(source, options, bodyStartRow, bodyStartCol, rowLabels, colLabels, buckets);

        if (rowLabels.Count == 0 || colLabels.Count == 0)
            return ConsolidateResult.Empty;

        return BuildResult(options, rowLabels, colLabels, buckets);
    }

    private static void CollectSource(
        ConsolidateSource source,
        ConsolidateOptions options,
        int bodyStartRow,
        int bodyStartCol,
        List<string> rowLabels,
        List<string> colLabels,
        Dictionary<(string Row, string Col), Bucket> buckets)
    {
        var rowCount = source.RowCount;
        var colCount = source.ColumnCount;

        for (var row = bodyStartRow; row < rowCount; row++)
        {
            var rowLabel = options.UseLeftColumnLabels
                ? source.CellAt(row, 0).LabelText()
                : RowPositionLabel(row - bodyStartRow);
            AddUnique(rowLabels, rowLabel);

            for (var col = bodyStartCol; col < colCount; col++)
            {
                var colLabel = options.UseTopRowLabels
                    ? source.CellAt(0, col).LabelText()
                    : ColumnPositionLabel(col - bodyStartCol);
                AddUnique(colLabels, colLabel);

                var key = (rowLabel, colLabel);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket();
                    buckets[key] = bucket;
                }

                var cell = source.CellAt(row, col);
                if (cell.IsNonEmpty)
                    bucket.NonEmptyCount++;
                if (cell.IsNumber)
                    bucket.Values.Add(cell.Number);
            }
        }
    }

    private static ConsolidateResult BuildResult(
        ConsolidateOptions options,
        IReadOnlyList<string> rowLabels,
        IReadOnlyList<string> colLabels,
        IReadOnlyDictionary<(string Row, string Col), Bucket> buckets)
    {
        var rowHeaderOffset = options.UseTopRowLabels ? 1 : 0;
        var colHeaderOffset = options.UseLeftColumnLabels ? 1 : 0;
        var rowCount = rowLabels.Count + rowHeaderOffset;
        var colCount = colLabels.Count + colHeaderOffset;

        var cells = new List<ConsolidateOutputCell>(rowCount * colCount);

        if (options.UseTopRowLabels && options.UseLeftColumnLabels)
            cells.Add(ConsolidateOutputCell.ForBlank(0, 0));

        if (options.UseTopRowLabels)
        {
            for (var index = 0; index < colLabels.Count; index++)
                cells.Add(ConsolidateOutputCell.ForLabel(0, colHeaderOffset + index, colLabels[index]));
        }

        if (options.UseLeftColumnLabels)
        {
            for (var index = 0; index < rowLabels.Count; index++)
                cells.Add(ConsolidateOutputCell.ForLabel(rowHeaderOffset + index, 0, rowLabels[index]));
        }

        for (var rowIndex = 0; rowIndex < rowLabels.Count; rowIndex++)
        {
            for (var colIndex = 0; colIndex < colLabels.Count; colIndex++)
            {
                var bucket = buckets.TryGetValue((rowLabels[rowIndex], colLabels[colIndex]), out var found)
                    ? found
                    : Bucket.Empty;
                cells.Add(ConsolidateOutputCell.ForNumber(
                    rowHeaderOffset + rowIndex,
                    colHeaderOffset + colIndex,
                    ConsolidateAggregation.Aggregate(bucket.Values, bucket.NonEmptyCount, options.Function)));
            }
        }

        return new ConsolidateResult(rowCount, colCount, cells);
    }

    private static void AddUnique(List<string> labels, string label)
    {
        if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            labels.Add(label);
    }

    private static string RowPositionLabel(int offset) => $"Row {offset + 1}";

    private static string ColumnPositionLabel(int offset) => $"Column {offset + 1}";

    private sealed class Bucket
    {
        public static Bucket Empty { get; } = new();

        public List<double> Values { get; } = [];

        public int NonEmptyCount { get; set; }
    }
}
