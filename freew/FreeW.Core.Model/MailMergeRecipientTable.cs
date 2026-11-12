using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// Pure tabular helpers behind mail-merge recipient lists: turning a rectangular block of cell text
/// (a worksheet range, a delimited text file, a document table, or the rows typed into a new list) into
/// a <see cref="MergeData"/>, and back. File access lives in FreeW.Core.IO; nothing here touches the disk.
/// </summary>
public static class MergeRecipientTable
{
    /// <summary>The delimiters a recipient text file may use, in detection-preference order.</summary>
    public static IReadOnlyList<char> SupportedDelimiters { get; } = [',', '\t', ';', '|'];

    /// <summary>
    /// Build a recipient list from rows of cell text. With <paramref name="firstRowIsHeader"/> the first
    /// row names the fields (blank names become <c>F1</c>, <c>F2</c>, … by position, and a name that
    /// repeats one before it — ignoring case — gets a numeric suffix so no column silently overwrites
    /// another); without it every row is a record and all fields are named by position, which is what
    /// Word does for a headerless source. Fully blank rows are dropped and short rows are padded.
    /// </summary>
    public static MergeData FromTable(IReadOnlyList<IReadOnlyList<string>> rows, bool firstRowIsHeader)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var records = rows
            .Where(row => row is not null && row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
        if (records.Count == 0)
            return new MergeData([], []);

        var width = records.Max(row => row.Count);
        var header = BuildHeader(firstRowIsHeader ? records[0] : [], width);
        var body = records.Skip(firstRowIsHeader ? 1 : 0)
            .Select(row => (IReadOnlyList<string>)Enumerable.Range(0, width)
                .Select(index => index < row.Count ? row[index] ?? string.Empty : string.Empty)
                .ToArray());
        return new MergeData(header, body);
    }

    /// <summary>The values of record <paramref name="rowIndex"/> in header order.</summary>
    public static IReadOnlyList<string> RowValues(MergeData data, int rowIndex)
    {
        ArgumentNullException.ThrowIfNull(data);
        var row = data.Rows[rowIndex];
        return data.Header
            .Select(name => row.TryGetValue(name, out var value) ? value : string.Empty)
            .ToArray();
    }

    /// <summary>
    /// A recipient list holding the records at <paramref name="rowIndexes"/> of <paramref name="data"/>,
    /// in that order. Out-of-range and repeated indexes are ignored.
    /// </summary>
    public static MergeData SelectRows(MergeData data, IEnumerable<int> rowIndexes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rowIndexes);
        var seen = new HashSet<int>();
        return new MergeData(
            data.Header,
            rowIndexes
                .Where(index => index >= 0 && index < data.Count && seen.Add(index))
                .Select(index => RowValues(data, index))
                .ToList());
    }

    /// <summary>
    /// Split delimited text into rows of fields. Fields may be wrapped in double quotes to carry the
    /// delimiter, line breaks, or a doubled quote (<c>""</c>); CRLF, LF and CR all end a record. Rows are
    /// returned as written (blank lines included as single-empty-field rows are skipped).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseDelimited(string text, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = new List<IReadOnlyList<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRecord()
        {
            var blankLine = record.Count == 0 && field.Length == 0 && !fieldWasQuoted;
            EndField();
            if (!blankLine)
                records.Add(record);
            record = [];
        }

        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = false;
                }
                continue;
            }

            if (c == '"' && field.Length == 0 && !fieldWasQuoted)
            {
                inQuotes = true;
                fieldWasQuoted = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                EndRecord();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fieldWasQuoted || record.Count > 0)
            EndRecord();
        return records;
    }

    /// <summary>
    /// Pick the delimiter a text recipient list most plausibly uses: the candidate that splits the
    /// leading lines into the same number (more than one) of fields most consistently, preferring more
    /// fields on a tie. Delimiters inside quoted fields do not count. Returns <paramref name="fallback"/>
    /// when no candidate splits the text at all (a single-column list).
    /// </summary>
    public static char DetectDelimiter(string text, char fallback = ',')
    {
        ArgumentNullException.ThrowIfNull(text);

        const int sampleRows = 20;
        var best = fallback;
        var bestScore = 0;
        foreach (var candidate in SupportedDelimiters)
        {
            var rows = ParseDelimited(SampleText(text, sampleRows * 4), candidate).Take(sampleRows).ToList();
            if (rows.Count == 0)
                continue;

            var width = rows[0].Count;
            if (width < 2)
                continue;

            // Consistency first (how many sampled rows agree with the first row's width), then width.
            var agreeing = rows.Count(row => row.Count == width);
            var score = agreeing * 1000 + Math.Min(width, 999);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Serialize a recipient list as delimited text (header line first), quoting any field that holds
    /// the delimiter, a quote, a line break, or leading/trailing space so it reads back unchanged.
    /// </summary>
    public static string WriteDelimited(MergeData data, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(data);

        var builder = new StringBuilder();
        AppendLine(builder, data.Header, delimiter);
        for (var index = 0; index < data.Count; index++)
            AppendLine(builder, RowValues(data, index), delimiter);
        return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, IReadOnlyList<string> fields, char delimiter)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
                builder.Append(delimiter);
            builder.Append(Quote(fields[index] ?? string.Empty, delimiter));
        }
        builder.Append("\r\n");
    }

    private static string Quote(string field, char delimiter)
    {
        var needsQuotes = field.Length == 0
            ? false
            : field.Contains(delimiter)
                || field.Contains('"')
                || field.Contains('\r')
                || field.Contains('\n')
                || char.IsWhiteSpace(field[0])
                || char.IsWhiteSpace(field[^1]);
        return needsQuotes ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;
    }

    private static string SampleText(string text, int maxLines)
    {
        var lines = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' && ++lines >= maxLines)
                return text[..index];
        }
        return text;
    }

    private static IReadOnlyList<string> BuildHeader(IReadOnlyList<string> names, int width)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var header = new List<string>(width);
        for (var index = 0; index < width; index++)
        {
            var name = index < names.Count ? (names[index] ?? string.Empty).Trim() : string.Empty;
            if (name.Length == 0)
                name = "F" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

            var unique = name;
            for (var suffix = 2; !used.Add(unique); suffix++)
                unique = name + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            header.Add(unique);
        }
        return header;
    }
}
