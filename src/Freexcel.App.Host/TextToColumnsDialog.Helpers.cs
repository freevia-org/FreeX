using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class TextToColumnsDialog
{
    public static TextToColumnsDialogResult CreateResult(
        TextToColumnsDelimiterKind delimiterKind,
        string? customDelimiter = null,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsDialogResultFactory.CreateResult(
            delimiterKind,
            customDelimiter,
            destination,
            columnFormats,
            advancedOptions);

    public static TextToColumnsDialogResult CreateResult(
        IEnumerable<TextToColumnsDelimiterKind> delimiterKinds,
        string? customDelimiter = null,
        TextToColumnsTextQualifier textQualifier = TextToColumnsTextQualifier.DoubleQuote,
        bool treatConsecutiveDelimitersAsOne = false,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsDialogResultFactory.CreateResult(
            delimiterKinds,
            customDelimiter,
            textQualifier,
            treatConsecutiveDelimitersAsOne,
            destination,
            columnFormats,
            advancedOptions);

    public static TextToColumnsDialogResult CreateFixedWidthResult(
        string? breakPositionsText,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsDialogResultFactory.CreateFixedWidthResult(
            breakPositionsText,
            destination,
            columnFormats,
            advancedOptions);

    public static IReadOnlyList<string> BuildPreviewRows(Sheet? sheet, GridRange range, int maxRows = 3) =>
        TextToColumnsDialogResultFactory.BuildPreviewRows(sheet, range, maxRows);

    public static bool CanConvertRange(GridRange range) =>
        TextToColumnsDialogResultFactory.CanConvertRange(range);

    public static bool TryParseDestination(string? input, CellAddress defaultDestination, out CellAddress destination) =>
        TextToColumnsDialogResultFactory.TryParseDestination(input, defaultDestination, out destination);

    public static IReadOnlyList<TextToColumnsColumnFormat> NormalizeColumnFormats(
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats) =>
        TextToColumnsDialogResultFactory.NormalizeColumnFormats(columnFormats);

    public static IReadOnlyList<int> AddFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int position,
        int maxLength) =>
        TextToColumnsDialogResultFactory.AddFixedWidthBreakPosition(breakPositions, position, maxLength);

    public static IReadOnlyList<int> MoveFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index,
        int position,
        int maxLength) =>
        TextToColumnsDialogResultFactory.MoveFixedWidthBreakPosition(breakPositions, index, position, maxLength);

    public static IReadOnlyList<int> RemoveFixedWidthBreakPosition(
        IReadOnlyList<int> breakPositions,
        int index) =>
        TextToColumnsDialogResultFactory.RemoveFixedWidthBreakPosition(breakPositions, index);

    public static IReadOnlyList<int> ParseFixedWidthBreakPositions(string? text) =>
        TextToColumnsDialogResultFactory.ParseFixedWidthBreakPositions(text);

    public static bool TryParseFixedWidthBreakPositions(string? text, int maxLength, out IReadOnlyList<int> positions) =>
        TextToColumnsDialogResultFactory.TryParseFixedWidthBreakPositions(text, maxLength, out positions);

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
}
