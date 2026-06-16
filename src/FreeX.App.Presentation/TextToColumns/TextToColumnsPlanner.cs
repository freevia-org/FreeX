namespace FreeX.App.Presentation.TextToColumns;

/// <summary>
/// Portable planner for the Text-to-Columns feature. Given the source column's cell texts and a set of
/// options, it produces the split rows (each input mapped to its field strings), the detected output
/// column count, and the per-column format hints — pure data in, pure data out. It performs no value
/// conversion and references no desktop-host or renderer types; a host applies trimming, the
/// <see cref="TextToColumnsColumnFormat.Skip"/> exclusion, and data-format conversion in its own step.
/// </summary>
public static class TextToColumnsPlanner
{
    /// <summary>
    /// Splits each source cell text according to <paramref name="options"/>. Null entries are treated as
    /// empty text. The result's column count is the widest row produced.
    /// </summary>
    public static TextToColumnsResult Plan(IEnumerable<string?> sources, TextToColumnsOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);

        var rows = new List<TextToColumnsRow>();
        var columnCount = 0;
        foreach (var source in sources)
        {
            var text = source ?? string.Empty;
            var fields = Split(text, options);
            if (fields.Length > columnCount)
                columnCount = fields.Length;

            rows.Add(new TextToColumnsRow(text, fields));
        }

        return new TextToColumnsResult(rows, columnCount, options.ColumnFormats);
    }

    /// <summary>
    /// Projects a bounded preview of the split, taking up to <paramref name="sampleRowLimit"/> leading
    /// rows. The column count reflects the full input, not just the sampled rows.
    /// </summary>
    public static TextToColumnsPreview Preview(
        IEnumerable<string?> sources,
        TextToColumnsOptions options,
        int sampleRowLimit = 10)
    {
        var result = Plan(sources, options);
        return Preview(result, sampleRowLimit);
    }

    /// <summary>Projects a bounded preview from an already-computed result.</summary>
    public static TextToColumnsPreview Preview(TextToColumnsResult result, int sampleRowLimit = 10)
    {
        ArgumentNullException.ThrowIfNull(result);

        var limit = Math.Max(0, sampleRowLimit);
        var sample = limit >= result.Rows.Count
            ? result.Rows
            : result.Rows.Take(limit).ToList();

        return new TextToColumnsPreview(result.ColumnCount, sample, result.ColumnFormats);
    }

    /// <summary>Splits a single line of text according to <paramref name="options"/>.</summary>
    public static string[] Split(string text, TextToColumnsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.SplitMode == TextToColumnsSplitMode.FixedWidth
            ? TextToColumnsSplitter.SplitFixedWidth(text, options.FixedWidthBreakPositions)
            : TextToColumnsSplitter.SplitDelimited(
                text,
                options.Delimiters,
                options.TextQualifier,
                options.TreatConsecutiveDelimitersAsOne);
    }
}
