using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class TextToColumnsPlanner
{
    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, char delimiter)
    {
        return BuildEdits(sheet, range, delimiter.ToString());
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, string delimiters)
    {
        var edits = new List<(CellAddress, Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            if (sheet.GetValue(row, range.Start.Col) is not TextValue cellValue)
                continue;

            var parts = SplitText(cellValue.Value, delimiters);
            for (var index = 0; index < parts.Length; index++)
            {
                var trimmed = parts[index].Trim();
                var address = new CellAddress(sheet.Id, row, range.Start.Col + (uint)index);
                ScalarValue value = double.TryParse(trimmed, out var number)
                    ? new NumberValue(number)
                    : new TextValue(trimmed);
                edits.Add((address, Cell.FromValue(value)));
            }
        }

        return edits;
    }

    public static string[] SplitText(string text, string delimiters)
    {
        var delimiterChars = string.IsNullOrEmpty(delimiters)
            ? [',']
            : delimiters.Distinct().ToArray();
        return text.Split(delimiterChars, StringSplitOptions.None);
    }
}
