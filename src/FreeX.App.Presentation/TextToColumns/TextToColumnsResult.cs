namespace FreeX.App.Presentation.TextToColumns;

/// <summary>
/// One split source row: the original cell text and the field strings it produced. Fields are the raw
/// slices the splitter emitted (untrimmed, unconverted); a host applies trimming and data-format
/// conversion in its own apply step.
/// </summary>
public sealed record TextToColumnsRow(string Source, IReadOnlyList<string> Fields)
{
    /// <summary>The number of fields this row produced.</summary>
    public int FieldCount => Fields.Count;
}

/// <summary>
/// The result of splitting a source column: every row's fields, the detected output column count, and
/// the per-column format hints carried through from the options.
/// </summary>
public sealed record TextToColumnsResult(
    IReadOnlyList<TextToColumnsRow> Rows,
    int ColumnCount,
    IReadOnlyList<TextToColumnsColumnFormat> ColumnFormats)
{
    /// <summary>An empty result with no rows and no columns.</summary>
    public static TextToColumnsResult Empty { get; } = new([], 0, []);

    /// <summary>True when there are no rows to split.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>
    /// The data-format hint for the given output column index, defaulting to
    /// <see cref="TextToColumnsColumnFormat.General"/> when no explicit hint was supplied.
    /// </summary>
    public TextToColumnsColumnFormat FormatFor(int columnIndex) =>
        columnIndex >= 0 && columnIndex < ColumnFormats.Count
            ? ColumnFormats[columnIndex]
            : TextToColumnsColumnFormat.General;
}

/// <summary>
/// A bounded projection of a result for a wizard preview: the column count and the leading sample rows.
/// </summary>
public sealed record TextToColumnsPreview(
    int ColumnCount,
    IReadOnlyList<TextToColumnsRow> SampleRows,
    IReadOnlyList<TextToColumnsColumnFormat> ColumnFormats);
