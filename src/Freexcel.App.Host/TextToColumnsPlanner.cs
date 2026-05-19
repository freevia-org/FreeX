using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class TextToColumnsPlanner
{
    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, char delimiter)
    {
        var edits = new List<(CellAddress, Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            if (sheet.GetValue(row, range.Start.Col) is not TextValue cellValue)
                continue;

            var parts = cellValue.Value.Split(delimiter);
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
}
