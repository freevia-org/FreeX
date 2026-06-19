using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// A simple mail-merge data source: an ordered header of field names plus zero or more rows, each row
/// mapping a field name to its value for that record. Field names are matched case-insensitively (so a
/// template field «Name» binds to a "name" header), mirroring how Word treats merge-field names. The
/// store is pure model data with no docx part of its own; the merge engine (see <see cref="MailMerge"/>)
/// substitutes the values into ordinary text runs.
/// </summary>
public sealed class MergeData
{
    private readonly List<IReadOnlyDictionary<string, string>> _rows = [];

    /// <summary>
    /// Create a data source from a header (the field names, in order) and rows of values. Each row is a
    /// list of cell values positionally matched to the header; rows shorter than the header are padded
    /// with empty strings, and extra cells beyond the header are ignored. Header names are trimmed.
    /// </summary>
    public MergeData(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        Header = header.Select(h => (h ?? string.Empty).Trim()).ToList();
        foreach (var cells in rows)
        {
            ArgumentNullException.ThrowIfNull(cells);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Header.Count; i++)
                row[Header[i]] = i < cells.Count ? cells[i] ?? string.Empty : string.Empty;
            _rows.Add(row);
        }
    }

    /// <summary>The field names, in header order (trimmed).</summary>
    public IReadOnlyList<string> Header { get; }

    /// <summary>The records, each a case-insensitive map from field name to value.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows => _rows;

    /// <summary>The number of records (rows) in the data source.</summary>
    public int Count => _rows.Count;

    /// <summary>
    /// Parse a data source from CSV text. The first non-empty content forms the header line; each
    /// subsequent line is a record. Fields may be quoted with double quotes to embed commas, newlines,
    /// or doubled quotes (<c>""</c> → a literal <c>"</c>), following the usual CSV conventions. Both
    /// CRLF and LF line endings are accepted. An empty/blank input yields an empty data source (no
    /// header, no rows).
    /// </summary>
    public static MergeData FromCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var records = ParseCsv(csv);
        if (records.Count == 0)
            return new MergeData([], []);

        var header = records[0];
        var rows = records.Skip(1).ToList();
        return new MergeData(header, rows);
    }

    // Tokenise CSV into a list of records (each a list of field strings), honouring double-quoted fields
    // (with embedded commas/newlines and "" escapes). Fully blank lines outside quotes are skipped.
    private static List<List<string>> ParseCsv(string csv)
    {
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;
        var sawAny = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            // Skip records that are entirely empty (a single blank field) — typically a trailing newline.
            if (record.Count == 1 && record[0].Length == 0)
            {
                record = [];
                return;
            }
            records.Add(record);
            record = [];
        }

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    sawAny = true;
                    break;
                case ',':
                    sawAny = true;
                    EndField();
                    break;
                case '\r':
                    // Swallow CR; the following LF (if any) ends the record.
                    if (i + 1 < csv.Length && csv[i + 1] == '\n')
                        i++;
                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    sawAny = true;
                    field.Append(c);
                    break;
            }
        }

        // Flush the final record if the input did not end with a newline.
        if (field.Length > 0 || record.Count > 0 || (sawAny && records.Count == 0))
            EndRecord();

        return records;
    }
}

/// <summary>
/// Pure, deterministic mail-merge helpers over the FreeW document model. A merge field is the literal
/// text <c>«FieldName»</c> — the field name wrapped in guillemets (U+00AB «, U+00BB ») — carried inside
/// ordinary run text, so it round-trips through docx as plain text with no special part. The engine
/// discovers field names, substitutes a record's values into the field placeholders, and produces one
/// merged document per record.
/// <para>
/// Missing-field policy: when a placeholder names a field that the data row does not contain, the
/// placeholder is replaced with an <b>empty string</b> (the field is dropped, matching Word's behaviour
/// for an empty merge value). A field whose row value is the empty string is likewise substituted to
/// empty. The placeholder delimiters themselves are always removed for any well-formed «Field».
/// </para>
/// </summary>
public static class MailMerge
{
    /// <summary>The opening merge-field delimiter (left guillemet, U+00AB).</summary>
    public const char FieldOpen = '«';

    /// <summary>The closing merge-field delimiter (right guillemet, U+00BB).</summary>
    public const char FieldClose = '»';

    /// <summary>
    /// The distinct merge-field names appearing in <paramref name="text"/>, in first-appearance order.
    /// A field is <c>«Name»</c>; the returned names are trimmed and de-duplicated case-insensitively
    /// (the first spelling wins). Empty placeholders (<c>«»</c> or whitespace-only) are ignored.
    /// </summary>
    public static IReadOnlyList<string> FieldNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in EnumerateFields(text))
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0)
                continue;
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    /// <summary>
    /// The distinct merge-field names appearing anywhere in the document body (paragraph runs and table
    /// cell paragraphs), in first-appearance order, de-duplicated case-insensitively. Header/footer and
    /// footnote/comment text are not scanned — merge fields live in the body flow.
    /// </summary>
    public static IReadOnlyList<string> FieldNames(TextDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string text)
        {
            foreach (var name in EnumerateFields(text))
            {
                var trimmed = name.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }
        }

        foreach (var block in doc.Blocks)
            ScanBlock(block, Scan);

        return result;
    }

    /// <summary>
    /// Replace every <c>«Field»</c> placeholder in <paramref name="text"/> with the matching value from
    /// <paramref name="row"/> (looked up case-insensitively when the dictionary supports it; otherwise by
    /// exact key). A placeholder whose field is absent from the row is replaced with the empty string
    /// (see the missing-field policy on <see cref="MailMerge"/>). Literal text outside placeholders is
    /// left untouched, and an unterminated <c>«</c> (no closing <c>»</c>) is treated as literal text.
    /// </summary>
    public static string Substitute(string text, IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(row);

        if (text.IndexOf(FieldOpen) < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == FieldOpen)
            {
                var close = text.IndexOf(FieldClose, i + 1);
                if (close < 0)
                {
                    // No closing delimiter — emit the rest verbatim.
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                var name = text.Substring(i + 1, close - i - 1).Trim();
                sb.Append(Lookup(row, name));
                i = close + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Produce a new document that is <paramref name="template"/> with every run's <c>«Field»</c>
    /// placeholder substituted for <paramref name="row"/>'s values. The template is not mutated; the
    /// returned document is a deep copy of the body (paragraphs, runs and tables) sharing the same
    /// immutable formatting records, with styles, page settings, header/footer and properties carried
    /// over so the merged record looks like the template. Deterministic.
    /// </summary>
    public static TextDocument MergeRecord(TextDocument template, IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(row);

        var doc = new TextDocument
        {
            DefaultRun = template.DefaultRun,
            DefaultParagraph = template.DefaultParagraph
        };

        foreach (var (id, style) in template.Styles)
            doc.Styles[id] = style;

        CopyPageSettings(template.Page, doc.Page);
        doc.Header = CloneHeaderFooter(template.Header, row);
        doc.Footer = CloneHeaderFooter(template.Footer, row);

        foreach (var block in template.Blocks)
            doc.Blocks.Add(CloneBlock(block, row));

        return doc;
    }

    /// <summary>
    /// Produce one merged document per row in <paramref name="data"/>, in row order, each the result of
    /// <see cref="MergeRecord"/> against <paramref name="template"/>. Deterministic; an empty data source
    /// yields an empty list.
    /// </summary>
    public static IReadOnlyList<TextDocument> MergeAll(TextDocument template, MergeData data)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);

        var result = new List<TextDocument>(data.Count);
        foreach (var row in data.Rows)
            result.Add(MergeRecord(template, row));
        return result;
    }

    // Enumerate the raw (untrimmed) field-name spans found between matched «…» delimiters in order.
    private static IEnumerable<string> EnumerateFields(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf(FieldOpen, i);
            if (open < 0)
                yield break;
            var close = text.IndexOf(FieldClose, open + 1);
            if (close < 0)
                yield break;
            yield return text.Substring(open + 1, close - open - 1);
            i = close + 1;
        }
    }

    private static string Lookup(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;

    private static void ScanBlock(Block block, Action<string> scan)
    {
        switch (block)
        {
            case Paragraph p:
                foreach (var run in p.Runs)
                    scan(run.Text);
                break;
            case Table t:
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var p in cell.Paragraphs)
                            foreach (var run in p.Runs)
                                scan(run.Text);
                break;
        }
    }

    private static Block CloneBlock(Block block, IReadOnlyDictionary<string, string> row) => block switch
    {
        Paragraph p => CloneParagraph(p, row),
        Table t => CloneTable(t, row),
        _ => new Paragraph()
    };

    private static Paragraph CloneParagraph(Paragraph source, IReadOnlyDictionary<string, string> row)
    {
        var clone = new Paragraph
        {
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            BookmarkName = source.BookmarkName
        };
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRun(run, row));
        return clone;
    }

    private static Run CloneRun(Run source, IReadOnlyDictionary<string, string> row)
    {
        // Image/field/footnote/comment/control runs carry no merge text of their own; copy their text
        // through unchanged (Substitute is a no-op when there is no placeholder) while preserving marks.
        return new Run(Substitute(source.Text, row), source.Formatting)
        {
            Image = source.Image,
            HyperlinkUrl = source.HyperlinkUrl,
            HyperlinkAnchor = source.HyperlinkAnchor,
            HyperlinkTooltip = source.HyperlinkTooltip,
            FieldKind = source.FieldKind,
            FootnoteId = source.FootnoteId,
            EndnoteId = source.EndnoteId,
            CommentId = source.CommentId,
            IsCommentReference = source.IsCommentReference,
            Revision = source.Revision,
            Control = source.Control,
            Citation = source.Citation,
            CrossReference = source.CrossReference,
            RevisionAuthor = source.RevisionAuthor,
            RevisionDateXml = source.RevisionDateXml
        };
    }

    private static Table CloneTable(Table source, IReadOnlyDictionary<string, string> row)
    {
        var clone = new Table { Formatting = source.Formatting };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var sourceRow in source.Rows)
        {
            var newRow = new TableRow();
            foreach (var cell in sourceRow.Cells)
            {
                var newCell = new TableCell
                {
                    ShadingColorHex = cell.ShadingColorHex,
                    WidthPt = cell.WidthPt,
                    GridSpan = cell.GridSpan,
                    VerticalMerge = cell.VerticalMerge
                };
                foreach (var p in cell.Paragraphs)
                    newCell.Paragraphs.Add(CloneParagraph(p, row));
                newRow.Cells.Add(newCell);
            }
            clone.Rows.Add(newRow);
        }
        return clone;
    }

    private static HeaderFooter? CloneHeaderFooter(HeaderFooter? source, IReadOnlyDictionary<string, string> row)
    {
        if (source is null)
            return null;
        var clone = new HeaderFooter();
        foreach (var p in source.Paragraphs)
            clone.Paragraphs.Add(CloneParagraph(p, row));
        return clone;
    }

    private static void CopyPageSettings(PageSettings from, PageSettings to)
    {
        to.WidthPt = from.WidthPt;
        to.HeightPt = from.HeightPt;
        to.MarginLeftPt = from.MarginLeftPt;
        to.MarginRightPt = from.MarginRightPt;
        to.MarginTopPt = from.MarginTopPt;
        to.MarginBottomPt = from.MarginBottomPt;
        to.Landscape = from.Landscape;
        to.ColumnCount = from.ColumnCount;
        to.ColumnSpacingPt = from.ColumnSpacingPt;
        to.ColumnsLineBetween = from.ColumnsLineBetween;
        to.ColumnWidthsPt = from.ColumnWidthsPt is null ? null : new List<double>(from.ColumnWidthsPt);
        to.PageBorder = from.PageBorder;
        to.Watermark = from.Watermark;
        to.LineNumberMode = from.LineNumberMode;
        to.LineNumberCountBy = from.LineNumberCountBy;
        to.AutoHyphenation = from.AutoHyphenation;
        to.VerticalAlignment = from.VerticalAlignment;
        to.DifferentFirstPage = from.DifferentFirstPage;
    }
}
