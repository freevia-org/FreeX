using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class TextToColumnsPlanner
{
    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, char delimiter) =>
        TextToColumnsApplyPlanner.BuildEdits(sheet, range, delimiter);

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        char delimiter,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsApplyPlanner.BuildEdits(sheet, range, destination, delimiter, columnFormats, advancedOptions);

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(Sheet sheet, GridRange range, string delimiters) =>
        TextToColumnsApplyPlanner.BuildEdits(sheet, range, delimiters);

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        string delimiters,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsApplyPlanner.BuildEdits(sheet, range, destination, delimiters, columnFormats, advancedOptions);

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne) =>
        TextToColumnsApplyPlanner.BuildEdits(sheet, range, delimiters, textQualifier, treatConsecutiveDelimitersAsOne);

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsApplyPlanner.BuildEdits(
            sheet,
            range,
            destination,
            delimiters,
            textQualifier,
            treatConsecutiveDelimitersAsOne,
            columnFormats,
            advancedOptions);

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        IReadOnlyList<int> breakPositions) =>
        TextToColumnsApplyPlanner.BuildFixedWidthEdits(sheet, range, breakPositions);

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        IReadOnlyList<int> breakPositions,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsApplyPlanner.BuildFixedWidthEdits(sheet, range, destination, breakPositions, columnFormats, advancedOptions);

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        IEnumerable<(CellAddress Address, Cell NewCell)> edits,
        GridRange sourceRange) =>
        TextToColumnsApplyPlanner.FindOverwriteTargets(sheet, edits, sourceRange);

    public static string[] SplitText(string text, string delimiters) =>
        TextToColumnsApplyPlanner.SplitText(text, delimiters);

    public static string[] SplitText(
        string text,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne) =>
        TextToColumnsApplyPlanner.SplitText(text, delimiters, textQualifier, treatConsecutiveDelimitersAsOne);

    public static string[] SplitFixedWidthText(string text, IReadOnlyList<int> breakPositions) =>
        TextToColumnsApplyPlanner.SplitFixedWidthText(text, breakPositions);
}
