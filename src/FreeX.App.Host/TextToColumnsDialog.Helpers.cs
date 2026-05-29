using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class TextToColumnsDialog
{
    public static TextToColumnsDialogResult CreateResult(
        TextToColumnsDelimiterKind delimiterKind,
        string? customDelimiter = null,
        CellAddress? destination = null,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        var delimiter = TextToColumnsDelimiterPlanner.DelimiterFor(delimiterKind, customDelimiter);

        return new TextToColumnsDialogResult(
            delimiterKind,
            delimiter,
            Destination: destination,
            ColumnFormats: TextToColumnsDialogPlanner.NormalizeColumnFormats(columnFormats),
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
        var delimiterPlan = TextToColumnsDelimiterPlanner.CreatePlan(delimiterKinds, customDelimiter);
        return new TextToColumnsDialogResult(
            delimiterPlan.PrimaryKind,
            delimiterPlan.Delimiters,
            TextQualifier: textQualifier,
            TreatConsecutiveDelimitersAsOne: treatConsecutiveDelimitersAsOne,
            Destination: destination,
            ColumnFormats: TextToColumnsDialogPlanner.NormalizeColumnFormats(columnFormats),
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
            ColumnFormats: TextToColumnsDialogPlanner.NormalizeColumnFormats(columnFormats),
            AdvancedOptions: advancedOptions);
    }

    public static IReadOnlyList<string> BuildPreviewRows(Sheet? sheet, GridRange range, int maxRows = 3) =>
        TextToColumnsDialogPlanner.BuildPreviewRows(sheet, range, maxRows);

    public static bool CanConvertRange(GridRange range) =>
        TextToColumnsDialogPlanner.CanConvertRange(range);

    public static bool TryParseDestination(string? input, CellAddress defaultDestination, out CellAddress destination) =>
        TextToColumnsDialogPlanner.TryParseDestination(input, defaultDestination, out destination);

    public static IReadOnlyList<TextToColumnsColumnFormat> NormalizeColumnFormats(
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats) =>
        TextToColumnsDialogPlanner.NormalizeColumnFormats(columnFormats);

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

    public static bool TryParseFixedWidthBreakPositions(string? text, int maxLength, out IReadOnlyList<int> positions) =>
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions(text, maxLength, out positions);

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
