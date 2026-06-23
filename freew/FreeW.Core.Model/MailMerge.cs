using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// The semantic roles Word maps recipient-list columns to when composing an Address Block or Greeting
/// Line. Each role represents a distinct piece of contact information; the <see cref="FieldMapping"/>
/// records which data-source column name is bound to each role.
/// </summary>
public enum FieldRole
{
    Title,
    FirstName,
    MiddleName,
    LastName,
    Suffix,
    Company,
    Address1,
    Address2,
    City,
    State,
    PostalCode,
    Country
}

/// <summary>
/// Maps each <see cref="FieldRole"/> to a column name in the active data source. A null value means the
/// role is unmapped (the field is omitted from the composed block). Instances are mutable so the Match
/// Fields dialog can update individual bindings without creating a new object.
/// </summary>
public sealed class FieldMapping
{
    private readonly Dictionary<FieldRole, string?> _map = new();

    /// <summary>Get or set the column name bound to <paramref name="role"/> (null = unmapped).</summary>
    public string? this[FieldRole role]
    {
        get => _map.TryGetValue(role, out var v) ? v : null;
        set => _map[role] = value;
    }

    /// <summary>All roles explicitly stored in this mapping (mapped or null).</summary>
    public IEnumerable<FieldRole> MappedRoles => _map.Keys;
}

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

/// <summary>Backed FreeW subset of Word's Start Mail Merge document types.</summary>
public enum MailMergeOutputMode
{
    /// <summary>Start each merged record on a new page, matching Word's letter-style merge output.</summary>
    Letters,

    /// <summary>Append records continuously, matching Word's directory/catalog-style merge output.</summary>
    Directory
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
    /// The placeholder text (without guillemets) for the «Next Record» special field. During
    /// <see cref="SubstituteSpecial"/> this causes the record index to advance by one so a single
    /// template can emit multiple records (used in directory / label layouts).
    /// </summary>
    public const string NextRecordField = "Next Record";

    /// <summary>
    /// The placeholder text (without guillemets) for the «Merge Record #» special field. During
    /// <see cref="SubstituteSpecial"/> this is replaced by the 1-based record index.
    /// </summary>
    public const string MergeRecordNumberField = "Merge Record #";

    // ── Canonical synonyms for each role used by AutoMatchFields (case-insensitive) ────────────────
    private static readonly Dictionary<FieldRole, string[]> RoleSynonyms = new()
    {
        [FieldRole.Title]      = ["title", "salutation", "honorific"],
        [FieldRole.FirstName]  = ["firstname", "first name", "first", "givenname", "given name"],
        [FieldRole.MiddleName] = ["middlename", "middle name", "middle", "middleinitial", "middle initial"],
        [FieldRole.LastName]   = ["lastname", "last name", "last", "surname", "familyname", "family name"],
        [FieldRole.Suffix]     = ["suffix"],
        [FieldRole.Company]    = ["company", "organization", "organisation", "companyname", "company name", "org"],
        [FieldRole.Address1]   = ["address1", "address 1", "address", "street", "streetaddress", "street address", "addr1"],
        [FieldRole.Address2]   = ["address2", "address 2", "addr2"],
        [FieldRole.City]       = ["city", "town", "locality"],
        [FieldRole.State]      = ["state", "province", "region"],
        [FieldRole.PostalCode] = ["postalcode", "postal code", "zip", "zipcode", "zip code", "postcode", "post code"],
        [FieldRole.Country]    = ["country", "countryorregion", "country or region", "nation"],
    };

    /// <summary>
    /// Auto-match a list of column headers to <see cref="FieldRole"/>s using case-insensitive
    /// heuristics (synonym matching). Each role is bound to the first header that matches any of its
    /// known synonyms; unmatched roles are left null. The returned mapping seeds the Match Fields dialog.
    /// </summary>
    public static FieldMapping AutoMatchFields(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var mapping = new FieldMapping();
        // Build a lookup from normalized header → original header name.
        var normalized = header
            .Select(h => (Normalized: Normalize(h), Original: h))
            .ToList();

        foreach (var (role, synonyms) in RoleSynonyms)
        {
            foreach (var (norm, orig) in normalized)
            {
                if (Array.Exists(synonyms, s => s.Equals(norm, StringComparison.OrdinalIgnoreCase)))
                {
                    mapping[role] = orig;
                    break;
                }
            }
        }

        return mapping;

        static string Normalize(string s) => s.Trim().Replace("_", " ");
    }

    /// <summary>
    /// Compose a formatted postal address block from <paramref name="row"/> using the role bindings in
    /// <paramref name="mapping"/>. The format follows Word's default address-block layout:
    /// <code>
    ///   [Title] FirstName [MiddleName] LastName [Suffix]
    ///   [Company]
    ///   Address1
    ///   [Address2]
    ///   City, State PostalCode
    ///   [Country]
    /// </code>
    /// Lines that contain only unmapped/empty values are omitted. Returns an empty string when no
    /// address information is available. Pure and deterministic.
    /// </summary>
    public static string ComposeAddressBlock(IReadOnlyDictionary<string, string> row, FieldMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);

        string Get(FieldRole role) => mapping[role] is { } col ? Lookup(row, col) : string.Empty;

        // Name line: Title FirstName MiddleName LastName Suffix
        var nameParts = new List<string>();
        var title  = Get(FieldRole.Title);
        var first  = Get(FieldRole.FirstName);
        var middle = Get(FieldRole.MiddleName);
        var last   = Get(FieldRole.LastName);
        var suffix = Get(FieldRole.Suffix);
        if (title.Length  > 0) nameParts.Add(title);
        if (first.Length  > 0) nameParts.Add(first);
        if (middle.Length > 0) nameParts.Add(middle);
        if (last.Length   > 0) nameParts.Add(last);
        if (suffix.Length > 0) nameParts.Add(suffix);

        var company   = Get(FieldRole.Company);
        var address1  = Get(FieldRole.Address1);
        var address2  = Get(FieldRole.Address2);
        var city      = Get(FieldRole.City);
        var state     = Get(FieldRole.State);
        var postal    = Get(FieldRole.PostalCode);
        var country   = Get(FieldRole.Country);

        // City, State PostalCode line — only include non-empty parts.
        var cityStateParts = new List<string>();
        var cityState = city.Length > 0 && state.Length > 0 ? $"{city}, {state}"
                      : city.Length  > 0 ? city
                      : state.Length > 0 ? state
                      : string.Empty;
        if (cityState.Length > 0) cityStateParts.Add(cityState);
        if (postal.Length    > 0) cityStateParts.Add(postal);
        var cityStateLine = string.Join(" ", cityStateParts);

        var lines = new List<string>();
        if (nameParts.Count > 0)  lines.Add(string.Join(" ", nameParts));
        if (company.Length  > 0)  lines.Add(company);
        if (address1.Length > 0)  lines.Add(address1);
        if (address2.Length > 0)  lines.Add(address2);
        if (cityStateLine.Length > 0) lines.Add(cityStateLine);
        if (country.Length  > 0)  lines.Add(country);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Compose a greeting line from <paramref name="row"/> using the role bindings in
    /// <paramref name="mapping"/>. <paramref name="greetingFormat"/> is the prefix text that precedes
    /// the recipient name (e.g. <c>"Dear"</c>); the composed greeting is:
    /// <c>{greetingFormat} {Title} {LastName},</c>
    /// falling back to <c>Dear Sir or Madam,</c> when no name fields are bound/populated.
    /// Pure and deterministic.
    /// </summary>
    public static string ComposeGreetingLine(
        IReadOnlyDictionary<string, string> row,
        FieldMapping mapping,
        string greetingFormat = "Dear")
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(greetingFormat);

        string Get(FieldRole role) => mapping[role] is { } col ? Lookup(row, col) : string.Empty;

        var title = Get(FieldRole.Title);
        var first = Get(FieldRole.FirstName);
        var last  = Get(FieldRole.LastName);

        // Build the name portion: prefer "Title LastName", fall back to "FirstName LastName",
        // then just the non-empty name part, then the generic fallback.
        string namePart;
        if (title.Length > 0 && last.Length > 0)
            namePart = $"{title} {last}";
        else if (first.Length > 0 && last.Length > 0)
            namePart = $"{first} {last}";
        else if (last.Length > 0)
            namePart = last;
        else if (first.Length > 0)
            namePart = first;
        else
            namePart = string.Empty;

        var prefix = greetingFormat.TrimEnd();
        return namePart.Length > 0 ? $"{prefix} {namePart}," : $"{prefix} Sir or Madam,";
    }

    /// <summary>
    /// Replace every <c>«Field»</c> placeholder in <paramref name="text"/> with the matching value from
    /// <paramref name="row"/>, and also resolve the special placeholders <c>«Merge Record #»</c> (the
    /// 1-based <paramref name="recordIndex"/>) and <c>«Next Record»</c> (sets
    /// <paramref name="advanceRecord"/> to true so the caller can move to the next row). A standard
    /// merge-field lookup occurs for all other names.
    /// </summary>
    public static string SubstituteSpecial(
        string text,
        IReadOnlyDictionary<string, string> row,
        int recordIndex,
        out bool advanceRecord)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(row);

        advanceRecord = false;
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
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                var name = text.Substring(i + 1, close - i - 1).Trim();
                if (name.Equals(NextRecordField, StringComparison.OrdinalIgnoreCase))
                {
                    advanceRecord = true;
                    // «Next Record» produces no visible output — it is a control directive only.
                }
                else if (name.Equals(MergeRecordNumberField, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(recordIndex);
                }
                else if (name.Equals("AddressBlock", StringComparison.OrdinalIgnoreCase))
                {
                    // «AddressBlock» without a mapping is a plain substitution from a named field if present,
                    // otherwise empty. Full resolution (via FieldMapping) is done by the caller before
                    // SubstituteSpecial; if it reaches here the field just resolves via the row dictionary.
                    sb.Append(Lookup(row, name));
                }
                else if (name.Equals("GreetingLine", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(Lookup(row, name));
                }
                else
                {
                    sb.Append(Lookup(row, name));
                }

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

    /// <summary>
    /// Combine already-merged records into a single document using the selected output mode. Letters force
    /// a page break before each record after the first; Directory appends records continuously. The merged
    /// record documents are consumed into the returned document.
    /// </summary>
    public static TextDocument CombineMergedRecords(IReadOnlyList<TextDocument> records, MailMergeOutputMode mode)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            return TextDocument.CreateEmpty();

        var first = records[0];
        for (var d = 1; d < records.Count; d++)
        {
            var blocks = records[d].Blocks;
            if (mode == MailMergeOutputMode.Letters)
                ForcePageBreakBeforeRecord(first, blocks);

            foreach (var block in blocks)
                first.Blocks.Add(block);
        }

        return first;
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

    private static void ForcePageBreakBeforeRecord(TextDocument first, IList<Block> blocks)
    {
        // Word's Letters output starts each record on a new page. If the record starts with a paragraph,
        // keep the content and put the break on that paragraph; otherwise insert a dedicated break block.
        if (blocks.Count > 0 && blocks[0] is Paragraph lead)
        {
            lead.Formatting = lead.Formatting with { PageBreakBefore = true };
        }
        else
        {
            first.Blocks.Add(DocumentOps.CreatePageBreak());
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
            ComplexField = source.ComplexField,
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
