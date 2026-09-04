using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class OdsFileAdapter
{
    private Workbook ReadWorkbook(XDocument contentDoc, XDocument? stylesDoc)
    {
        var workbook = new Workbook("OpenDocument Spreadsheet");

        var root = contentDoc.Root
            ?? throw new InvalidDataException("content.xml has no root element.");

        // Build the cell-style table from automatic-styles in content.xml (and styles.xml as a fallback).
        var styleTable = new OdsStyleTable();
        styleTable.Load(root.Element(OfficeNs + "automatic-styles"));
        if (stylesDoc?.Root is { } stylesRoot)
        {
            styleTable.Load(stylesRoot.Element(OfficeNs + "automatic-styles"));
            styleTable.Load(stylesRoot.Element(OfficeNs + "styles"));
        }

        var spreadsheet = root
            .Element(OfficeNs + "body")?
            .Element(OfficeNs + "spreadsheet");
        if (spreadsheet is null)
        {
            workbook.AddSheet("Sheet1");
            return workbook;
        }

        var sheetIndex = 1;
        foreach (var tableElement in spreadsheet.Elements(TableNs + "table"))
        {
            var name = UniqueSheetName(workbook, (string?)tableElement.Attribute(TableNs + "name"), sheetIndex++);
            var sheet = workbook.AddSheet(name);
            ReadTable(workbook, sheet, tableElement, styleTable);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        var workbookNamedExpressions = spreadsheet.Element(TableNs + "named-expressions");
        if (workbookNamedExpressions is not null)
            ReadNamedExpressions(workbook, workbookNamedExpressions, scopeSheetId: null);
        return workbook;
    }

    private void ReadTable(Workbook workbook, Sheet sheet, XElement tableElement, OdsStyleTable styleTable)
    {
        ReadColumns(sheet, tableElement, styleTable);

        uint row = 0;
        var pendingMerges = new List<GridRange>();
        var pendingMatrices = new List<GridRange>();
        foreach (var rowElement in tableElement.Elements(TableNs + "table-row"))
        {
            var rowRepeat = ReadRepeat(rowElement, TableNs + "number-rows-repeated");
            // Cap pathological repeats to the model max so a sparse file can't blow up memory.
            rowRepeat = (uint)Math.Min(rowRepeat, CellAddress.MaxRow);

            // Read the row's cells once; apply the same content for each repeated row instance.
            var rowStyleName = (string?)rowElement.Attribute(TableNs + "style-name");
            var rowHeight = rowStyleName is not null ? styleTable.GetRowHeight(rowStyleName) : null;

            for (uint r = 0; r < rowRepeat; r++)
            {
                row++;
                if (row > CellAddress.MaxRow) return;
                if (rowHeight is { } h)
                    sheet.RowHeights[row] = h;

                ReadRowCells(workbook, sheet, rowElement, row, styleTable, pendingMerges, pendingMatrices, isFirstRepeat: r == 0);
            }
        }

        foreach (var merge in pendingMerges)
            sheet.AddMergedRegion(merge);

        RegisterMatrixMembers(sheet, pendingMatrices);

        // table:named-expressions may also appear nested inside table:table, holding sheet-scoped
        // named ranges/formulas (per the ODF 1.2 schema, it's the last child of the table element).
        var sheetNamedExpressions = tableElement.Element(TableNs + "named-expressions");
        if (sheetNamedExpressions is not null)
            ReadNamedExpressions(workbook, sheetNamedExpressions, scopeSheetId: sheet.Id);
    }

    /// <summary>
    /// Re-registers the non-anchor cells covered by each matrix-formula extent as provisional spill
    /// members of their anchor, mirroring what the XLSX and legacy .xls loaders do for the cells covered
    /// by a declared array range.
    /// <para>Runs as a post-pass because a matrix's covered cells appear in LATER rows than its anchor,
    /// so they cannot be classified while the anchor's row is being read. Two things depend on it: the
    /// anchor's recalculated spill must be allowed to overwrite these cells (a plain loaded cell would
    /// block it and the anchor would report #SPILL!), and Sheet.TryGetArrayExtent must see the whole
    /// declared block so CommandGuards.RejectIfSplitsArray can enforce "You cannot change part of an
    /// array". Any formula LibreOffice replicated onto a covered cell is dropped -- only the anchor is
    /// an independent formula cell, exactly as in the .xls loader -- while the cached value and style
    /// are kept so the grid shows the loaded results before the first recalc.</para>
    /// </summary>
    private static void RegisterMatrixMembers(Sheet sheet, List<GridRange> pendingMatrices)
    {
        foreach (var matrix in pendingMatrices)
        {
            var anchor = matrix.Start;
            for (var r = matrix.Start.Row; r <= matrix.End.Row; r++)
            {
                for (var c = matrix.Start.Col; c <= matrix.End.Col; c++)
                {
                    if (r == anchor.Row && c == anchor.Col)
                        continue;

                    var existing = sheet.GetCell(r, c);
                    var member = Cell.FromValue(existing?.Value ?? BlankValue.Instance);
                    if (existing is not null)
                        member.StyleId = existing.StyleId;
                    sheet.SetProvisionalSpillCell(anchor, r, c, member);
                }
            }
        }
    }

    private void ReadColumns(Sheet sheet, XElement tableElement, OdsStyleTable styleTable)
    {
        uint col = 0;
        foreach (var columnElement in tableElement.Elements(TableNs + "table-column"))
        {
            var repeat = ReadRepeat(columnElement, TableNs + "number-columns-repeated");
            repeat = (uint)Math.Min(repeat, CellAddress.MaxCol);
            var styleName = (string?)columnElement.Attribute(TableNs + "style-name");
            var width = styleName is not null ? styleTable.GetColumnWidth(styleName) : null;
            for (uint i = 0; i < repeat; i++)
            {
                col++;
                if (col > CellAddress.MaxCol) return;
                if (width is { } w)
                    sheet.ColumnWidths[col] = w;
            }
        }
    }

    private void ReadRowCells(
        Workbook workbook,
        Sheet sheet,
        XElement rowElement,
        uint row,
        OdsStyleTable styleTable,
        List<GridRange> pendingMerges,
        List<GridRange> pendingMatrices,
        bool isFirstRepeat)
    {
        uint col = 0;
        foreach (var cellElement in rowElement.Elements())
        {
            var isCovered = cellElement.Name == TableNs + "covered-table-cell";
            var isCell = cellElement.Name == TableNs + "table-cell";
            if (!isCovered && !isCell)
                continue;

            if (col >= CellAddress.MaxCol) return;

            var repeat = ReadRepeat(cellElement, TableNs + "number-columns-repeated");
            repeat = (uint)Math.Min(repeat, CellAddress.MaxCol);

            // A merge anchor must never be applied per-repeat-instance to multiple columns (spans aren't
            // repeated). Detect spans and treat the cell as non-repeating in that case.
            var rowsSpanned = ReadRepeat(cellElement, TableNs + "number-rows-spanned");
            var colsSpanned = ReadRepeat(cellElement, TableNs + "number-columns-spanned");
            var isMerge = rowsSpanned > 1 || colsSpanned > 1;

            // A matrix (array) formula -- ODF's spelling of Ctrl+Shift+Enter. The anchor cell carries
            // the formula plus the declared extent; the covered cells are ordinary table-cells holding
            // the cached results (NOT covered-table-cell, which is the merge concept above). Like a
            // merge, the declared extent is not repeated, so a matrix anchor is never a repeat run.
            var matrixRows = ReadRepeat(cellElement, TableNs + "number-matrix-rows-spanned");
            var matrixCols = ReadRepeat(cellElement, TableNs + "number-matrix-columns-spanned");
            var isMatrix = cellElement.Attribute(TableNs + "number-matrix-rows-spanned") is not null ||
                cellElement.Attribute(TableNs + "number-matrix-columns-spanned") is not null;

            if (isMerge || isMatrix)
                repeat = 1;

            // Resolve the cell's content/style once per XML element rather than once per repeat
            // instance — none of these depend on the column index.
            string? styleName = null;
            StyleId styleId = StyleId.Default;
            var value = (ScalarValue)BlankValue.Instance;
            string? formula = null;
            if (!isCovered)
            {
                styleName = (string?)cellElement.Attribute(TableNs + "style-name");
                if (styleName is not null && styleTable.GetCellStyle(workbook, styleName) is { } sid)
                    styleId = sid;
                value = ReadCellValue(cellElement, styleTable, styleName);
                formula = ReadFormula(cellElement, row, col);
            }

            // r293/r294: a note or a link is information too. Without them here, a cell carrying only
            // a comment (or only a hyperlink) looked "fully blank" to the skip below and was dropped
            // before it could be read -- so the writer emitted the annotation and the reader threw it
            // away. The skip itself is a DoS guard against a huge repeat count on a blank cell and is
            // deliberately left intact; only the definition of "blank" is corrected.
            var hasInfo = formula is not null
                || value is not BlankValue
                || styleId != StyleId.Default
                || cellElement.Element(OfficeNs + "annotation") is not null
                || cellElement.Descendants(TextNs + "a").Any();
            if (!hasInfo && !isMerge)
            {
                // A covered-merge interior, or a fully blank/style-less cell, carries no information
                // for any of its repeat instances — advance the column cursor for the whole run in
                // O(1) rather than materializing (and re-evaluating) every repeated instance. Without
                // this, a tiny file declaring a huge number-columns-repeated on a blank cell —
                // combined with a huge number-rows-repeated on the enclosing row — would force the
                // reader to iterate the full row*column product (a decompression-bomb style DoS).
                var newCol = (ulong)col + repeat;
                col = newCol > CellAddress.MaxCol ? CellAddress.MaxCol + 1 : (uint)newCol;
                if (col > CellAddress.MaxCol) return;
                continue;
            }

            for (uint i = 0; i < repeat; i++)
            {
                col++;
                if (col > CellAddress.MaxCol) return;
                if (isCovered)
                    continue;

                if (formula is not null)
                {
                    var cell = Cell.FromFormula(formula);
                    if (isMatrix)
                    {
                        // An ODF matrix formula is a declared array (Ctrl+Shift+Enter), so it takes the
                        // same LegacyArrayRows/Cols confinement the XLSX and legacy .xls loaders use for
                        // <f t="array" ref="..."> and BIFF8 array records: RecalcEngine clamps the natural
                        // result to the declared extent and the anchor shows its TOP-LEFT element. Leaving
                        // it Implicit (as every ODS formula cell was before r176) routes the result through
                        // ImplicitIntersection.Resolve instead, which positionally intersects against the
                        // formula cell's OWN row/column -- the rule for an ordinary non-array formula's
                        // automatic @ operator. For a 1x1-declared matrix whose body is a multi-cell range
                        // that silently shows the wrong element; for a multi-cell one it also loses the
                        // extent, so nothing stops an edit from splitting the array.
                        cell.ArrayMode = FormulaArrayMode.Dynamic;
                        cell.LegacyArrayRows = Math.Max(matrixRows, 1);
                        cell.LegacyArrayCols = Math.Max(matrixCols, 1);
                    }
                    else
                    {
                        cell.ArrayMode = FormulaArrayMode.Implicit;
                    }
                    if (value is not BlankValue)
                        cell.Value = value;
                    if (styleId != StyleId.Default)
                        cell.StyleId = styleId;
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);

                    if (isMatrix && isFirstRepeat)
                    {
                        // Same 64-bit widening the merge extent below uses: the spans come straight from
                        // attacker-controlled XML with no upper bound, so uint arithmetic could wrap.
                        var lastRow = (uint)Math.Min(CellAddress.MaxRow, (ulong)row + Math.Max((ulong)matrixRows, 1) - 1);
                        var lastCol = (uint)Math.Min(CellAddress.MaxCol, (ulong)col + Math.Max((ulong)matrixCols, 1) - 1);
                        pendingMatrices.Add(new GridRange(
                            new CellAddress(sheet.Id, row, col),
                            new CellAddress(sheet.Id, lastRow, lastCol)));
                    }
                }
                else if (value is not BlankValue)
                {
                    var cell = Cell.FromValue(value);
                    if (styleId != StyleId.Default)
                        cell.StyleId = styleId;
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
                }
                else if (styleId != StyleId.Default)
                {
                    // A formatted-but-empty (style-only) cell.
                    sheet.SetStyleOnly(row, col, styleId);
                }

                // r293: ODF puts a hyperlink on text:a inside the cell's paragraph, and this adapter
                // read neither side of it -- so every link was lost, while its visible TEXT survived
                // because the reader flattens the paragraph. Placed AFTER the value/formula/style
                // branches above rather than inside one of them: a link can sit on a formula cell or
                // a plain text cell, and a first attempt that hooked only the formula branch left
                // ordinary linked text still losing its target.
                if (HyperlinkTarget(cellElement) is { } href)
                    sheet.Hyperlinks[new CellAddress(sheet.Id, row, col)] = href;

                // r294: the cell's note. Same placement reasoning as the hyperlink above -- a comment
                // can sit on a formula, value or style-only cell, so it is read after the branches
                // rather than inside one of them.
                if (AnnotationText(cellElement) is { } note)
                    sheet.Comments[new CellAddress(sheet.Id, row, col)] = note;

                if (isMerge && isFirstRepeat)
                {
                    // Widen to a 64-bit accumulator before clamping: rowsSpanned/colsSpanned come
                    // straight from the (attacker-controlled) XML with no upper bound, so
                    // `row + rowsSpanned - 1` in uint arithmetic can wrap around and silently produce
                    // a corrupt (and possibly tiny/negative-looking) merge extent.
                    var endRow = (uint)Math.Min(CellAddress.MaxRow, (ulong)row + Math.Max((ulong)rowsSpanned, 1) - 1);
                    var endCol = (uint)Math.Min(CellAddress.MaxCol, (ulong)col + Math.Max((ulong)colsSpanned, 1) - 1);
                    pendingMerges.Add(new GridRange(
                        new CellAddress(sheet.Id, row, col),
                        new CellAddress(sheet.Id, endRow, endCol)));
                }
            }
        }
    }

    private ScalarValue ReadCellValue(XElement cellElement, OdsStyleTable styleTable, string? styleName)
    {
        var valueType = (string?)cellElement.Attribute(OfficeNs + "value-type");
        switch (valueType)
        {
            case "float":
            case "percentage":
            case "currency":
            {
                var raw = (string?)cellElement.Attribute(OfficeNs + "value");
                if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return new NumberValue(d);
                return TextContent(cellElement) is { Length: > 0 } txt &&
                       double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2)
                    ? new NumberValue(d2)
                    : BlankValue.Instance;
            }
            case "date":
            {
                var raw = (string?)cellElement.Attribute(OfficeNs + "date-value");
                // FromDateTime (not a bare ToOADate) so an ODF date in the 1900-01-01..1900-02-28
                // window lands on its Excel serial rather than one day later — see DateTimeValue.
                if (raw is not null && TryParseOdfDate(raw, out var dt))
                    return DateTimeValue.FromDateTime(dt);
                return BlankValue.Instance;
            }
            case "time":
            {
                var raw = (string?)cellElement.Attribute(OfficeNs + "time-value");
                if (raw is not null && TryParseOdfDuration(raw, out var serial))
                    return new DateTimeValue(serial);
                return BlankValue.Instance;
            }
            case "boolean":
            {
                var raw = (string?)cellElement.Attribute(OfficeNs + "boolean-value");
                return new BoolValue(string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase));
            }
            case "string":
            {
                // A literal error value round-trips via the private ods-error-code hint.
                var errorCode = (string?)cellElement.Attribute(TableNs + "ods-error-code");
                if (errorCode is { Length: > 0 })
                    return new ErrorValue(errorCode);
                return new TextValue(TextContent(cellElement));
            }
            default:
                // No/unrecognized office:value-type (e.g. per the ODF spec's optional-attribute rule,
                // a plain string cell may omit it entirely). Fall back to the cell's visible <text:p>
                // content rather than silently dropping it — real Excel/LibreOffice both render it.
                return TextContent(cellElement) is { Length: > 0 } text
                    ? new TextValue(text)
                    : BlankValue.Instance;
        }
    }

    private string? ReadFormula(XElement cellElement, uint row, uint col)
    {
        // Prefer the verbatim FreeX A1 hint when present — guarantees exact round-trip.
        var a1Hint = (string?)cellElement.Attribute(TableNs + "freex-a1-formula");
        if (a1Hint is { Length: > 0 })
            return a1Hint;

        var formula = (string?)cellElement.Attribute(TableNs + "formula");
        if (string.IsNullOrEmpty(formula))
            return null;

        // Strip the leading "of:=" (or "=") namespace+assignment prefix.
        var body = formula;
        if (body.StartsWith("of:", StringComparison.OrdinalIgnoreCase))
            body = body[3..];
        if (body.StartsWith('='))
            body = body[1..];

        return OdsFormulaConverter.ToA1(body);
    }

    /// <summary>
    /// Reads a table:named-expressions container: table:named-range (a named range) and
    /// table:named-expression (a named formula). <paramref name="scopeSheetId"/> is null for the
    /// workbook-level container (office:spreadsheet/table:named-expressions) and the owning sheet's
    /// id when reading the sheet-scoped container nested inside a table:table.
    /// </summary>
    private void ReadNamedExpressions(Workbook workbook, XElement container, SheetId? scopeSheetId)
    {
        foreach (var namedRange in container.Elements(TableNs + "named-range"))
        {
            var name = (string?)namedRange.Attribute(TableNs + "name");
            var cellRange = (string?)namedRange.Attribute(TableNs + "cell-range-address");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cellRange))
                continue;
            if (workbook.ValidateNamedRangeName(name) is not null)
                continue;

            try
            {
                var range = ParseOdfCellRangeAddress(workbook, cellRange);
                if (range is not { } r)
                    continue;
                if (scopeSheetId is { } sid)
                    workbook.DefineNamedRange(name, r, metadata: null, sid);
                else
                    workbook.DefineNamedRange(name, r);
            }
            catch (FormatException) { /* skip unparseable refs */ }
            catch (ArgumentException) { /* skip invalid names/ranges */ }
        }

        foreach (var namedExpression in container.Elements(TableNs + "named-expression"))
        {
            var name = (string?)namedExpression.Attribute(TableNs + "name");
            if (string.IsNullOrEmpty(name) || workbook.ValidateNamedRangeName(name) is not null)
                continue;

            var formulaText = ReadNamedExpressionFormula(namedExpression);
            if (string.IsNullOrEmpty(formulaText))
                continue;

            if (scopeSheetId is { } sid)
                workbook.DefineNamedFormula(name, formulaText, sid);
            else
                workbook.NamedFormulas.TryAdd(name, formulaText);
        }
    }

    private static string? ReadNamedExpressionFormula(XElement namedExpression)
    {
        // Prefer the verbatim FreeX A1 hint when present — mirrors the per-cell formula hint and
        // guarantees exact round-trip.
        var a1Hint = (string?)namedExpression.Attribute(TableNs + "freex-a1-formula");
        if (a1Hint is { Length: > 0 })
            return a1Hint;

        var expression = (string?)namedExpression.Attribute(TableNs + "expression");
        if (string.IsNullOrEmpty(expression))
            return null;

        var body = expression;
        if (body.StartsWith("of:", StringComparison.OrdinalIgnoreCase))
            body = body[3..];
        if (body.StartsWith('='))
            body = body[1..];

        return OdsFormulaConverter.ToA1(body);
    }

    private static GridRange? ParseOdfCellRangeAddress(Workbook workbook, string address)
    {
        // Forms: "$Sheet.$A$1:.$B$2" or "$Sheet.$A$1:$Sheet.$B$2".
        var colon = address.IndexOf(':');
        var first = colon >= 0 ? address[..colon] : address;
        var second = colon >= 0 ? address[(colon + 1)..] : address;

        if (!TryParseOdfSingle(workbook, first, out var sheetId, out var startRow, out var startCol))
            return null;
        // The second endpoint may omit the sheet (".$B$2"); reuse the first's sheet.
        if (!TryParseOdfSingle(workbook, second, out var sheet2, out var endRow, out var endCol))
            return null;

        var sheet = sheet2 ?? sheetId;
        if (sheet is null)
            return null;

        return new GridRange(
            new CellAddress(sheet.Value, startRow, startCol),
            new CellAddress(sheet.Value, endRow, endCol));
    }

    private static bool TryParseOdfSingle(Workbook workbook, string part, out SheetId? sheetId, out uint row, out uint col)
    {
        sheetId = null;
        row = 0;
        col = 0;

        var dot = part.LastIndexOf('.');
        var sheetToken = dot > 0 ? part[..dot] : "";
        var coord = dot >= 0 ? part[(dot + 1)..] : part;

        if (sheetToken.Length > 0)
        {
            var sheetName = sheetToken.TrimStart('$').Trim('\'').Replace("''", "'", StringComparison.Ordinal);
            var sheet = workbook.GetSheet(sheetName);
            if (sheet is null)
                return false;
            sheetId = sheet.Id;
        }

        coord = coord.Replace("$", "", StringComparison.Ordinal);
        if (!CellAddress.TryParse(coord, sheetId ?? SheetId.New(), out var addr))
            return false;
        row = addr.Row;
        col = addr.Col;
        return true;
    }

    // ---- value helpers ---------------------------------------------------------------------------

    /// <summary>
    /// r293: the first hyperlink target in the cell, or null. ODF nests it as
    /// <c>text:p/text:a/@xlink:href</c>; <c>Descendants</c> rather than a fixed path because a link
    /// can sit inside a styled span, which is what LibreOffice writes for a formatted link.
    /// </summary>
    private static string? HyperlinkTarget(XElement cellElement)
    {
        foreach (var anchor in cellElement.Descendants(TextNs + "a"))
        {
            var href = anchor.Attribute(XlinkNs + "href")?.Value;
            if (!string.IsNullOrWhiteSpace(href))
                return href;
        }

        return null;
    }

    /// <summary>
    /// r294: the cell's note text, or null. ODF holds it as <c>office:annotation</c> containing one
    /// <c>text:p</c> per line; the creator/date children it may also carry are metadata this model
    /// does not keep, so only the paragraphs are joined.
    /// </summary>
    private static string? AnnotationText(XElement cellElement)
    {
        var annotation = cellElement.Element(OfficeNs + "annotation");
        if (annotation is null)
            return null;

        var lines = annotation.Elements(TextNs + "p").Select(paragraph => paragraph.Value).ToList();
        if (lines.Count == 0)
            return null;

        var text = string.Join("\n", lines);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string TextContent(XElement cellElement)
    {
        var paragraphs = cellElement.Elements(TextNs + "p").ToList();
        if (paragraphs.Count == 0)
        {
            // r294: the fallback reads the whole subtree, which now includes office:annotation --
            // so a cell carrying ONLY a note and no value would have taken the note's text as its
            // VALUE, inventing content the user never typed. Exclude the annotation explicitly
            // rather than relying on there always being a value paragraph beside it.
            return cellElement.Elements(OfficeNs + "annotation").Any()
                ? string.Concat(cellElement.Nodes()
                    .Where(node => node is not XElement element || element.Name != OfficeNs + "annotation")
                    .Select(node => node is XElement element ? element.Value : node.ToString()))
                : cellElement.Value;
        }

        return string.Join("\n", paragraphs.Select(p => p.Value));
    }

    private static bool TryParseOdfDate(string raw, out DateTime dt)
    {
        // ODF date-value is ISO-8601: "2024-01-31" or "2024-01-31T13:45:00".
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AssumeLocal, out dt);
    }

    private static bool TryParseOdfDuration(string raw, out double serial)
    {
        serial = 0;
        // ODF time-value is an ISO-8601 duration "PThhHmmMss.sssS".
        try
        {
            var ts = System.Xml.XmlConvert.ToTimeSpan(raw);
            serial = ts.TotalDays;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static uint ReadRepeat(XElement element, XName attr)
    {
        var raw = (string?)element.Attribute(attr);
        if (raw is not null && uint.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1)
            return n;
        return 1;
    }

    private static string UniqueSheetName(Workbook workbook, string? proposed, int index)
    {
        var name = string.IsNullOrWhiteSpace(proposed) ? $"Sheet{index}" : proposed!.Trim();
        // Sanitize to Excel's structural rules (<=31, no : \ / ? * [ ]).
        if (Workbook.ContainsInvalidSheetNameCharacter(name))
        {
            foreach (var bad in new[] { ':', '\\', '/', '?', '*', '[', ']' })
                name = name.Replace(bad, '_');
        }
        if (name.Length > 31)
            // r194: see SurrogateSafeTruncation.
            name = SurrogateSafeTruncation.LimitToTextElements(name, 31);
        if (name.StartsWith('\'')) name = name.TrimStart('\'');
        if (name.EndsWith('\'')) name = name.TrimEnd('\'');
        if (string.IsNullOrWhiteSpace(name))
            name = $"Sheet{index}";

        var baseName = name;
        var suffix = 1;
        while (workbook.GetSheet(name) is not null)
        {
            var tail = $" ({suffix++})";
            // r195: see SurrogateSafeTruncation.
            name = baseName.Length + tail.Length > 31
                ? SurrogateSafeTruncation.LimitToTextElements(baseName, 31 - tail.Length) + tail
                : baseName + tail;
        }
        return name;
    }
}
