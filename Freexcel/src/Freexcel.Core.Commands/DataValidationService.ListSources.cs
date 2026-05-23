using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class DataValidationService
{
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
        var allowed = new HashSet<string>(allowedValues, StringComparer.OrdinalIgnoreCase);
        var textValue = ToValidationText(value);

        return allowed.Contains(textValue)
            ? null
            : dv.ErrorMessage ?? $"Invalid entry. Allowed values: {string.Join(", ", allowedValues)}";
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
