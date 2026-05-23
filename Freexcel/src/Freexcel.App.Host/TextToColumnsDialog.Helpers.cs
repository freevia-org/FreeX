using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class TextToColumnsDialog
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
            kinds.Add(TextToColumnsDelimiterKind.Comma);

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
        var positions = ParseFixedWidthBreakPositions(breakPositionsText);
        if (positions.Count == 0)
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

public static bool TryParseDestination(string? input, CellAddress defaultDestination, out CellAddress destination)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            destination = defaultDestination;
            return true;
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
        int maxLength)
    {
        if (maxLength <= 1)
            return breakPositions.Distinct().Order().ToList();

        var clamped = Math.Clamp(position, 1, maxLength - 1);
        return breakPositions
            .Append(clamped)
            .Distinct()
            .Order()
            .ToList();
    }

    public static IReadOnlyList<int> MoveFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index,
        int position,
        int maxLength)
    {
        if (index < 0 || index >= breakPositions.Count)
            return breakPositions.Distinct().Order().ToList();

        var updated = breakPositions.ToList();
        updated.RemoveAt(index);
        return AddFixedWidthBreakPosition(updated, position, maxLength);
    }

    public static IReadOnlyList<int> RemoveFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index)
    {
        if (index < 0 || index >= breakPositions.Count)
            return breakPositions.Distinct().Order().ToList();

        var updated = breakPositions.ToList();
        updated.RemoveAt(index);
        return updated.Distinct().Order().ToList();
    }

private static IReadOnlyList<string> NormalizePreviewRows(IEnumerable<string>? previewRows)
    {
        var rows = previewRows?
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .Take(3)
            .ToList() ?? [];

        return rows.Count == 0
            ? ["East,42,Open", "West,7,Closed", "North,18,Ready"]
            : rows;
    }

private static string[] PadRow(IReadOnlyList<string> row, int columnCount)
    {
        var padded = new string[columnCount];
        for (var index = 0; index < columnCount; index++)
            padded[index] = index < row.Count ? row[index] : string.Empty;
        return padded;
    }

public static IReadOnlyList<int> ParseFixedWidthBreakPositions(string? text) =>
        (text ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var position) ? position : 0)
            .Where(position => position > 0)
            .Distinct()
            .Order()
            .ToList();
}
