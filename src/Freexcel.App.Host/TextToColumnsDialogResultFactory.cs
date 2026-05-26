using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class TextToColumnsDialogResultFactory
{
    public static TextToColumnsDialogResult CreateResult(
        TextToColumnsDelimiterKind delimiterKind,
        string? customDelimiter = null,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        var delimiter = delimiterKind switch
        {
            TextToColumnsDelimiterKind.Comma => ",",
            TextToColumnsDelimiterKind.Semicolon => ";",
            TextToColumnsDelimiterKind.Tab => "\t",
            TextToColumnsDelimiterKind.Space => " ",
            TextToColumnsDelimiterKind.Custom => string.IsNullOrEmpty(customDelimiter)
                ? throw new ArgumentException("Custom delimiter is required.", nameof(customDelimiter))
                : customDelimiter,
            _ => throw new ArgumentOutOfRangeException(nameof(delimiterKind), delimiterKind, "Unsupported delimiter.")
        };

        return new TextToColumnsDialogResult(
            delimiterKind,
            delimiter,
            Destination: destination,
            ColumnFormats: NormalizeColumnFormats(columnFormats),
            AdvancedOptions: advancedOptions);
    }

    public static TextToColumnsDialogResult CreateResult(
        IEnumerable<TextToColumnsDelimiterKind> delimiterKinds,
        string? customDelimiter = null,
        TextToColumnsTextQualifier textQualifier = TextToColumnsTextQualifier.DoubleQuote,
        bool treatConsecutiveDelimitersAsOne = false,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        var kinds = delimiterKinds.Distinct().ToList();
        if (kinds.Count == 0)
            throw new ArgumentException("Select at least one delimiter.", nameof(delimiterKinds));

        var delimiters = string.Concat(kinds.Select(kind => CreateResult(kind, customDelimiter).Delimiter));
        var primaryKind = kinds.Contains(TextToColumnsDelimiterKind.Custom)
            ? TextToColumnsDelimiterKind.Custom
            : kinds[0];
        return new TextToColumnsDialogResult(
            primaryKind,
            delimiters,
            TextQualifier: textQualifier,
            TreatConsecutiveDelimitersAsOne: treatConsecutiveDelimitersAsOne,
            Destination: destination,
            ColumnFormats: NormalizeColumnFormats(columnFormats),
            AdvancedOptions: advancedOptions);
    }

    public static TextToColumnsDialogResult CreateFixedWidthResult(
        string? breakPositionsText,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        if (!TryParseFixedWidthBreakPositions(breakPositionsText, int.MaxValue, out var positions))
            throw new ArgumentException("Enter at least one fixed-width break position.", nameof(breakPositionsText));

        return new TextToColumnsDialogResult(
            TextToColumnsDelimiterKind.Comma,
            string.Empty,
            TextToColumnsSplitMode.FixedWidth,
            positions,
            Destination: destination,
            ColumnFormats: NormalizeColumnFormats(columnFormats),
            AdvancedOptions: advancedOptions);
    }

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

        return CellAddress.TryParse(input.Trim(), defaultDestination.Sheet, out destination);
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

    public static IReadOnlyList<int> AddFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int position,
        int maxLength) =>
        TextToColumnsFixedWidthBreakPlanner.AddBreakPosition(breakPositions, position, maxLength);

    public static IReadOnlyList<int> MoveFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index,
        int position,
        int maxLength) =>
        TextToColumnsFixedWidthBreakPlanner.MoveBreakPosition(breakPositions, index, position, maxLength);

    public static IReadOnlyList<int> RemoveFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index) =>
        TextToColumnsFixedWidthBreakPlanner.RemoveBreakPosition(breakPositions, index);

    public static IReadOnlyList<int> ParseFixedWidthBreakPositions(string? text) =>
        TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions(text);

    public static bool TryParseFixedWidthBreakPositions(string? text, int maxLength, out IReadOnlyList<int> positions)
        => TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions(text, maxLength, out positions);
}
