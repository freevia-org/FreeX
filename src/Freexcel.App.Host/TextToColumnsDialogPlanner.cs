using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class TextToColumnsDialogPlanner
{
    public static IReadOnlyList<string> BuildPreviewRows(Sheet? sheet, GridRange range, int maxRows = 3)
    {
        if (sheet is null)
            return [];

        var rows = new List<string>();
        for (var row = range.Start.Row; row <= range.End.Row && rows.Count < maxRows; row++)
        {
            if (sheet.GetValue(row, range.Start.Col) is TextValue text && !string.IsNullOrWhiteSpace(text.Value))
                rows.Add(text.Value);
        }

        return rows;
    }

    public static bool CanConvertRange(GridRange range) =>
        range.Start.Col == range.End.Col;

    public static bool TryParseDestination(string? input, CellAddress defaultDestination, out CellAddress destination)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            destination = default;
            return false;
        }

        return CellReferenceInputParser.TryParseCell(input, defaultDestination.Sheet, out destination);
    }

    public static IReadOnlyList<TextToColumnsColumnFormat> NormalizeColumnFormats(
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats)
    {
        if (columnFormats is null || columnFormats.Count == 0)
            return [];

        var normalized = columnFormats.ToList();
        while (normalized.Count > 0 && normalized[^1] == TextToColumnsColumnFormat.General)
            normalized.RemoveAt(normalized.Count - 1);
        return normalized;
    }

    public static bool TryParseAdvancedSeparator(string? value, out string separator)
    {
        separator = string.Empty;
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length != 1)
            return false;

        separator = trimmed;
        return true;
    }

    public static TextToColumnsTextQualifier TextQualifierFromSelectedIndex(int selectedIndex) =>
        selectedIndex switch
        {
            1 => TextToColumnsTextQualifier.SingleQuote,
            2 => TextToColumnsTextQualifier.None,
            _ => TextToColumnsTextQualifier.DoubleQuote
        };

    public static TextToColumnsColumnFormat DateColumnFormatFromLabel(string? label) =>
        label switch
        {
            "DMY" => TextToColumnsColumnFormat.DateDMY,
            "YMD" => TextToColumnsColumnFormat.DateYMD,
            "MYD" => TextToColumnsColumnFormat.DateMYD,
            "DYM" => TextToColumnsColumnFormat.DateDYM,
            "YDM" => TextToColumnsColumnFormat.DateYDM,
            _ => TextToColumnsColumnFormat.DateMDY
        };

    public static bool IsDateColumnFormat(TextToColumnsColumnFormat format) =>
        format is TextToColumnsColumnFormat.DateMDY
            or TextToColumnsColumnFormat.DateDMY
            or TextToColumnsColumnFormat.DateYMD
            or TextToColumnsColumnFormat.DateMYD
            or TextToColumnsColumnFormat.DateDYM
            or TextToColumnsColumnFormat.DateYDM;

    public static string DateColumnFormatLabel(TextToColumnsColumnFormat format) =>
        format switch
        {
            TextToColumnsColumnFormat.DateDMY => "DMY",
            TextToColumnsColumnFormat.DateYMD => "YMD",
            TextToColumnsColumnFormat.DateMYD => "MYD",
            TextToColumnsColumnFormat.DateDYM => "DYM",
            TextToColumnsColumnFormat.DateYDM => "YDM",
            _ => "MDY"
        };

    public static IReadOnlyList<TextToColumnsColumnFormat> BuildColumnFormats(
        int columnCount,
        IReadOnlyDictionary<int, TextToColumnsColumnFormat> storedFormats)
    {
        var formats = Enumerable.Range(0, columnCount)
            .Select(index => storedFormats.TryGetValue(index, out var format)
                ? format
                : TextToColumnsColumnFormat.General)
            .ToList();
        return NormalizeColumnFormats(formats);
    }
}
