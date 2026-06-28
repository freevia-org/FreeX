using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public static class MailMergeRecipientFilterSortPlanner
{
    public const int MaxPreviewColumns = 8;

    public static IReadOnlyList<string> GetPreviewColumns(IReadOnlyList<string> header, int maxColumns = MaxPreviewColumns)
    {
        ArgumentNullException.ThrowIfNull(header);

        return header.Take(Math.Max(0, maxColumns)).ToList();
    }

    public static string FormatPreviewHeader(IReadOnlyList<string> previewColumns)
    {
        ArgumentNullException.ThrowIfNull(previewColumns);

        return "  " + string.Join("  |  ", previewColumns);
    }

    public static string FormatPreviewRow(
        int rowIndex,
        IReadOnlyDictionary<string, string> row,
        IReadOnlyList<string> previewColumns)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(previewColumns);

        var preview = string.Join("  |  ", previewColumns.Select(column =>
            row.TryGetValue(column, out var value) ? value : string.Empty));
        return $"{rowIndex + 1}. {preview}";
    }

    public static MergeData Apply(
        MergeData data,
        IEnumerable<int> includedRowIndexes,
        string? sortColumn,
        bool ascending)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(includedRowIndexes);

        var selectedRows = includedRowIndexes
            .Where(index => index >= 0 && index < data.Rows.Count)
            .Distinct()
            .Select(index => data.Rows[index]);

        var column = sortColumn ?? string.Empty;
        var orderedRows = (ascending
                ? selectedRows.OrderBy(row => SortValue(row, column), StringComparer.OrdinalIgnoreCase)
                : selectedRows.OrderByDescending(row => SortValue(row, column), StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new MergeData(
            data.Header,
            orderedRows.Select(row =>
                (IReadOnlyList<string>)data.Header
                    .Select(header => row.TryGetValue(header, out var value) ? value : string.Empty)
                    .ToList())
                .ToList());
    }

    private static string SortValue(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : string.Empty;
}
