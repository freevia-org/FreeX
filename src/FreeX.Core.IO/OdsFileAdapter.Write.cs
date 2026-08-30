using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class OdsFileAdapter
{
    /// <summary>Builds the whole content.xml document for a workbook.</summary>
    private XDocument WriteContent(Workbook workbook)
    {
        var styleRegistry = new OdsStyleRegistry(workbook);

        var spreadsheet = new XElement(OfficeNs + "spreadsheet");
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.Kind != SheetKind.Worksheet)
                continue; // chartsheets carry no cell grid we model in ODS
            spreadsheet.Add(WriteTable(workbook, sheet, styleRegistry));
        }

        // Named ranges become table:named-expressions on the spreadsheet element.
        var namedExpressions = WriteNamedExpressions(workbook);
        if (namedExpressions is not null)
            spreadsheet.Add(namedExpressions);

        var body = new XElement(OfficeNs + "body", spreadsheet);

        var automaticStyles = new XElement(OfficeNs + "automatic-styles");
        foreach (var styleElement in styleRegistry.EmitAutomaticStyles())
            automaticStyles.Add(styleElement);

        var root = new XElement(OfficeNs + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", OfficeNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", TableNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", TextNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", StyleNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", FoNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "number", NumberNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "svg", SvgNs.NamespaceName),
            new XAttribute(OfficeNs + "version", "1.2"),
            automaticStyles,
            body);

        return new XDocument(root);
    }

    private XElement WriteTable(Workbook workbook, Sheet sheet, OdsStyleRegistry styleRegistry)
    {
        var table = new XElement(TableNs + "table",
            new XAttribute(TableNs + "name", sheet.Name));

        var used = sheet.GetUsedRange();
        var maxRow = used?.End.Row ?? 0;
        var maxCol = used?.End.Col ?? 0;

        // Extend the bounds so style-only cells and explicit column/row sizes are still emitted.
        foreach (var col in sheet.ColumnWidths.Keys) maxCol = Math.Max(maxCol, col);
        foreach (var row in sheet.RowHeights.Keys) maxRow = Math.Max(maxRow, row);
        foreach (var (key, _) in sheet.GetStyleOnlyEntries())
        {
            maxRow = Math.Max(maxRow, key.Row);
            maxCol = Math.Max(maxCol, key.Col);
        }
        foreach (var region in sheet.MergedRegions)
        {
            maxRow = Math.Max(maxRow, region.End.Row);
            maxCol = Math.Max(maxCol, region.End.Col);
        }

        WriteColumns(sheet, table, maxCol, styleRegistry);

        if (maxRow == 0 || maxCol == 0)
        {
            // Empty sheet: ODF still needs at least one column + row.
            if (table.Element(TableNs + "table-column") is null)
                table.Add(new XElement(TableNs + "table-column"));
            table.Add(new XElement(TableNs + "table-row", new XElement(TableNs + "table-cell")));
            AppendSheetNamedExpressions(workbook, sheet, table);
            return table;
        }

        WriteRows(workbook, sheet, table, maxRow, maxCol, styleRegistry);
        AppendSheetNamedExpressions(workbook, sheet, table);
        return table;
    }

    /// <summary>
    /// Sheet-scoped named ranges/formulas are nested as a table:named-expressions child of their
    /// owning table:table (per the ODF 1.2 schema, the last child of table-table-content), mirroring
    /// how <see cref="WriteNamedExpressions"/> handles workbook-scoped names on the spreadsheet root.
    /// </summary>
    private void AppendSheetNamedExpressions(Workbook workbook, Sheet sheet, XElement table)
    {
        var container = new XElement(TableNs + "named-expressions");

        foreach (var (key, range) in workbook.ScopedNamedRanges)
        {
            if (!key.Sheet.Equals(sheet.Id)) continue;
            if (BuildNamedRangeElement(workbook, key.Name, range) is { } element)
                container.Add(element);
        }

        foreach (var (key, formulaText) in workbook.ScopedNamedFormulas)
        {
            if (!key.Sheet.Equals(sheet.Id)) continue;
            container.Add(BuildNamedExpressionElement(key.Name, formulaText, sheet));
        }

        if (container.HasElements)
            table.Add(container);
    }

    private void WriteColumns(Sheet sheet, XElement table, uint maxCol, OdsStyleRegistry styleRegistry)
    {
        if (maxCol == 0)
        {
            table.Add(new XElement(TableNs + "table-column"));
            return;
        }

        // Group consecutive columns with identical width into number-columns-repeated runs.
        uint col = 1;
        while (col <= maxCol)
        {
            var widthStyle = sheet.ColumnWidths.TryGetValue(col, out var w)
                ? styleRegistry.GetColumnStyle(w)
                : null;
            uint runEnd = col;
            while (runEnd + 1 <= maxCol)
            {
                var nextStyle = sheet.ColumnWidths.TryGetValue(runEnd + 1, out var nw)
                    ? styleRegistry.GetColumnStyle(nw)
                    : null;
                if (!string.Equals(nextStyle, widthStyle, StringComparison.Ordinal))
                    break;
                runEnd++;
            }

            var columnElement = new XElement(TableNs + "table-column");
            if (widthStyle is not null)
                columnElement.SetAttributeValue(TableNs + "style-name", widthStyle);
            var repeat = runEnd - col + 1;
            if (repeat > 1)
                columnElement.SetAttributeValue(TableNs + "number-columns-repeated", repeat.ToString(CultureInfo.InvariantCulture));
            table.Add(columnElement);
            col = runEnd + 1;
        }
    }

    private void WriteRows(Workbook workbook, Sheet sheet, XElement table, uint maxRow, uint maxCol, OdsStyleRegistry styleRegistry)
    {
        // Build a merge lookup: anchor -> (rows, cols); covered-non-anchor cells are suppressed.
        var mergeAnchors = new Dictionary<(uint, uint), (uint Rows, uint Cols)>();
        var mergeCovered = new HashSet<(uint, uint)>();
        foreach (var region in sheet.MergedRegions)
        {
            var rows = region.End.Row - region.Start.Row + 1;
            var cols = region.End.Col - region.Start.Col + 1;
            mergeAnchors[(region.Start.Row, region.Start.Col)] = (rows, cols);
            foreach (var addr in region.AllCells())
            {
                if (addr.Row == region.Start.Row && addr.Col == region.Start.Col) continue;
                mergeCovered.Add((addr.Row, addr.Col));
            }
        }

        for (uint row = 1; row <= maxRow; row++)
        {
            var rowElement = new XElement(TableNs + "table-row");
            if (sheet.RowHeights.TryGetValue(row, out var h))
            {
                var rowStyle = styleRegistry.GetRowStyle(h);
                rowElement.SetAttributeValue(TableNs + "style-name", rowStyle);
            }

            WriteRowCells(workbook, sheet, rowElement, row, maxCol, styleRegistry, mergeAnchors, mergeCovered);
            table.Add(rowElement);
        }
    }

    private void WriteRowCells(
        Workbook workbook,
        Sheet sheet,
        XElement rowElement,
        uint row,
        uint maxCol,
        OdsStyleRegistry styleRegistry,
        Dictionary<(uint, uint), (uint Rows, uint Cols)> mergeAnchors,
        HashSet<(uint, uint)> mergeCovered)
    {
        uint col = 1;
        while (col <= maxCol)
        {
            // A covered (non-anchor) merge cell is emitted as a covered-table-cell placeholder.
            if (mergeCovered.Contains((row, col)))
            {
                rowElement.Add(new XElement(TableNs + "covered-table-cell"));
                col++;
                continue;
            }

            var cellElement = BuildCell(workbook, sheet, row, col, styleRegistry, out var styleSignature);

            if (mergeAnchors.TryGetValue((row, col), out var span))
            {
                if (span.Rows > 1)
                    cellElement.SetAttributeValue(TableNs + "number-rows-spanned", span.Rows.ToString(CultureInfo.InvariantCulture));
                if (span.Cols > 1)
                    cellElement.SetAttributeValue(TableNs + "number-columns-spanned", span.Cols.ToString(CultureInfo.InvariantCulture));
                rowElement.Add(cellElement);
                col++;
                continue;
            }

            // Collapse identical, structurally-trivial cells (empty + same style) into a repeated run.
            uint runEnd = col;
            if (IsRepeatableEmptyCell(cellElement))
            {
                while (runEnd + 1 <= maxCol &&
                       !mergeCovered.Contains((row, runEnd + 1)) &&
                       !mergeAnchors.ContainsKey((row, runEnd + 1)))
                {
                    var next = BuildCell(workbook, sheet, row, runEnd + 1, styleRegistry, out var nextSig);
                    if (!IsRepeatableEmptyCell(next) || !string.Equals(nextSig, styleSignature, StringComparison.Ordinal))
                        break;
                    runEnd++;
                }
            }

            var repeat = runEnd - col + 1;
            if (repeat > 1)
            {
                // Do not emit a trailing run of unstyled empties (they carry no information).
                if (styleSignature.Length == 0 && runEnd == maxCol)
                    return;
                cellElement.SetAttributeValue(TableNs + "number-columns-repeated", repeat.ToString(CultureInfo.InvariantCulture));
            }
            rowElement.Add(cellElement);
            col = runEnd + 1;
        }
    }

    private static bool IsRepeatableEmptyCell(XElement cell) =>
        cell.Name == TableNs + "table-cell" &&
        !cell.Elements().Any() &&
        cell.Attribute(OfficeNs + "value-type") is null;

    /// <summary>
    /// Builds one table:table-cell. <paramref name="styleSignature"/> is a cheap identity string used by
    /// the run-length collapser to decide whether adjacent empty cells can merge.
    /// </summary>
    private XElement BuildCell(Workbook workbook, Sheet sheet, uint row, uint col, OdsStyleRegistry styleRegistry, out string styleSignature)
    {
        var addr = new CellAddress(sheet.Id, row, col);
        var cell = sheet.GetCell(row, col);

        StyleId styleId = StyleId.Default;
        if (cell is not null)
            styleId = cell.StyleId;
        else if (sheet.GetStyleOnly(row, col) is { } soStyle)
            styleId = soStyle;

        var cellStyleName = styleRegistry.GetCellStyle(styleId);
        var cellElement = new XElement(TableNs + "table-cell");
        if (cellStyleName is not null)
            cellElement.SetAttributeValue(TableNs + "style-name", cellStyleName);

        styleSignature = cellStyleName ?? "";

        if (cell is null)
            return cellElement;

        // Formula (with cached value/type so a reader without recalc still shows a result).
        if (cell.HasFormula && cell.FormulaText is { Length: > 0 } formulaText)
        {
            var body = formulaText.StartsWith('=') ? formulaText[1..] : formulaText;
            var odf = "of:=" + OdsFormulaConverter.ToOdf(body);
            cellElement.SetAttributeValue(TableNs + "formula", odf);
            // Carry the exact FreeX A1 body verbatim so the formula round-trips losslessly regardless of
            // OpenFormula edge cases (structured-table refs, defined names, etc.) that a bidirectional
            // bracket conversion cannot represent faithfully. Read prefers this hint.
            cellElement.SetAttributeValue(TableNs + "freex-a1-formula", body);
        }

        WriteCellValue(cellElement, cell.Value, styleId, workbook);
        // A value/formula cell can never collapse into an empty run.
        styleSignature = "\x01" + styleSignature;
        return cellElement;
    }

    private void WriteCellValue(XElement cellElement, ScalarValue value, StyleId styleId, Workbook workbook)
    {
        switch (value)
        {
            case NumberValue n when double.IsFinite(n.Value):
            {
                // A number whose style format is a date/percentage/currency keeps its ODF value-type so
                // the typed semantics round-trip. We infer the value-type from the number format string.
                var fmt = workbook.GetStyleNumberFormat(styleId);
                if (OdsNumberFormat.IsPercentage(fmt))
                {
                    cellElement.SetAttributeValue(OfficeNs + "value-type", "percentage");
                    cellElement.SetAttributeValue(OfficeNs + "value", n.Value.ToString("R", CultureInfo.InvariantCulture));
                }
                else if (OdsNumberFormat.IsCurrency(fmt))
                {
                    cellElement.SetAttributeValue(OfficeNs + "value-type", "currency");
                    cellElement.SetAttributeValue(OfficeNs + "value", n.Value.ToString("R", CultureInfo.InvariantCulture));
                }
                else
                {
                    cellElement.SetAttributeValue(OfficeNs + "value-type", "float");
                    cellElement.SetAttributeValue(OfficeNs + "value", n.Value.ToString("R", CultureInfo.InvariantCulture));
                }
                cellElement.Add(TextParagraph(n.Value.ToString("R", CultureInfo.InvariantCulture)));
                break;
            }
            case NumberValue:
                // Non-finite (NaN/Inf): write as text so the file stays valid.
                cellElement.SetAttributeValue(OfficeNs + "value-type", "string");
                cellElement.Add(TextParagraph(value.ToString() ?? ""));
                break;
            case DateTimeValue d when double.IsFinite(d.Value):
            {
                // ToDateTime (not a bare FromOADate) so the serial written back is the mirror of the
                // one OdsFileAdapter.Read parsed — see DateTimeValue's 1900 serial note.
                var dt = d.ToDateTime();
                cellElement.SetAttributeValue(OfficeNs + "value-type", "date");
                // Preserve a time component when present (no date part => time-of-day only is still valid).
                var iso = dt.TimeOfDay == TimeSpan.Zero
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dt.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
                cellElement.SetAttributeValue(OfficeNs + "date-value", iso);
                cellElement.Add(TextParagraph(iso));
                break;
            }
            case BoolValue b:
                cellElement.SetAttributeValue(OfficeNs + "value-type", "boolean");
                cellElement.SetAttributeValue(OfficeNs + "boolean-value", b.Value ? "true" : "false");
                cellElement.Add(TextParagraph(b.Value ? "TRUE" : "FALSE"));
                break;
            case ErrorValue e:
                // ODF marks formula errors via the cell value; for a literal error we keep the code as
                // text-with-string-type and tag it so the reader recognizes it.
                cellElement.SetAttributeValue(OfficeNs + "value-type", "string");
                cellElement.SetAttributeValue(TableNs + "ods-error-code", e.Code); // round-trip hint
                cellElement.Add(TextParagraph(e.Code));
                break;
            case TextValue t:
                cellElement.SetAttributeValue(OfficeNs + "value-type", "string");
                cellElement.Add(TextParagraph(t.Value));
                break;
            case BlankValue:
            default:
                break;
        }
    }

    private static XElement TextParagraph(string text) =>
        new(TextNs + "p", text);

    /// <summary>
    /// Workbook-scoped named ranges and named formulas become table:named-expressions on the
    /// office:spreadsheet element. Sheet-scoped names are written separately, nested inside their
    /// owning table:table (see <see cref="AppendSheetNamedExpressions"/>).
    /// </summary>
    private XElement? WriteNamedExpressions(Workbook workbook)
    {
        var container = new XElement(TableNs + "named-expressions");

        foreach (var (name, range) in workbook.NamedRanges)
        {
            if (BuildNamedRangeElement(workbook, name, range) is { } element)
                container.Add(element);
        }

        var defaultSheet = workbook.Sheets.Count > 0 ? workbook.Sheets[0] : null;
        foreach (var (name, formulaText) in workbook.NamedFormulas)
        {
            if (defaultSheet is null) continue;
            container.Add(BuildNamedExpressionElement(name, formulaText, defaultSheet));
        }

        return container.HasElements ? container : null;
    }

    private XElement? BuildNamedRangeElement(Workbook workbook, string name, GridRange range)
    {
        var sheet = workbook.GetSheet(range.Start.Sheet);
        if (sheet is null) return null;
        var sheetName = sheet.Name;
        var cellRange = "$" + QuoteOds(sheetName) + "." +
            "$" + CellAddress.NumberToColumnName(range.Start.Col) + "$" + range.Start.Row +
            ":.$" + CellAddress.NumberToColumnName(range.End.Col) + "$" + range.End.Row;
        var baseCell = "$" + QuoteOds(sheetName) + ".$" +
            CellAddress.NumberToColumnName(range.Start.Col) + "$" + range.Start.Row;
        return new XElement(TableNs + "named-range",
            new XAttribute(TableNs + "name", name),
            new XAttribute(TableNs + "base-cell-address", baseCell),
            new XAttribute(TableNs + "cell-range-address", cellRange));
    }

    /// <summary>
    /// Builds a table:named-expression (a named formula). OpenFormula requires a base-cell-address
    /// even for a formula that carries no cell reference of its own; like Excel/ClosedXML's own
    /// defined-name base, we anchor it at the owning sheet's A1.
    /// </summary>
    private XElement BuildNamedExpressionElement(string name, string formulaText, Sheet baseSheet)
    {
        var body = formulaText.StartsWith('=') ? formulaText[1..] : formulaText;
        var odf = "of:=" + OdsFormulaConverter.ToOdf(body);
        var baseCell = "$" + QuoteOds(baseSheet.Name) + ".$A$1";
        var element = new XElement(TableNs + "named-expression",
            new XAttribute(TableNs + "name", name),
            new XAttribute(TableNs + "base-cell-address", baseCell),
            new XAttribute(TableNs + "expression", odf));
        // Carry the exact FreeX A1 body verbatim, mirroring the per-cell freex-a1-formula hint, so the
        // named formula round-trips losslessly regardless of OpenFormula conversion edge cases.
        element.SetAttributeValue(TableNs + "freex-a1-formula", body);
        return element;
    }

    private static string QuoteOds(string sheet)
    {
        var needsQuote = sheet.Length == 0 || char.IsDigit(sheet[0]);
        foreach (var ch in sheet)
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.') { needsQuote = true; break; }
        return needsQuote ? "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'" : sheet;
    }
}
