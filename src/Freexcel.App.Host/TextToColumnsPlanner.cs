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
        return BuildEdits(sheet, range, text => SplitText(text, delimiters));
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        IReadOnlyList<int> breakPositions)
    {
        return BuildEdits(sheet, range, text => SplitFixedWidthText(text, breakPositions));
    }

    private static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        Func<string, string[]> split)
    {
        var edits = new List<(CellAddress, Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            if (sheet.GetValue(row, range.Start.Col) is not TextValue cellValue)
                continue;

            var parts = split(cellValue.Value);
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

    public static string[] SplitFixedWidthText(string text, IReadOnlyList<int> breakPositions)
    {
        var positions = breakPositions
            .Where(position => position > 0)
            .Distinct()
            .Order()
            .ToList();
        if (positions.Count == 0)
            return [text];

        var parts = new List<string>();
        var start = 0;
        foreach (var position in positions)
        {
            var end = Math.Min(position, text.Length);
            if (end > start)
                parts.Add(text[start..end]);
            start = Math.Min(position, text.Length);
        }

        if (start < text.Length)
            parts.Add(text[start..]);
        else if (parts.Count == 0)
            parts.Add(string.Empty);

        return parts.ToArray();
    }
}
