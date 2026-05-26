using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static class StructuredTableColumnBuilder
{
    public static IEnumerable<StructuredTableColumnModel> BuildColumns(Sheet sheet, GridRange range, bool firstRowHasHeaders)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;
        for (var col = range.Start.Col; col <= range.End.Col; col++, ordinal++)
        {
            var rawName = firstRowHasHeaders
                ? HeaderText(sheet.GetValue(range.Start.Row, col))
                : string.Empty;
            var baseName = string.IsNullOrWhiteSpace(rawName)
                ? $"Column{ordinal.ToString(CultureInfo.InvariantCulture)}"
                : rawName.Trim();
            var name = MakeUnique(baseName, usedNames);
            usedNames.Add(name);
            yield return new StructuredTableColumnModel(ordinal, name);
        }
    }

    private static string HeaderText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToShortDateString(),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static string MakeUnique(string baseName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; suffix <= 10000; suffix++)
        {
            var candidate = $"{baseName}{suffix.ToString(CultureInfo.InvariantCulture)}";
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        return $"{baseName}{Guid.NewGuid():N}"[..Math.Min(31, baseName.Length + 32)];
    }
}
