using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Pivot;

/// <summary>
/// UI-free glue backing the Avalonia "Insert PivotTable" dialog. Reads the source range's fields (header
/// text + whether the column is numeric), proposes a sensible default field assignment (first non-numeric
/// column as a Row field, numeric columns as Value fields), and builds the Core command that creates the
/// pivot — either on a new worksheet or at a chosen target cell. Pure (no Avalonia types) so the field
/// classification, defaults, and validation are unit testable without a running shell.
/// </summary>
internal static class PivotCreatePlanner
{
    /// <summary>A single source column the user can assign to the Row or Values area.</summary>
    internal sealed record SourceField(int Index, string Header, bool IsNumeric);

    /// <summary>How a pivot source column is used in the new pivot.</summary>
    internal enum FieldRole
    {
        Unused,
        Row,
        Value,
    }

    /// <summary>
    /// True when <paramref name="source"/> can seed a pivot: at least one column and at least two rows (a
    /// header row plus one data row), matching the Core <see cref="AddPivotTableCommand"/> guard.
    /// </summary>
    internal static bool IsValidSource(GridRange source) =>
        source.ColCount >= 1 && source.RowCount >= 2;

    /// <summary>
    /// Reads each source column as a <see cref="SourceField"/>: the header text from the first row and
    /// whether the first data row's cell is numeric (number/bool/date), used to default numeric columns to
    /// the Values area. Columns are indexed from 0 at the source range's first column.
    /// </summary>
    internal static IReadOnlyList<SourceField> ReadFields(Sheet sheet, GridRange source)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var fields = new List<SourceField>();
        var headerRow = source.Start.Row;
        var firstDataRow = source.RowCount >= 2 ? headerRow + 1 : headerRow;

        for (var col = source.Start.Col; col <= source.End.Col; col++)
        {
            var index = (int)(col - source.Start.Col);
            var header = HeaderText(sheet.GetValue(headerRow, col), index);
            var isNumeric = sheet.GetValue(firstDataRow, col) is NumberValue or BoolValue or DateTimeValue;
            fields.Add(new SourceField(index, header, isNumeric));
        }

        return fields;
    }

    /// <summary>
    /// The default role for each field: the first non-numeric column becomes a Row field (or column 0 when
    /// every column is numeric), and every numeric column becomes a Value field. When no column is numeric,
    /// the last column is used as the single Value so the result still satisfies the "needs a data field"
    /// guard.
    /// </summary>
    internal static IReadOnlyDictionary<int, FieldRole> DefaultRoles(IReadOnlyList<SourceField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var roles = new Dictionary<int, FieldRole>();
        foreach (var field in fields)
            roles[field.Index] = FieldRole.Unused;

        if (fields.Count == 0)
            return roles;

        var numeric = fields.Where(f => f.IsNumeric).ToList();
        if (numeric.Count > 0)
        {
            foreach (var f in numeric)
                roles[f.Index] = FieldRole.Value;

            var firstNonNumeric = fields.FirstOrDefault(f => !f.IsNumeric);
            roles[(firstNonNumeric ?? fields[0]).Index] = FieldRole.Row;
        }
        else
        {
            // No numeric columns: first column is the Row, last column is the (text-count) Value.
            roles[fields[0].Index] = FieldRole.Row;
            roles[fields[^1].Index] = FieldRole.Value;
        }

        return roles;
    }

    /// <summary>The Row field indexes from a role assignment, in ascending source order.</summary>
    internal static IReadOnlyList<int> RowIndexes(IReadOnlyDictionary<int, FieldRole> roles) =>
        roles.Where(kv => kv.Value == FieldRole.Row).Select(kv => kv.Key).OrderBy(i => i).ToList();

    /// <summary>The Value (data) field indexes from a role assignment, in ascending source order.</summary>
    internal static IReadOnlyList<int> ValueIndexes(IReadOnlyDictionary<int, FieldRole> roles) =>
        roles.Where(kv => kv.Value == FieldRole.Value).Select(kv => kv.Key).OrderBy(i => i).ToList();

    /// <summary>A unique pivot name across the workbook (PivotTable1, PivotTable2, …).</summary>
    internal static string SuggestName(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var existing = workbook.Sheets
            .SelectMany(s => s.PivotTables)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var n = 1; ; n++)
        {
            var candidate = $"PivotTable{n}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Builds the Core command that creates the pivot: on a new worksheet when <paramref name="target"/> is
    /// null, otherwise at the given target cell on <paramref name="targetSheetId"/>. Field indexes are the
    /// source-relative column indexes from <see cref="RowIndexes"/> / <see cref="ValueIndexes"/>.
    /// </summary>
    internal static IWorkbookCommand BuildCommand(
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
            return new AddPivotTableCommand(targetSheetId, source, targetRange, name, rowIndexes, dataIndexes);
        }

        return new AddPivotTableToNewWorksheetCommand(source, name, rowIndexes, dataIndexes);
    }

    private static string HeaderText(ScalarValue value, int index) =>
        value switch
        {
            TextValue { Value.Length: > 0 } text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => $"Column{index + 1}",
        };
}
