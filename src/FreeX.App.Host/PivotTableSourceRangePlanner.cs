using FreeX.Core.Model;

namespace FreeX.App.Host;

internal enum PivotTableSourceRangeError
{
    None,
    MissingSource,
    MinimumShape,
    MissingHeaders
}

internal sealed record PivotTableSourceRangePlan(GridRange? SourceRange, PivotTableSourceRangeError Error)
{
    public bool IsValid => Error == PivotTableSourceRangeError.None && SourceRange is not null;
}

internal static class PivotTableSourceRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange) =>
        CreatePlan(sheet, selectedRange) is { IsValid: true, SourceRange: { } sourceRange }
            ? sourceRange
            : selectedRange;

    public static PivotTableSourceRangePlan CreatePlan(Sheet? sheet, GridRange? selectedRange)
    {
        if (sheet is null || selectedRange is null)
            return new PivotTableSourceRangePlan(null, PivotTableSourceRangeError.MissingSource);

        var sourceRange = IsValidExplicitPivotRange(selectedRange.Value)
            ? selectedRange.Value
            : CreateTableSourceRangePlanner.ExpandToCurrentRegion(sheet, selectedRange.Value);

        if (sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
            return new PivotTableSourceRangePlan(sourceRange, PivotTableSourceRangeError.MinimumShape);

        if (!CreateTableSourceRangePlanner.HasCompleteHeaderRow(sheet, sourceRange))
            return new PivotTableSourceRangePlan(sourceRange, PivotTableSourceRangeError.MissingHeaders);

        return new PivotTableSourceRangePlan(sourceRange, PivotTableSourceRangeError.None);
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

    private static bool IsValidExplicitPivotRange(GridRange range) =>
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
            indexes.Add(PivotUiPlanner.ChooseDefaultDataField(sheet, sourceRange));

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
        return $"{summary} of {HeaderText(sheet, sourceRange, dataCol, dataFieldIndex)} by {HeaderText(sheet, sourceRange, rowCol, rowFieldIndex)}";
    }

    private static string HeaderText(Sheet sheet, GridRange sourceRange, uint col, int sourceFieldIndex)
    {
        var value = sheet.GetValue(sourceRange.Start.Row, col);
        return value switch
        {
            TextValue text when !string.IsNullOrWhiteSpace(text.Value) => text.Value.Trim(),
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            DateTimeValue date => date.ToDateTime().ToShortDateString(),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            ErrorValue error => error.Code,
            _ => $"Column {sourceFieldIndex + 1}"
        };
    }
}

internal sealed record RecommendedPivotTableLayout(
    string Title,
    IReadOnlyList<int> RowFieldIndexes,
    IReadOnlyList<int> DataFieldIndexes);
