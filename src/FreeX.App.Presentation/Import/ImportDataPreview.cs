namespace FreeX.App.Presentation.Import;

/// <summary>
/// A bounded projection of how a Get Data import would split the source text: the widest field count
/// across the sampled rows, the leading sample rows (each already split into its fields), the resolved
/// delimiter character the split used, and the encoding web-name the bytes were decoded with. The dialog
/// renders this in a grid so the user can confirm the delimiter/encoding before applying.
/// </summary>
public sealed record ImportDataPreview(
    int ColumnCount,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
    char Delimiter,
    string EncodingName,
    int TotalRowCount)
{
    /// <summary>An empty preview (no rows, no columns).</summary>
    public static ImportDataPreview Empty { get; } =
        new(0, [], ',', "utf-8", 0);

    /// <summary>True when the sample produced no rows.</summary>
    public bool IsEmpty => SampleRows.Count == 0;
}
