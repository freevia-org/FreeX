using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeW.Core.IO.RecipientSources;

/// <summary>
/// Reads the cell text of an Excel workbook (.xlsx / .xlsm) for use as a mail-merge recipient list.
/// It is a deliberately small SpreadsheetML reader built on the shared OPC helpers rather than a
/// reference to FreeX's workbook stack: FreeW only needs displayed values from one rectangular range,
/// and this keeps FreeW's dependency closure unchanged. It understands worksheets, defined names and
/// Excel tables as selectable sources, shared and inline strings, booleans, cached formula results, and
/// numeric cells (shown as dates when their number format is a date format). Formulas are never
/// evaluated: the value Excel last saved is used, exactly as Word's own mail merge does.
/// </summary>
internal static class XlsxRecipientReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace StrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";
    private static readonly XNamespace OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace StrictOfficeRel = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Excel's own grid bounds; a reference outside them is not a usable range.</summary>
    private const int MaxRow = 1_048_576;
    private const int MaxColumn = 16_384;

    /// <summary>The largest Excel serial date (9999-12-31).</summary>
    private const double MaxSerialDate = 2_958_465.99999999;

    /// <summary>Upper bound on cells materialized for one recipient list.</summary>
    internal const int MaxCells = 2_000_000;

    public static IReadOnlyList<RecipientSourceTable> ListTables(Stream stream)
    {
        using var archive = Open(stream);
        var workbook = LoadWorkbook(archive);
        var tables = new List<RecipientSourceTable>();
        foreach (var sheet in workbook.Sheets)
        {
            tables.Add(new RecipientSourceTable(
                SheetKey(sheet.Name),
                sheet.Name + "$",
                RecipientSourceTableKind.Worksheet));
        }

        foreach (var name in workbook.DefinedNames)
        {
            tables.Add(new RecipientSourceTable(
                NameKey(name.Name),
                name.Name,
                RecipientSourceTableKind.NamedRange));
        }

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in LoadSheetTables(archive, sheet))
            {
                tables.Add(new RecipientSourceTable(
                    TableKey(table.Name),
                    table.Name,
                    RecipientSourceTableKind.Table));
            }
        }

        return tables;
    }

    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(Stream stream, string? tableKey)
    {
        using var archive = Open(stream);
        var workbook = LoadWorkbook(archive);
        if (workbook.Sheets.Count == 0)
            throw new RecipientSourceException(RecipientSourceError.NoTable);

        SheetInfo sheet;
        CellRange? range = null;
        if (string.IsNullOrEmpty(tableKey))
        {
            sheet = workbook.Sheets[0];
        }
        else if (tableKey.StartsWith(SheetPrefix, StringComparison.Ordinal))
        {
            sheet = workbook.Sheets.FirstOrDefault(candidate =>
                    string.Equals(SheetKey(candidate.Name), tableKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new RecipientSourceException(RecipientSourceError.NoTable);
        }
        else if (tableKey.StartsWith(NamePrefix, StringComparison.Ordinal))
        {
            var name = workbook.DefinedNames.FirstOrDefault(candidate =>
                    string.Equals(NameKey(candidate.Name), tableKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new RecipientSourceException(RecipientSourceError.NoTable);
            sheet = workbook.Sheets.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name.SheetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new RecipientSourceException(RecipientSourceError.NoTable);
            range = name.Range;
        }
        else
        {
            SheetInfo? owner = null;
            foreach (var candidate in workbook.Sheets)
            {
                var table = LoadSheetTables(archive, candidate).FirstOrDefault(item =>
                    string.Equals(TableKey(item.Name), tableKey, StringComparison.OrdinalIgnoreCase));
                if (table is null)
                    continue;
                owner = candidate;
                range = table.Range;
                break;
            }

            sheet = owner ?? throw new RecipientSourceException(RecipientSourceError.NoTable);
        }

        return ReadSheet(archive, sheet, range, workbook);
    }

    private const string SheetPrefix = "sheet:";
    private const string NamePrefix = "name:";
    private const string TablePrefix = "table:";

    private static string SheetKey(string name) => SheetPrefix + name;

    private static string NameKey(string name) => NamePrefix + name;

    private static string TableKey(string name) => TablePrefix + name;

    private static ZipArchive Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            try
            {
                // Declared-size and duplicate-entry checks only; nothing is decompressed here.
                WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(archive);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
            return archive;
        }
        catch (InvalidDataException exception)
        {
            throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
        }
        catch (WorkbookTooLargeException exception)
        {
            throw new RecipientSourceException(RecipientSourceError.TooLarge, exception);
        }
        catch (WorkbookInvalidException exception)
        {
            throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
        }
    }

    private sealed record SheetInfo(string Name, string PartPath);

    private sealed record DefinedNameInfo(string Name, string SheetName, CellRange Range);

    private sealed record TableInfo(string Name, CellRange Range);

    private readonly record struct CellRange(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
    {
        public bool Contains(int row, int column) =>
            row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;
    }

    private sealed class WorkbookInfo
    {
        public List<SheetInfo> Sheets { get; } = [];

        public List<DefinedNameInfo> DefinedNames { get; } = [];

        public IReadOnlyList<string> SharedStrings { get; set; } = [];

        public IReadOnlyList<bool> DateStyles { get; set; } = [];

        public bool Date1904 { get; set; }
    }

    private static WorkbookInfo LoadWorkbook(ZipArchive archive)
    {
        var workbookPath = FindWorkbookPath(archive)
            ?? throw new RecipientSourceException(RecipientSourceError.Unreadable);
        var workbookXml = LoadXml(archive, workbookPath)
            ?? throw new RecipientSourceException(RecipientSourceError.Unreadable);
        var directory = OpcPathHelper.GetDirectoryName(workbookPath);
        var relationships = LoadRelationships(archive, workbookPath);
        var info = new WorkbookInfo
        {
            Date1904 = IsOn(Child(workbookXml.Root, "workbookPr")?.Attribute("date1904")?.Value),
        };

        foreach (var sheet in Children(Child(workbookXml.Root, "sheets"), "sheet"))
        {
            var name = sheet.Attribute("name")?.Value;
            var relationshipId = sheet.Attribute(OfficeRel + "id")?.Value
                ?? sheet.Attribute(StrictOfficeRel + "id")?.Value;
            // Chart sheets and macro sheets are not data; only worksheet targets are listed.
            if (string.IsNullOrEmpty(name)
                || relationshipId is null
                || !relationships.TryGetValue(relationshipId, out var relationship)
                || !relationship.Type.EndsWith("/worksheet", StringComparison.Ordinal))
            {
                continue;
            }

            info.Sheets.Add(new SheetInfo(
                name,
                OpcPathHelper.ResolveRelationshipTargetZipPath(archive, directory, relationship.Target)));
        }

        foreach (var definedName in Children(Child(workbookXml.Root, "definedNames"), "definedName"))
        {
            var name = definedName.Attribute("name")?.Value;
            // Excel's own bookkeeping names (print areas, filter ranges) are not recipient lists.
            if (string.IsNullOrEmpty(name)
                || name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase)
                || IsOn(definedName.Attribute("hidden")?.Value)
                || !TryParseSheetRange(definedName.Value, out var sheetName, out var range)
                || !info.Sheets.Any(sheet => string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            info.DefinedNames.Add(new DefinedNameInfo(name, sheetName, range));
        }

        var sharedStrings = relationships.Values.FirstOrDefault(relationship =>
            relationship.Type.EndsWith("/sharedStrings", StringComparison.Ordinal));
        if (sharedStrings is not null)
        {
            info.SharedStrings = LoadSharedStrings(
                archive,
                OpcPathHelper.ResolveRelationshipTargetZipPath(archive, directory, sharedStrings.Target));
        }

        var styles = relationships.Values.FirstOrDefault(relationship =>
            relationship.Type.EndsWith("/styles", StringComparison.Ordinal));
        if (styles is not null)
        {
            info.DateStyles = LoadDateStyles(
                archive,
                OpcPathHelper.ResolveRelationshipTargetZipPath(archive, directory, styles.Target));
        }

        return info;
    }

    private static string? FindWorkbookPath(ZipArchive archive)
    {
        var rootRelationships = LoadXml(archive, "_rels/.rels");
        var target = rootRelationships?.Root?.Elements(PackageRel + "Relationship")
            .FirstOrDefault(relationship =>
                (relationship.Attribute("Type")?.Value ?? string.Empty)
                .EndsWith("/officeDocument", StringComparison.Ordinal))
            ?.Attribute("Target")?.Value;
        if (!string.IsNullOrEmpty(target))
            return OpcPathHelper.ResolveRelationshipTargetZipPath(archive, string.Empty, target);
        return OpcPathHelper.FindEntry(archive, "xl/workbook.xml")?.FullName;
    }

    private sealed record Relationship(string Type, string Target, bool External);

    private static Dictionary<string, Relationship> LoadRelationships(ZipArchive archive, string partPath)
    {
        var result = new Dictionary<string, Relationship>(StringComparer.Ordinal);
        var document = LoadXml(archive, OpcPathHelper.GetRelationshipPartPath(partPath));
        if (document?.Root is null)
            return result;

        foreach (var relationship in document.Root.Elements(PackageRel + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
                continue;
            var external = string.Equals(
                relationship.Attribute("TargetMode")?.Value,
                "External",
                StringComparison.OrdinalIgnoreCase);
            // An external target is never followed: a recipient list comes from this package only.
            if (external)
                continue;
            result[id] = new Relationship(relationship.Attribute("Type")?.Value ?? string.Empty, target, external);
        }

        return result;
    }

    private static IReadOnlyList<TableInfo> LoadSheetTables(ZipArchive archive, SheetInfo sheet)
    {
        var tables = new List<TableInfo>();
        var directory = OpcPathHelper.GetDirectoryName(sheet.PartPath);
        foreach (var relationship in LoadRelationships(archive, sheet.PartPath).Values)
        {
            if (!relationship.Type.EndsWith("/table", StringComparison.Ordinal))
                continue;
            var tableXml = LoadXml(
                archive,
                OpcPathHelper.ResolveRelationshipTargetZipPath(archive, directory, relationship.Target));
            var root = tableXml?.Root;
            var name = root?.Attribute("displayName")?.Value ?? root?.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name) || !TryParseRange(root?.Attribute("ref")?.Value, out var range))
                continue;
            tables.Add(new TableInfo(name, range));
        }
        return tables;
    }

    private static IReadOnlyList<string> LoadSharedStrings(ZipArchive archive, string path)
    {
        var entry = OpcPathHelper.FindEntry(archive, path);
        if (entry is null)
            return [];

        var strings = new List<string>();
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si")
                    continue;
                using var item = reader.ReadSubtree();
                strings.Add(ReadStringItem(XElement.Load(item)));
            }
        }
        catch (XmlException exception)
        {
            throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
        }
        return strings;
    }

    // A string item is either one <t> or rich-text runs (<r><t/></r>…); phonetic runs (<rPh>) are
    // reading aids, not cell text.
    private static string ReadStringItem(XElement item) =>
        string.Concat(item.Elements()
            .Where(child => child.Name.LocalName is "t" or "r")
            .SelectMany(child => child.Name.LocalName == "t"
                ? [child]
                : child.Elements().Where(run => run.Name.LocalName == "t"))
            .Select(text => text.Value));

    private static IReadOnlyList<bool> LoadDateStyles(ZipArchive archive, string path)
    {
        var root = LoadXml(archive, path)?.Root;
        if (root is null)
            return [];

        var customFormats = Children(Child(root, "numFmts"), "numFmt")
            .Select(format => (
                Id: int.TryParse(format.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1,
                Code: format.Attribute("formatCode")?.Value ?? string.Empty))
            .Where(format => format.Id >= 0)
            .GroupBy(format => format.Id)
            .ToDictionary(group => group.Key, group => group.Last().Code);

        return Children(Child(root, "cellXfs"), "xf")
            .Select(xf =>
            {
                if (!int.TryParse(
                        xf.Attribute("numFmtId")?.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var formatId))
                {
                    return false;
                }
                return customFormats.TryGetValue(formatId, out var code)
                    ? IsDateFormatCode(code)
                    : IsBuiltInDateFormat(formatId);
            })
            .ToArray();
    }

    // Excel's built-in date/time number formats (ECMA-376 Part 1, 18.8.30).
    private static bool IsBuiltInDateFormat(int formatId) =>
        formatId is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58;

    // A custom format is a date format when, outside quoted literals and [bracketed] sections, it uses
    // a date/time placeholder.
    internal static bool IsDateFormatCode(string code)
    {
        var inQuotes = false;
        var inBrackets = false;
        for (var index = 0; index < code.Length; index++)
        {
            var c = code[index];
            if (c == '"')
                inQuotes = !inQuotes;
            else if (inQuotes)
                continue;
            else if (c == '\\')
                index++;
            else if (c == '[')
                inBrackets = true;
            else if (c == ']')
                inBrackets = false;
            else if (!inBrackets && char.ToLowerInvariant(c) is 'y' or 'm' or 'd' or 'h' or 's')
                return true;
        }
        return false;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadSheet(
        ZipArchive archive,
        SheetInfo sheet,
        CellRange? range,
        WorkbookInfo workbook)
    {
        var entry = OpcPathHelper.FindEntry(archive, sheet.PartPath)
            ?? throw new RecipientSourceException(RecipientSourceError.Unreadable);

        // Sparse rows keyed by 1-based row number, each a map of 1-based column number to text.
        var rows = new SortedDictionary<int, SortedDictionary<int, string>>();
        var cells = 0;
        using (var stream = entry.Open())
        using (var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create(long.MaxValue)))
        {
            try
            {
                var nextRow = 1;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
                        continue;

                    var rowNumber = ParseIndex(reader.GetAttribute("r"), MaxRow) ?? nextRow;
                    nextRow = rowNumber + 1;
                    using var rowReader = reader.ReadSubtree();
                    var rowElement = XElement.Load(rowReader);
                    var nextColumn = 1;
                    foreach (var cell in rowElement.Elements().Where(child => child.Name.LocalName == "c"))
                    {
                        var column = TryParseCellReference(cell.Attribute("r")?.Value, out _, out var parsedColumn)
                            ? parsedColumn
                            : nextColumn;
                        nextColumn = column + 1;
                        if (range is { } bounds && !bounds.Contains(rowNumber, column))
                            continue;

                        var text = CellText(cell, workbook);
                        if (text.Length == 0)
                            continue;

                        if (++cells > MaxCells)
                            throw new RecipientSourceException(RecipientSourceError.TooLarge);
                        if (!rows.TryGetValue(rowNumber, out var rowCells))
                            rows[rowNumber] = rowCells = [];
                        rowCells[column] = text;
                    }
                }
            }
            catch (XmlException exception)
            {
                throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
            }
        }

        if (rows.Count == 0)
            return [];

        // Without an explicit range the list is the sheet's used range: from its first used column to
        // its last, so a list that does not start in column A still lines up with its header.
        var firstColumn = range?.FirstColumn ?? rows.Values.Min(row => row.Keys.First());
        var lastColumn = rows.Values.Max(row => row.Keys.Last());
        var width = lastColumn - firstColumn + 1;
        var result = new List<IReadOnlyList<string>>(rows.Count);
        foreach (var row in rows.Values)
        {
            var values = new string[width];
            Array.Fill(values, string.Empty);
            foreach (var (column, text) in row)
                values[column - firstColumn] = text;
            result.Add(values);
        }
        return result;
    }

    private static string CellText(XElement cell, WorkbookInfo workbook)
    {
        var type = cell.Attribute("t")?.Value;
        var value = cell.Elements().FirstOrDefault(child => child.Name.LocalName == "v")?.Value;
        switch (type)
        {
            case "s":
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                    && index >= 0
                    && index < workbook.SharedStrings.Count
                    ? workbook.SharedStrings[index]
                    : string.Empty;

            case "inlineStr":
                var inline = cell.Elements().FirstOrDefault(child => child.Name.LocalName == "is");
                return inline is null ? string.Empty : ReadStringItem(inline);

            case "b":
                return value == "1" ? "TRUE" : value == "0" ? "FALSE" : value ?? string.Empty;

            case "str":
            case "e":
                return value ?? string.Empty;

            case "d":
                return DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var isoDate)
                    ? FormatDate(isoDate)
                    : value ?? string.Empty;
        }

        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return value;

        var styleIndex = int.TryParse(
            cell.Attribute("s")?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedStyle)
            ? parsedStyle
            : -1;
        var isDate = styleIndex >= 0 && styleIndex < workbook.DateStyles.Count && workbook.DateStyles[styleIndex];
        if (isDate && TryFromSerialDate(number, workbook.Date1904, out var date))
            return FormatDate(date);

        // An accept range, not a two-sided reject: NaN and the infinities fail it and keep their raw text.
        if (!(number >= -1e308 && number <= 1e308))
            return value;
        // 15 significant digits is what Excel itself displays; it hides binary noise such as 0.1+0.2.
        return number.ToString("G15", CultureInfo.CurrentCulture);
    }

    internal static bool TryFromSerialDate(double serial, bool date1904, out DateTime date)
    {
        date = default;
        // An accept range: NaN fails both comparisons, so it is refused along with out-of-range serials.
        if (!(serial >= 0 && serial <= MaxSerialDate))
            return false;

        if (date1904)
        {
            date = new DateTime(1904, 1, 1).AddDays(serial);
            return true;
        }

        // The 1900 system counts the non-existent 1900-02-29 as day 60, so days before it sit one later.
        var whole = Math.Floor(serial);
        var adjusted = whole < 60 ? serial + 1 : serial;
        date = new DateTime(1899, 12, 30).AddDays(adjusted);
        return true;
    }

    private static string FormatDate(DateTime date) =>
        date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : date.Date == new DateTime(1899, 12, 30) || date.Date == new DateTime(1899, 12, 31)
                ? date.ToString("t", CultureInfo.CurrentCulture)
                : date.ToString("g", CultureInfo.CurrentCulture);

    // "Sheet1!$A$1:$C$10", "'My Sheet'!A1:B2", or a single cell "Sheet1!$B$2".
    private static bool TryParseSheetRange(string? reference, out string sheetName, out CellRange range)
    {
        sheetName = string.Empty;
        range = default;
        if (string.IsNullOrWhiteSpace(reference) || reference.Contains(','))
            return false;

        var separator = reference.LastIndexOf('!');
        if (separator <= 0)
            return false;

        sheetName = reference[..separator].Trim();
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'')
            sheetName = sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);
        return sheetName.Length > 0 && TryParseRange(reference[(separator + 1)..], out range);
    }

    private static bool TryParseRange(string? reference, out CellRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var parts = reference.Replace("$", string.Empty, StringComparison.Ordinal).Split(':');
        if (parts.Length is < 1 or > 2
            || !TryParseCellReference(parts[0], out var firstRow, out var firstColumn)
            || !TryParseCellReference(parts[^1], out var lastRow, out var lastColumn))
        {
            return false;
        }

        range = new CellRange(
            Math.Min(firstRow, lastRow),
            Math.Min(firstColumn, lastColumn),
            Math.Max(firstRow, lastRow),
            Math.Max(firstColumn, lastColumn));
        return true;
    }

    private static bool TryParseCellReference(string? reference, out int row, out int column)
    {
        row = 0;
        column = 0;
        if (string.IsNullOrEmpty(reference))
            return false;

        var index = 0;
        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            if (column > MaxColumn)
                return false;
            index++;
        }
        if (index == 0 || index == reference.Length)
            return false;

        var parsedRow = ParseIndex(reference[index..], MaxRow);
        if (parsedRow is null)
            return false;
        row = parsedRow.Value;
        return true;
    }

    private static int? ParseIndex(string? text, int max) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value >= 1
        && value <= max
            ? value
            : null;

    private static XDocument? LoadXml(ZipArchive archive, string path)
    {
        var entry = OpcPathHelper.FindEntry(archive, path);
        if (entry is null)
            return null;

        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            return XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new RecipientSourceException(RecipientSourceError.Unreadable, exception);
        }
    }

    // Transitional and Strict workbooks differ only in namespace, so parts are matched by local name.
    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element =>
            element.Name.LocalName == localName
            && (element.Name.Namespace == Main || element.Name.Namespace == StrictMain));

    private static IEnumerable<XElement> Children(XElement? parent, string localName) =>
        parent?.Elements().Where(element =>
            element.Name.LocalName == localName
            && (element.Name.Namespace == Main || element.Name.Namespace == StrictMain))
        ?? [];

    private static bool IsOn(string? value) => value is "1" or "true" or "on";
}
