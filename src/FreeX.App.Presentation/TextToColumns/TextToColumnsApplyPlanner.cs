using FreeX.Core.Model;

namespace FreeX.App.Presentation.TextToColumns;

public sealed record TextToColumnsSheetApplyPlan(
    SheetId SheetId,
    GridRange SourceRange,
    CellAddress Destination,
    IReadOnlyList<(CellAddress Address, Cell NewCell)> Edits);

public static class TextToColumnsApplyPlanner
{
    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        char delimiter) =>
        BuildEdits(sheet, range, delimiter.ToString());

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        char delimiter,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        BuildEdits(sheet, range, destination, delimiter.ToString(), columnFormats, advancedOptions);

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        string delimiters) =>
        BuildEdits(sheet, range, range.Start, null, null, text => SplitText(text, delimiters));

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        string delimiters,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        BuildEdits(sheet, range, destination, columnFormats, advancedOptions, text => SplitText(text, delimiters));

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne) =>
        BuildEdits(
            sheet,
            range,
            range.Start,
            null,
            null,
            text => SplitText(text, delimiters, textQualifier, treatConsecutiveDelimitersAsOne));

    public static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        BuildEdits(
            sheet,
            range,
            destination,
            columnFormats,
            advancedOptions,
            text => SplitText(text, delimiters, textQualifier, treatConsecutiveDelimitersAsOne));

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        IReadOnlyList<int> breakPositions) =>
        BuildEdits(sheet, range, range.Start, null, null, text => SplitFixedWidthText(text, breakPositions));

    public static List<(CellAddress Address, Cell NewCell)> BuildFixedWidthEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        IReadOnlyList<int> breakPositions,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats = null,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        BuildEdits(sheet, range, destination, columnFormats, advancedOptions, text => SplitFixedWidthText(text, breakPositions));

    public static IReadOnlyList<TextToColumnsSheetApplyPlan> BuildSheetPlans(
        Workbook workbook,
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange sourceRange,
        TextToColumnsDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(targetSheetIds);
        ArgumentNullException.ThrowIfNull(result);

        return targetSheetIds
            .Distinct()
            .Select(sheetId => BuildSheetPlan(workbook.GetSheet(sheetId), sourceRange, result))
            .Where(plan => plan is not null)
            .Cast<TextToColumnsSheetApplyPlan>()
            .ToList();
    }

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Workbook workbook,
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange sourceRange,
        TextToColumnsDialogResult result)
    {
        var targets = new List<CellAddress>();
        foreach (var plan in BuildSheetPlans(workbook, targetSheetIds, sourceRange, result))
        {
            var sheet = workbook.GetSheet(plan.SheetId);
            if (sheet is null)
                continue;

            targets.AddRange(FindOverwriteTargets(sheet, plan.Edits, plan.SourceRange));
        }

        return targets;
    }

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> MapResultToEdits(
        SheetId sheetId,
        TextToColumnsResult result,
        GridRange sourceRange,
        CellAddress? destination = null,
        TextToColumnsAdvancedOptions? advancedOptions = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var edits = new List<(CellAddress Address, Cell NewCell)>();
        var start = destination ?? sourceRange.Start;
        for (var rowIndex = 0; rowIndex < result.Rows.Count; rowIndex++)
        {
            AddFieldEdits(
                edits,
                sheetId,
                start.Row + (uint)rowIndex,
                start.Col,
                result.Rows[rowIndex].Fields,
                result.ColumnCount,
                result.ColumnFormats,
                advancedOptions,
                fillMissingFields: false,
                trimFields: true);
        }

        return edits;
    }

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        IEnumerable<(CellAddress Address, Cell NewCell)> edits,
        GridRange sourceRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(edits);

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

    public static string[] SplitText(string text, string delimiters) =>
        TextToColumnsSplitter.SplitDelimited(text, delimiters);

    public static string[] SplitText(
        string text,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne) =>
        TextToColumnsSplitter.SplitDelimited(text, delimiters, textQualifier, treatConsecutiveDelimitersAsOne);

    public static string[] SplitFixedWidthText(string text, IReadOnlyList<int> breakPositions) =>
        TextToColumnsSplitter.SplitFixedWidth(text, breakPositions);

    private static TextToColumnsSheetApplyPlan? BuildSheetPlan(
        Sheet? sheet,
        GridRange sourceRange,
        TextToColumnsDialogResult result)
    {
        if (sheet is null)
            return null;

        var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(sourceRange, sheet.Id);
        var destination = RemapDestination(result.Destination ?? sourceRange.Start, sheet.Id);
        var edits = result.SplitMode == TextToColumnsSplitMode.FixedWidth
            ? BuildFixedWidthEdits(
                sheet,
                sheetRange,
                destination,
                result.FixedWidthBreakPositions ?? [],
                result.ColumnFormats,
                result.AdvancedOptions)
            : BuildEdits(
                sheet,
                sheetRange,
                destination,
                result.Delimiters,
                result.TextQualifierChar,
                result.TreatConsecutiveDelimitersAsOne,
                result.ColumnFormats,
                result.AdvancedOptions);

        return new TextToColumnsSheetApplyPlan(sheet.Id, sheetRange, destination, edits);
    }

    private static CellAddress RemapDestination(CellAddress destination, SheetId sheetId) =>
        new(sheetId, destination.Row, destination.Col);

    private static List<(CellAddress Address, Cell NewCell)> BuildEdits(
        Sheet sheet,
        GridRange range,
        CellAddress destination,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats,
        TextToColumnsAdvancedOptions? advancedOptions,
        Func<string, string[]> split)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(split);

        var edits = new List<(CellAddress Address, Cell NewCell)>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var sourceAddress = new CellAddress(sheet.Id, row, range.Start.Col);
            var cellValue = sheet.GetValue(sourceAddress);
            if (cellValue is BlankValue)
                continue;

            var targetRow = destination.Row + (row - range.Start.Row);

            string text;
            if (cellValue is TextValue textValue)
            {
                text = textValue.Value;
            }
            else if (new CellAddress(sheet.Id, targetRow, destination.Col) == sourceAddress)
            {
                // Destination is the source cell itself: leaving a non-text value untouched
                // is equivalent to rewriting it, so skip it exactly as before rather than churn it.
                continue;
            }
            else
            {
                // Destination differs from the source cell: a non-text row (e.g. a genuine
                // Number/DateTime/Bool typed directly into the cell) must still carry its value
                // across, or the destination row is silently left blank. Mirrors the Avalonia
                // shell's ReadTextToColumnsSources, which stringifies every scalar type up front.
                text = SpreadsheetDisplayFormatter.FormatScalarValue(cellValue);
            }

            var parts = split(text);
            AddFieldEdits(
                edits,
                sheet.Id,
                targetRow,
                destination.Col,
                parts,
                parts.Length,
                columnFormats,
                advancedOptions,
                fillMissingFields: false,
                trimFields: true);
        }

        return edits;
    }

    private static void AddFieldEdits(
        List<(CellAddress Address, Cell NewCell)> edits,
        SheetId sheetId,
        uint targetRow,
        uint startCol,
        IReadOnlyList<string> fields,
        int fieldCount,
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats,
        TextToColumnsAdvancedOptions? advancedOptions,
        bool fillMissingFields,
        bool trimFields)
    {
        var outputIndex = 0u;
        for (var index = 0; index < fieldCount; index++)
        {
            var columnFormat = GetColumnFormat(columnFormats, index);
            if (columnFormat == TextToColumnsColumnFormat.Skip)
                continue;

            var targetCol = startCol + outputIndex;
            if (targetRow > CellAddress.MaxRow || targetCol > CellAddress.MaxCol)
                continue;

            if (!fillMissingFields && index >= fields.Count)
                continue;

            var text = index < fields.Count ? fields[index] : string.Empty;
            if (trimFields)
                text = text.Trim();

            var address = new CellAddress(sheetId, targetRow, targetCol);
            var value = TextToColumnsValueConverter.ConvertValue(text, columnFormat, advancedOptions);
            edits.Add((address, Cell.FromValue(value)));
            outputIndex++;
        }
    }

    private static TextToColumnsColumnFormat GetColumnFormat(
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats,
        int index) =>
        columnFormats is not null && index >= 0 && index < columnFormats.Count
            ? columnFormats[index]
            : TextToColumnsColumnFormat.General;

    private static bool IsOriginalSourceCell(CellAddress address, GridRange sourceRange) =>
        address.Sheet == sourceRange.Start.Sheet &&
        address.Col == sourceRange.Start.Col &&
        address.Row >= sourceRange.Start.Row &&
        address.Row <= sourceRange.End.Row;
}
