using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class DataValidationService
{
    private static readonly string MissingBlankCellText = ToValidationText(BlankValue.Instance);

    private static string? ValidateList(DataValidation dv, ScalarValue value)
    {
        if (string.IsNullOrEmpty(dv.Formula1))
            return null;

        // Split once; build case-insensitive set for O(1) lookup.
        var trimmed = ParseInlineListItems(dv.Formula1);
        return ValidateListAgainstValues(dv, value, trimmed);
    }

    private static string? ValidateList(DataValidation dv, ScalarValue value, Sheet sheet, Workbook? workbook)
    {
        if (string.IsNullOrWhiteSpace(dv.Formula1))
            return null;

        var source = dv.Formula1.Trim();
        if (source.StartsWith('='))
        {
            if (TryValidateRangeOrNamedSource(source, sheet, workbook, value, out var rangeMatch))
            {
                if (rangeMatch)
                    return null;

                if (!string.IsNullOrEmpty(dv.ErrorMessage))
                    return dv.ErrorMessage;
            }

            var allowed = ResolveListValues(source, sheet, workbook);
            if (allowed.Count > 0)
                return ValidateListAgainstValues(dv, value, allowed);
        }

        return ValidateList(dv, value);
    }

    private static IReadOnlyCollection<string> ResolveListValues(string formulaText, Sheet sheet, Workbook? workbook)
    {
        var source = formulaText.Trim();
        if (source.StartsWith('='))
        {
            if (TryReadRangeOrNamedSource(source, sheet, workbook, out var rangeValues))
                return rangeValues;

            var result = new FormulaEvaluator().Evaluate(source, sheet, workbook);
            if (result is RangeValue range)
                return range.Flatten().Select(ToValidationText).ToArray();

            if (result is not ErrorValue)
                return new[] { ToValidationText(result) };
        }

        return ParseInlineListItems(formulaText);
    }

    private static IReadOnlyCollection<string> ParseInlineListItems(string text)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                items.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        items.Add(current.ToString().Trim());
        return items;
    }

    private static bool TryReadRangeOrNamedSource(
        string formulaText,
        Sheet sheet,
        Workbook? workbook,
        out IReadOnlyCollection<string> values)
    {
        values = Array.Empty<string>();

        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();

            if (ast is RangeRefNode range)
            {
                var sourceSheet = sheet;
                var sheetName = range.SheetName ?? range.Start.SheetName ?? range.End.SheetName;
                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    sourceSheet = workbook?.GetSheet(sheetName) ?? sheet;
                    if (!string.Equals(sourceSheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                values = ReadRangeValues(sourceSheet, range.Start.Row, range.Start.ColumnNumber, range.End.Row, range.End.ColumnNumber);
                return true;
            }

            if (ast is NamedRangeNode named && workbook is not null && workbook.TryGetNamedRange(named.Name, out var namedRange))
            {
                var sourceSheet = workbook.GetSheet(namedRange.Start.Sheet) ?? sheet;
                values = ReadRangeValues(
                    sourceSheet,
                    namedRange.Start.Row,
                    namedRange.Start.Col,
                    namedRange.End.Row,
                    namedRange.End.Col);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidateRangeOrNamedSource(
        string formulaText,
        Sheet sheet,
        Workbook? workbook,
        ScalarValue value,
        out bool matches)
    {
        if (TryValidateSimpleSameSheetRangeSource(formulaText, sheet, value, out matches))
            return true;

        matches = false;

        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();

            if (ast is RangeRefNode range)
            {
                var sourceSheet = sheet;
                var sheetName = range.SheetName ?? range.Start.SheetName ?? range.End.SheetName;
                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    sourceSheet = workbook?.GetSheet(sheetName) ?? sheet;
                    if (!string.Equals(sourceSheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                matches = RangeContainsValue(
                    sourceSheet,
                    range.Start.Row,
                    range.Start.ColumnNumber,
                    range.End.Row,
                    range.End.ColumnNumber,
                    value);
                return true;
            }

            if (ast is NamedRangeNode named && workbook is not null && workbook.TryGetNamedRange(named.Name, out var namedRange))
            {
                var sourceSheet = workbook.GetSheet(namedRange.Start.Sheet) ?? sheet;
                matches = RangeContainsValue(
                    sourceSheet,
                    namedRange.Start.Row,
                    namedRange.Start.Col,
                    namedRange.End.Row,
                    namedRange.End.Col,
                    value);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidateSimpleSameSheetRangeSource(
        string formulaText,
        Sheet sheet,
        ScalarValue value,
        out bool matches)
    {
        matches = false;
        var source = formulaText.AsSpan().Trim();
        if (source.IsEmpty || source[0] != '=')
            return false;

        source = source[1..].Trim();
        if (source.IndexOf('!') >= 0)
            return false;

        var colon = source.IndexOf(':');
        if (colon < 0)
        {
            if (!TryParseA1Cell(source, sheet.Id, out var cell))
                return false;

            matches = RangeContainsValue(sheet, cell.Row, cell.Col, cell.Row, cell.Col, value);
            return true;
        }

        if (!TryParseA1Cell(source[..colon], sheet.Id, out var start) ||
            !TryParseA1Cell(source[(colon + 1)..], sheet.Id, out var end))
        {
            return false;
        }

        matches = RangeContainsValue(sheet, start.Row, start.Col, end.Row, end.Col, value);
        return true;
    }

    private static bool TryParseA1Cell(ReadOnlySpan<char> text, SheetId sheetId, out CellAddress address)
    {
        var value = text.Trim();
        var index = 0;
        if (index < value.Length && value[index] == '$')
            index++;

        var column = 0u;
        var columnStart = index;
        while (index < value.Length)
        {
            var ch = NormalizeColumnLetter(value[index]);
            if (ch is < 'A' or > 'Z')
                break;

            column = column * 26 + (uint)(ch - 'A' + 1);
            if (column > CellAddress.MaxCol)
            {
                address = default;
                return false;
            }

            index++;
        }

        if (index == columnStart)
        {
            address = default;
            return false;
        }

        if (index < value.Length && value[index] == '$')
            index++;

        var row = 0u;
        var rowStart = index;
        while (index < value.Length)
        {
            var ch = value[index];
            if (ch is < '0' or > '9')
            {
                address = default;
                return false;
            }

            var digit = (uint)(ch - '0');
            if (row > CellAddress.MaxRow / 10 || row == CellAddress.MaxRow / 10 && digit > CellAddress.MaxRow % 10)
            {
                address = default;
                return false;
            }

            row = row * 10 + digit;
            index++;
        }

        if (index == rowStart || row == 0)
        {
            address = default;
            return false;
        }

        address = new CellAddress(sheetId, row, column);
        return true;
    }

    private static char NormalizeColumnLetter(char ch) =>
        ch is >= 'a' and <= 'z' ? (char)(ch - ('a' - 'A')) : ch;

    private static bool RangeContainsValue(
        Sheet sheet,
        uint firstRow,
        uint firstCol,
        uint lastRow,
        uint lastCol,
        ScalarValue value)
    {
        var startRow = Math.Min(firstRow, lastRow);
        var endRow = Math.Max(firstRow, lastRow);
        var startCol = Math.Min(firstCol, lastCol);
        var endCol = Math.Max(firstCol, lastCol);
        var textValue = ToValidationText(value);
        var occupiedCells = sheet.GetOccupiedCellMap();
        var rowCount = (ulong)(endRow - startRow) + 1;
        var colCount = (ulong)(endCol - startCol) + 1;

        if (occupiedCells.Count > 0 &&
            (ulong)occupiedCells.Count <= rowCount * colCount &&
            !CouldMatchMissingBlankCell(textValue))
        {
            if (occupiedCells is Dictionary<(uint Row, uint Col), Cell> occupiedDictionary)
                return OccupiedRangeContainsValue(occupiedDictionary, startRow, endRow, startCol, endCol, textValue);

            return OccupiedRangeContainsValue(occupiedCells, startRow, endRow, startCol, endCol, textValue);
        }

        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                var cellValue = sheet.GetCell(row, col)?.Value ?? BlankValue.Instance;
                if (string.Equals(ToValidationText(cellValue), textValue, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool OccupiedRangeContainsValue(
        Dictionary<(uint Row, uint Col), Cell> occupiedCells,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol,
        string textValue)
    {
        foreach (var ((row, col), cell) in occupiedCells)
        {
            if (row < startRow || row > endRow || col < startCol || col > endCol)
                continue;

            if (string.Equals(ToValidationText(cell.Value), textValue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool OccupiedRangeContainsValue(
        IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells,
        uint startRow,
        uint endRow,
        uint startCol,
        uint endCol,
        string textValue)
    {
        foreach (var ((row, col), cell) in occupiedCells)
        {
            if (row < startRow || row > endRow || col < startCol || col > endCol)
                continue;

            if (string.Equals(ToValidationText(cell.Value), textValue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool CouldMatchMissingBlankCell(string textValue) =>
        string.Equals(textValue, MissingBlankCellText, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string> ReadRangeValues(
        Sheet sheet,
        uint firstRow,
        uint firstCol,
        uint lastRow,
        uint lastCol)
    {
        var startRow = Math.Min(firstRow, lastRow);
        var endRow = Math.Max(firstRow, lastRow);
        var startCol = Math.Min(firstCol, lastCol);
        var endCol = Math.Max(firstCol, lastCol);
        var list = new List<string>();

        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                var cellValue = sheet.GetCell(row, col)?.Value ?? BlankValue.Instance;
                list.Add(ToValidationText(cellValue));
            }
        }

        return list;
    }

    private static string? ValidateListAgainstValues(DataValidation dv, ScalarValue value, IReadOnlyCollection<string> allowedValues)
    {
        var textValue = ToValidationText(value);
        foreach (var allowedValue in allowedValues)
        {
            if (string.Equals(allowedValue, textValue, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return dv.ErrorMessage ?? $"Invalid entry. Allowed values: {string.Join(", ", allowedValues)}";
    }

    private static string ToValidationText(ScalarValue value)
    {
        return value switch
        {
            TextValue t   => t.Value,
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            BoolValue b   => b.Value ? "TRUE" : "FALSE",
            _             => value.ToString() ?? ""
        };
    }
}
