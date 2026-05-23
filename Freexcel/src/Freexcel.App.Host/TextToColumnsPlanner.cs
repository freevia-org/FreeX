using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class TextToColumnsPlanner
{
    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, char delimiter)
    {
        return BuildEdits(sheet, range, delimiter.ToString());
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        char delimiter,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        return BuildEdits(sheet, range, destination, delimiter.ToString(), columnFormats, advancedOptions);
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, string delimiters)
    {
        return BuildEdits(sheet, range, range.Start, null, null, text => SplitText(text, delimiters));
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        string delimiters,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        return BuildEdits(sheet, range, destination, columnFormats, advancedOptions, text => SplitText(text, delimiters));
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne)
    {
        return BuildEdits(
            sheet,
            range,
            range.Start,
            null,
            null,
            text => SplitText(text, delimiters, textQualifier, treatConsecutiveDelimitersAsOne));
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        return BuildEdits(
            sheet,
            range,
            destination,
            columnFormats,
            advancedOptions,
            text => SplitText(text, delimiters, textQualifier, treatConsecutiveDelimitersAsOne));
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        IReadOnlyList<int> breakPositions)
    {
        return BuildEdits(sheet, range, range.Start, null, null, text => SplitFixedWidthText(text, breakPositions));
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        IReadOnlyList<int> breakPositions,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        return BuildEdits(sheet, range, destination, columnFormats, advancedOptions, text => SplitFixedWidthText(text, breakPositions));
    }

    private static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats,
        TextToColumnsAdvancedOptions? advancedOptions,
        Func<string, string[]> split)
    {
        var edits = new List<(CellAddress, Cell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            if (sheet.GetValue(row, range.Start.Col) is not TextValue cellValue)
                continue;

            var parts = split(cellValue.Value);
            var outputIndex = 0u;
            for (var index = 0; index < parts.Length; index++)
            {
                var columnFormat = GetColumnFormat(columnFormats, index);
                if (columnFormat == TextToColumnsColumnFormat.Skip)
                    continue;

                var targetRow = destination.Row + (row - range.Start.Row);
                var targetCol = destination.Col + outputIndex;
                if (targetRow > CellAddress.MaxRow || targetCol > CellAddress.MaxCol)
                    continue;

                var trimmed = parts[index].Trim();
                var address = new CellAddress(sheet.Id, targetRow, targetCol);
                ScalarValue value = columnFormat == TextToColumnsColumnFormat.Text
                    ? new TextValue(trimmed)
                    : TryParseNumber(trimmed, advancedOptions, out var number)
                    ? new NumberValue(number)
                    : new TextValue(trimmed);
                edits.Add((address, Cell.FromValue(value)));
                outputIndex++;
            }
        }

        return edits;
    }

    private static TextToColumnsColumnFormat GetColumnFormat(
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats,
        int index) =>
        columnFormats is not null && index >= 0 && index < columnFormats.Count
            ? columnFormats[index]
            : TextToColumnsColumnFormat.General;

    private static bool TryParseNumber(string text, TextToColumnsAdvancedOptions? advancedOptions, out double number)
    {
        if (advancedOptions is null)
            return double.TryParse(text, out number);

        var normalized = text.Trim();
        if (advancedOptions.TrailingMinusNumbers && normalized.EndsWith("-", StringComparison.Ordinal))
            normalized = "-" + normalized[..^1];

        if (!string.IsNullOrEmpty(advancedOptions.ThousandsSeparator))
            normalized = normalized.Replace(advancedOptions.ThousandsSeparator, string.Empty, StringComparison.Ordinal);

        if (!string.IsNullOrEmpty(advancedOptions.DecimalSeparator) && advancedOptions.DecimalSeparator != ".")
            normalized = normalized.Replace(advancedOptions.DecimalSeparator, ".", StringComparison.Ordinal);

        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);
    }

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        IEnumerable<(CellAddress Address, Cell NewCell)> edits,
        GridRange sourceRange)
    {
        var conflicts = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();
        foreach (var (address, _) in edits)
        {
            if (!seen.Add(address) ||
                IsOriginalSourceCell(address, sourceRange) ||
                sheet.GetValue(address) is BlankValue)
            {
                continue;
            }

            conflicts.Add(address);
        }

        return conflicts;
    }

    private static bool IsOriginalSourceCell(CellAddress address, GridRange sourceRange) =>
        address.Sheet == sourceRange.Start.Sheet &&
        address.Col == sourceRange.Start.Col &&
        address.Row >= sourceRange.Start.Row &&
        address.Row <= sourceRange.End.Row;

    public static string[] SplitText(string text, string delimiters)
    {
        return SplitText(text, delimiters, '"', false);
    }

    public static string[] SplitText(
        string text,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne)
    {
        var delimiterChars = string.IsNullOrEmpty(delimiters)
            ? [',']
            : delimiters.Distinct().ToArray();

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQualifiedText = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (textQualifier is { } qualifier && ch == qualifier)
            {
                if (inQualifiedText && index + 1 < text.Length && text[index + 1] == qualifier)
                {
                    current.Append(qualifier);
                    index++;
                    continue;
                }

                inQualifiedText = !inQualifiedText;
                continue;
            }

            if (!inQualifiedText && delimiterChars.Contains(ch))
            {
                parts.Add(current.ToString());
                current.Clear();

                if (treatConsecutiveDelimitersAsOne)
                {
                    while (index + 1 < text.Length && delimiterChars.Contains(text[index + 1]))
                        index++;
                }

                continue;
            }

            current.Append(ch);
        }

        parts.Add(current.ToString());
        return parts.ToArray();
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
