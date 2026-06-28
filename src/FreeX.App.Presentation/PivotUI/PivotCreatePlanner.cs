using FreeX.App.Presentation.TableUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public enum PivotCreateSourceRangeError
{
    None,
    MissingSource,
    MinimumShape,
    MissingHeaders
}

public sealed record PivotCreateSourceRangePlan(GridRange? SourceRange, PivotCreateSourceRangeError Error)
{
    public bool IsValid => Error == PivotCreateSourceRangeError.None && SourceRange is not null;
}

public sealed record RecommendedPivotTableLayout(
    string Title,
    IReadOnlyList<int> RowFieldIndexes,
    IReadOnlyList<int> DataFieldIndexes);

/// <summary>
/// UI-free planning for PivotTable creation dialogs. Shells own windows, range pickers, and command execution;
/// this planner owns source-range expansion/validation, source-field defaults, PivotTable name selection, and
/// command construction so desktop renderers create pivots from the same domain decisions.
/// </summary>
public static class PivotCreatePlanner
{
    public sealed record SourceField(int Index, string Header, bool IsNumeric);

    public enum FieldRole
    {
        Unused,
        Row,
        Value,
    }

    public sealed record DefaultLayout(
        IReadOnlyList<int> RowFieldIndexes,
        IReadOnlyList<int> DataFieldIndexes);

    public static bool IsValidSource(GridRange source) =>
        source.ColCount >= 1 && source.RowCount >= 2;

    public static GridRange CreateSourceRange(Sheet sheet, GridRange selectedRange) =>
        CreateSourceRangePlan(sheet, selectedRange) is { IsValid: true, SourceRange: { } sourceRange }
            ? sourceRange
            : selectedRange;

    public static PivotCreateSourceRangePlan CreateSourceRangePlan(Sheet? sheet, GridRange? selectedRange)
    {
        if (sheet is null || selectedRange is null)
            return new PivotCreateSourceRangePlan(null, PivotCreateSourceRangeError.MissingSource);

        var sourceRange = IsValidExplicitSourceRange(selectedRange.Value)
            ? selectedRange.Value
            : TableCreationPlanner.ExpandToCurrentRegion(sheet, selectedRange.Value);

        if (sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
            return new PivotCreateSourceRangePlan(sourceRange, PivotCreateSourceRangeError.MinimumShape);

        if (!TableCreationPlanner.HasCompleteHeaderRow(sheet, sourceRange))
            return new PivotCreateSourceRangePlan(sourceRange, PivotCreateSourceRangeError.MissingHeaders);

        return new PivotCreateSourceRangePlan(sourceRange, PivotCreateSourceRangeError.None);
    }

    public static string FormatRange(Workbook workbook, SheetId sheetId, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheetName = workbook.GetSheet(sheetId)?.Name;
        var address = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"{SheetNameFormatter.QuoteIfNeeded(sheetName)}!{address}";
    }

    public static string FormatDefaultDestination(Workbook workbook, SheetId sheetId, GridRange sourceRange)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheetName = workbook.GetSheet(sheetId)?.Name;
        var col = Math.Min(sourceRange.End.Col + 2, CellAddress.MaxCol);
        var address = new CellAddress(sheetId, sourceRange.Start.Row, col).ToA1();
        return string.IsNullOrWhiteSpace(sheetName)
            ? address
            : $"{SheetNameFormatter.QuoteIfNeeded(sheetName)}!{address}";
    }

    public static IReadOnlyList<SourceField> ReadFields(Sheet sheet, GridRange source)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var fields = new List<SourceField>();
        var headerRow = source.Start.Row;

        for (var col = source.Start.Col; col <= source.End.Col; col++)
        {
            var index = checked((int)(col - source.Start.Col));
            var header = HeaderText(sheet.GetValue(headerRow, col), index);
            fields.Add(new SourceField(index, header, ColumnHasNumericData(sheet, source, col)));
        }

        return fields;
    }

    public static IReadOnlyDictionary<int, FieldRole> DefaultRoles(IReadOnlyList<SourceField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var roles = new Dictionary<int, FieldRole>();
        foreach (var field in fields)
            roles[field.Index] = FieldRole.Unused;

        if (fields.Count == 0)
            return roles;

        if (fields.Count == 1)
        {
            roles[fields[0].Index] = FieldRole.Value;
            return roles;
        }

        var numeric = fields.Where(field => field.IsNumeric).ToList();
        if (numeric.Count > 0)
        {
            foreach (var field in numeric)
                roles[field.Index] = FieldRole.Value;

            var firstNonNumeric = fields.FirstOrDefault(field => !field.IsNumeric);
            roles[(firstNonNumeric ?? fields[0]).Index] = FieldRole.Row;
        }
        else
        {
            roles[fields[0].Index] = FieldRole.Row;
            roles[fields[^1].Index] = FieldRole.Value;
        }

        return roles;
    }

    public static IReadOnlyList<int> RowIndexes(IReadOnlyDictionary<int, FieldRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles
            .Where(pair => pair.Value == FieldRole.Row)
            .Select(pair => pair.Key)
            .OrderBy(index => index)
            .ToList();
    }

    public static IReadOnlyList<int> ValueIndexes(IReadOnlyDictionary<int, FieldRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles
            .Where(pair => pair.Value == FieldRole.Value)
            .Select(pair => pair.Key)
            .OrderBy(index => index)
            .ToList();
    }

    public static DefaultLayout CreateDefaultLayout(Sheet sheet, GridRange sourceRange)
    {
        var fields = ReadFields(sheet, sourceRange);
        var roles = DefaultRoles(fields);
        return new DefaultLayout(RowIndexes(roles), ValueIndexes(roles));
    }

    public static int ChooseDefaultDataField(Sheet sheet, GridRange sourceRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            if (ColumnHasNumericData(sheet, sourceRange, col))
                return checked((int)(col - sourceRange.Start.Col));
        }

        return checked((int)Math.Min(1, sourceRange.ColCount - 1));
    }

    public static string SuggestName(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var existing = workbook.Sheets
            .SelectMany(sheet => sheet.PivotTables)
            .Select(pivot => pivot.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; ; index++)
        {
            var candidate = $"PivotTable{index}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    public static string SuggestName(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        for (var index = sheet.PivotTables.Count + 1; index <= 10000; index++)
        {
            var candidate = $"PivotTable{index}";
            if (sheet.PivotTables.All(pivot => !PivotTableNameEquals(pivot, candidate)))
                return candidate;
        }

        return $"PivotTable{Guid.NewGuid():N}"[..31];
    }

    public static AddPivotTableToNewWorksheetCommand BuildNewWorksheetCommand(
        GridRange source,
        string name,
        IReadOnlyList<int> rowIndexes,
        IReadOnlyList<int> dataIndexes) =>
        new(source, name, rowIndexes, dataIndexes);

    public static AddPivotTableCommand BuildInPlaceCommand(
        SheetId targetSheetId,
        GridRange source,
        GridRange targetRange,
        string name,
        IReadOnlyList<int> rowIndexes,
        IReadOnlyList<int> dataIndexes) =>
        new(targetSheetId, source, targetRange, name, rowIndexes, dataIndexes);

    public static IWorkbookCommand BuildCommand(
        GridRange source,
        string name,
        IReadOnlyList<int> rowIndexes,
        IReadOnlyList<int> dataIndexes,
        SheetId targetSheetId,
        CellAddress? target)
    {
        if (target is { } cell)
        {
            var targetRange = new GridRange(cell, cell);
            return BuildInPlaceCommand(targetSheetId, source, targetRange, name, rowIndexes, dataIndexes);
        }

        return BuildNewWorksheetCommand(source, name, rowIndexes, dataIndexes);
    }

    public static IReadOnlyList<RecommendedPivotTableLayout> CreateRecommendedLayouts(Sheet sheet, GridRange sourceRange)
    {
        if (sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
            return [];

        var dataFieldIndexes = GetPreferredDataFieldIndexes(sheet, sourceRange);
        var layouts = new List<RecommendedPivotTableLayout>();
        foreach (var dataFieldIndex in dataFieldIndexes)
        {
            foreach (var rowFieldIndex in GetPreferredRowFieldIndexes(sourceRange, dataFieldIndex))
            {
                AddLayoutIfDistinct(
                    layouts,
                    CreateTitle(sheet, sourceRange, rowFieldIndex, dataFieldIndex),
                    rowFieldIndex,
                    dataFieldIndex);

                if (layouts.Count >= 3)
                    return layouts;
            }
        }

        return layouts;
    }

    private static bool IsValidExplicitSourceRange(GridRange range) =>
        range.RowCount >= 2 && range.ColCount >= 2;

    private static IReadOnlyList<int> GetPreferredDataFieldIndexes(Sheet sheet, GridRange sourceRange)
    {
        var indexes = new List<int>();
        for (var col = sourceRange.Start.Col; col <= sourceRange.End.Col; col++)
        {
            var index = checked((int)(col - sourceRange.Start.Col));
            if (ColumnHasNumericData(sheet, sourceRange, col))
                indexes.Add(index);
        }

        if (indexes.Count == 0)
            indexes.Add(ChooseDefaultDataField(sheet, sourceRange));

        return indexes;
    }

    private static IEnumerable<int> GetPreferredRowFieldIndexes(GridRange sourceRange, int dataFieldIndex)
    {
        for (var index = 0; index < sourceRange.ColCount; index++)
        {
            if (index != dataFieldIndex)
                yield return index;
        }
    }

    private static bool ColumnHasNumericData(Sheet sheet, GridRange sourceRange, uint col)
    {
        for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
        {
            if (sheet.GetValue(row, col) is NumberValue or DateTimeValue)
                return true;
        }

        return false;
    }

    private static void AddLayoutIfDistinct(
        List<RecommendedPivotTableLayout> layouts,
        string title,
        int rowFieldIndex,
        int dataFieldIndex)
    {
        if (layouts.Any(layout =>
                layout.RowFieldIndexes.SequenceEqual([rowFieldIndex]) &&
                layout.DataFieldIndexes.SequenceEqual([dataFieldIndex])))
        {
            return;
        }

        layouts.Add(new RecommendedPivotTableLayout(title, [rowFieldIndex], [dataFieldIndex]));
    }

    private static string CreateTitle(Sheet sheet, GridRange sourceRange, int rowFieldIndex, int dataFieldIndex)
    {
        var dataCol = sourceRange.Start.Col + (uint)dataFieldIndex;
        var rowCol = sourceRange.Start.Col + (uint)rowFieldIndex;
        var summary = ColumnHasNumericData(sheet, sourceRange, dataCol) ? "Sum" : "Count";
        return $"{summary} of {HeaderText(sheet.GetValue(sourceRange.Start.Row, dataCol), dataFieldIndex)} by {HeaderText(sheet.GetValue(sourceRange.Start.Row, rowCol), rowFieldIndex)}";
    }

    private static string HeaderText(ScalarValue value, int sourceFieldIndex) =>
        value switch
        {
            TextValue text when !string.IsNullOrWhiteSpace(text.Value) => text.Value.Trim(),
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            DateTimeValue date => date.ToDateTime().ToShortDateString(),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            ErrorValue error => error.Code,
            _ => $"Column {sourceFieldIndex + 1}"
        };

    private static bool PivotTableNameEquals(PivotTableModel pivotTable, string name) =>
        string.Equals(pivotTable.Name, name, StringComparison.OrdinalIgnoreCase);
}
