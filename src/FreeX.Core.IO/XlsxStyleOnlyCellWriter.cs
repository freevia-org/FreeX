using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStyleOnlyCellWriter
{
    public static void Save(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.HasStyleOnlyCells)
                continue;

            var styleOnlyCells = GetWritableCells(sheet);
            if (styleOnlyCells.Count == 0)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var styleIndexes = ReadSeedStyleIndexes(edit.Root, sheet);
            if (styleIndexes.Count == 0)
                continue;

            if (ApplyStyleOnlyCells(edit.Root, sheet, styleOnlyCells, styleIndexes))
                session.MarkDirty(edit);
        }
    }

    public static IEnumerable<StyleOnlyCell> GetSeedCells(Sheet sheet)
    {
        if (!sheet.HasStyleOnlyCells)
            yield break;

        var occupiedCells = sheet.GetOccupiedCellMap();
        var seenStyles = new HashSet<StyleId>();
        foreach (var ((row, col), styleId) in sheet.GetStyleOnlyEntries())
        {
            if (!IsValidWorksheetCell(row, col) ||
                occupiedCells.ContainsKey((row, col)) ||
                !seenStyles.Add(styleId))
            {
                continue;
            }

            yield return new StyleOnlyCell(row, col, styleId);
        }
    }

    private static List<StyleOnlyCell> GetWritableCells(Sheet sheet)
    {
        var cells = new List<StyleOnlyCell>(sheet.StyleOnlyCellCount);
        if (!sheet.HasStyleOnlyCells)
            return cells;

        var occupiedCells = sheet.GetOccupiedCellMap();
        foreach (var ((row, col), styleId) in sheet.GetStyleOnlyEntries())
        {
            if (IsValidWorksheetCell(row, col) && !occupiedCells.ContainsKey((row, col)))
                cells.Add(new StyleOnlyCell(row, col, styleId));
        }

        cells.Sort(static (left, right) =>
        {
            var rowCompare = left.Row.CompareTo(right.Row);
            return rowCompare != 0 ? rowCompare : left.Col.CompareTo(right.Col);
        });
        return cells;
    }

    private static Dictionary<StyleId, string> ReadSeedStyleIndexes(XElement root, Sheet sheet)
    {
        var result = new Dictionary<StyleId, string>();
        var worksheetNs = root.Name.Namespace;
        var sheetData = root.Element(worksheetNs + "sheetData");
        if (sheetData is null)
            return result;

        foreach (var seed in GetSeedCells(sheet))
        {
            if (result.ContainsKey(seed.StyleId))
                continue;

            var reference = ToReference(seed.Row, seed.Col);
            var seedCell = FindCell(sheetData, worksheetNs, reference);
            var styleIndex = seedCell?.Attribute("s")?.Value;
            if (!string.IsNullOrWhiteSpace(styleIndex))
                result[seed.StyleId] = styleIndex;
        }

        return result;
    }

    private static bool ApplyStyleOnlyCells(
        XElement root,
        Sheet sheet,
        IReadOnlyList<StyleOnlyCell> styleOnlyCells,
        IReadOnlyDictionary<StyleId, string> styleIndexes)
    {
        var worksheetNs = root.Name.Namespace;
        var sheetData = EnsureSheetData(root, worksheetNs);
        var rowName = worksheetNs + "row";
        var cellName = worksheetNs + "c";
        var changed = false;

        var rowsByNumber = ReadRowsByNumber(sheetData, rowName);
        XElement? rowInsertionCursor = null;
        uint rowInsertionCursorNumber = 0;

        for (var index = 0; index < styleOnlyCells.Count;)
        {
            var rowNumber = styleOnlyCells[index].Row;
            var row = GetOrCreateRow(
                sheetData,
                rowName,
                rowsByNumber,
                rowNumber,
                ref rowInsertionCursor,
                ref rowInsertionCursorNumber,
                ref changed);
            XElement? insertionCursor = null;
            uint insertionCursorCol = 0;

            do
            {
                var cell = styleOnlyCells[index++];
                if (!styleIndexes.TryGetValue(cell.StyleId, out var styleIndex))
                    continue;

                var reference = ToReference(cell.Row, cell.Col);
                var targetCell = GetOrCreateCellInOrder(
                    row,
                    cellName,
                    reference,
                    cell.Col,
                    ref insertionCursor,
                    ref insertionCursorCol,
                    out var created);
                if (created)
                {
                    SetAttributeIfDifferent(targetCell, "s", styleIndex);
                    changed = true;
                    continue;
                }

                changed |= RewriteStyleOnlyCell(targetCell, worksheetNs, reference, styleIndex);
            }
            while (index < styleOnlyCells.Count && styleOnlyCells[index].Row == rowNumber);
        }

        changed |= UpdateDimension(root, worksheetNs, sheet, styleOnlyCells, styleIndexes);
        return changed;
    }

    private static bool RewriteStyleOnlyCell(XElement cell, XNamespace worksheetNs, string reference, string styleIndex)
    {
        var changed = false;
        changed |= SetAttributeIfDifferent(cell, "r", reference);
        changed |= SetAttributeIfDifferent(cell, "s", styleIndex);
        changed |= RemoveAttributeIfPresent(cell, "t");
        foreach (var child in cell.Elements().Where(IsCellPayloadElement).ToList())
        {
            child.Remove();
            changed = true;
        }

        return changed;

        bool IsCellPayloadElement(XElement child) =>
            child.Name == worksheetNs + "f" ||
            child.Name == worksheetNs + "v" ||
            child.Name == worksheetNs + "is";
    }

    private static bool UpdateDimension(
        XElement root,
        XNamespace worksheetNs,
        Sheet sheet,
        IReadOnlyList<StyleOnlyCell> styleOnlyCells,
        IReadOnlyDictionary<StyleId, string> styleIndexes)
    {
        var hasCell = false;
        uint minRow = 0;
        uint minCol = 0;
        uint maxRow = 0;
        uint maxCol = 0;

        foreach (var (row, col) in sheet.GetOccupiedCellMap().Keys)
            IncludeCell(row, col);

        foreach (var cell in styleOnlyCells)
        {
            if (styleIndexes.ContainsKey(cell.StyleId))
                IncludeCell(cell.Row, cell.Col);
        }

        if (!hasCell)
            return false;

        var reference = minRow == maxRow && minCol == maxCol
            ? ToReference(minRow, minCol)
            : $"{ToReference(minRow, minCol)}:{ToReference(maxRow, maxCol)}";
        var dimension = root.Element(worksheetNs + "dimension");
        if (dimension is null)
        {
            dimension = new XElement(worksheetNs + "dimension");
            root.AddFirst(dimension);
        }

        return SetAttributeIfDifferent(dimension, "ref", reference);

        void IncludeCell(uint row, uint col)
        {
            if (!IsValidWorksheetCell(row, col))
                return;

            if (!hasCell)
            {
                minRow = maxRow = row;
                minCol = maxCol = col;
                hasCell = true;
                return;
            }

            minRow = Math.Min(minRow, row);
            minCol = Math.Min(minCol, col);
            maxRow = Math.Max(maxRow, row);
            maxCol = Math.Max(maxCol, col);
        }
    }

    private static XElement EnsureSheetData(XElement root, XNamespace worksheetNs)
    {
        var sheetData = root.Element(worksheetNs + "sheetData");
        if (sheetData is not null)
            return sheetData;

        sheetData = new XElement(worksheetNs + "sheetData");
        var insertAfter = root.Element(worksheetNs + "sheetFormatPr") ??
                          root.Element(worksheetNs + "sheetViews") ??
                          root.Element(worksheetNs + "dimension");
        if (insertAfter is null)
            root.AddFirst(sheetData);
        else
            insertAfter.AddAfterSelf(sheetData);

        return sheetData;
    }

    private static Dictionary<uint, XElement> ReadRowsByNumber(XElement sheetData, XName rowName)
    {
        var rows = new Dictionary<uint, XElement>();
        foreach (var row in sheetData.Elements(rowName))
        {
            if (TryGetRowNumber(row, out var rowNumber) && rowNumber > 0)
                rows.Add(rowNumber, row);
        }

        return rows;
    }

    private static XElement GetOrCreateRow(
        XElement sheetData,
        XName rowName,
        IDictionary<uint, XElement> rowsByNumber,
        uint rowNumber,
        ref XElement? insertionCursor,
        ref uint insertionCursorNumber,
        ref bool changed)
    {
        if (rowsByNumber.TryGetValue(rowNumber, out var row))
        {
            insertionCursor = row;
            insertionCursorNumber = rowNumber;
            return row;
        }

        row = new XElement(rowName, new XAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture)));
        var rows = insertionCursor is not null && insertionCursorNumber < rowNumber
            ? insertionCursor.ElementsAfterSelf(rowName)
            : sheetData.Elements(rowName);
        var lastSeen = insertionCursor is not null && insertionCursorNumber < rowNumber
            ? insertionCursor
            : null;

        foreach (var existing in rows)
        {
            if (TryGetRowNumber(existing, out var existingRow) && existingRow > rowNumber)
            {
                existing.AddBeforeSelf(row);
                rowsByNumber[rowNumber] = row;
                insertionCursor = row;
                insertionCursorNumber = rowNumber;
                changed = true;
                return row;
            }

            lastSeen = existing;
        }

        if (lastSeen is null)
            sheetData.AddFirst(row);
        else
            lastSeen.AddAfterSelf(row);

        rowsByNumber[rowNumber] = row;
        insertionCursor = row;
        insertionCursorNumber = rowNumber;
        changed = true;
        return row;
    }

    private static XElement GetOrCreateCellInOrder(
        XElement row,
        XName cellName,
        string reference,
        uint col,
        ref XElement? insertionCursor,
        ref uint insertionCursorCol,
        out bool created)
    {
        if (insertionCursor is not null && insertionCursorCol == col)
        {
            created = false;
            return insertionCursor;
        }

        var cells = insertionCursor is not null && insertionCursorCol < col
            ? insertionCursor.ElementsAfterSelf(cellName)
            : row.Elements(cellName);
        var lastSeen = insertionCursor is not null && insertionCursorCol < col
            ? insertionCursor
            : null;

        foreach (var existing in cells)
        {
            if (TryGetCellColumn(existing, out var existingCol))
            {
                if (existingCol == col)
                {
                    insertionCursor = existing;
                    insertionCursorCol = col;
                    created = false;
                    return existing;
                }

                if (existingCol > col)
                {
                    var cell = new XElement(cellName, new XAttribute("r", reference));
                    existing.AddBeforeSelf(cell);
                    insertionCursor = cell;
                    insertionCursorCol = col;
                    created = true;
                    return cell;
                }
            }

            lastSeen = existing;
        }

        var appendedCell = new XElement(cellName, new XAttribute("r", reference));
        if (lastSeen is null)
            row.Add(appendedCell);
        else
            lastSeen.AddAfterSelf(appendedCell);

        insertionCursor = appendedCell;
        insertionCursorCol = col;
        created = true;
        return appendedCell;
    }

    private static XElement? FindCell(XElement sheetData, XNamespace worksheetNs, string reference)
    {
        var rowReferenceLength = reference.TakeWhile(static c => char.IsLetter(c)).Count();
        var rowText = reference[rowReferenceLength..];
        if (!uint.TryParse(rowText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
            return null;

        var rowName = worksheetNs + "row";
        var cellName = worksheetNs + "c";
        foreach (var row in sheetData.Elements(rowName))
        {
            if (!TryGetRowNumber(row, out var currentRow) || currentRow != rowNumber)
                continue;

            return row
                .Elements(cellName)
                .FirstOrDefault(cell => string.Equals(
                    cell.Attribute("r")?.Value,
                    reference,
                    StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool TryGetRowNumber(XElement row, out uint rowNumber) =>
        uint.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rowNumber);

    private static bool TryGetCellColumn(XElement cell, out uint col)
    {
        col = 0;
        var reference = cell.Attribute("r")?.Value;
        return !string.IsNullOrWhiteSpace(reference) &&
               CellAddress.TryParse(reference, default, out var address) &&
               (col = address.Col) > 0;
    }

    private static bool SetAttributeIfDifferent(XElement element, string name, string value)
    {
        var attribute = element.Attribute(name);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool RemoveAttributeIfPresent(XElement element, string name)
    {
        var attribute = element.Attribute(name);
        if (attribute is null)
            return false;

        attribute.Remove();
        return true;
    }

    private static string ToReference(uint row, uint col)
    {
        var columnLength = GetColumnNameLength(col);
        var rowLength = GetRowDigitCount(row);
        return string.Create(
            columnLength + rowLength,
            (Row: row, Col: col, ColumnLength: columnLength),
            static (destination, state) =>
            {
                WriteColumnName(state.Col, destination[..state.ColumnLength]);
                WriteRowNumber(state.Row, destination[state.ColumnLength..]);
            });
    }

    private static int GetColumnNameLength(uint col) =>
        col <= 26 ? 1 :
        col <= 702 ? 2 : 3;

    private static void WriteColumnName(uint col, Span<char> destination)
    {
        for (var index = destination.Length - 1; index >= 0; index--)
        {
            col--;
            destination[index] = (char)('A' + col % 26);
            col /= 26;
        }
    }

    private static int GetRowDigitCount(uint row) =>
        row < 10 ? 1 :
        row < 100 ? 2 :
        row < 1_000 ? 3 :
        row < 10_000 ? 4 :
        row < 100_000 ? 5 :
        row < 1_000_000 ? 6 : 7;

    private static void WriteRowNumber(uint row, Span<char> destination)
    {
        var index = destination.Length;
        do
        {
            destination[--index] = (char)('0' + row % 10);
            row /= 10;
        }
        while (row > 0);
    }

    private static bool IsValidWorksheetCell(uint row, uint col) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        col is >= 1 and <= CellAddress.MaxCol;

    public readonly record struct StyleOnlyCell(uint Row, uint Col, StyleId StyleId);
}
