using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RenameStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly string _newName;
    private StructuredTableModel? _previousTable;

    public string Label => "Table Name";

    public RenameStructuredTableCommand(SheetId sheetId, int tableId, string newName)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _newName = newName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousTable = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex < 0)
            return new CommandOutcome(false, "Table was not found.");

        if (StructuredTableDesignCommandHelpers.ValidateTableName(ctx.Workbook, _newName, _sheetId, _tableId) is { } error)
            return new CommandOutcome(false, error);

        var normalizedName = _newName.Trim();
        _previousTable = sheet.StructuredTables[tableIndex];
        sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
            _previousTable,
            name: normalizedName,
            displayName: normalizedName);

        return new CommandOutcome(true, AffectedCells: [_previousTable.Range.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex >= 0)
            sheet.StructuredTables[tableIndex] = _previousTable;
        _previousTable = null;
    }
}

public sealed class ResizeStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly GridRange _newRange;
    private StructuredTableModel? _previousTable;

    public string Label => "Resize Table";

    public ResizeStructuredTableCommand(SheetId sheetId, int tableId, GridRange newRange)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _newRange = newRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousTable = null;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex < 0)
            return new CommandOutcome(false, "Table was not found.");

        var table = sheet.StructuredTables[tableIndex];
        if (ValidateResizeRange(table, _newRange) is { } error)
            return new CommandOutcome(false, error);

        _previousTable = table;
        var columns = BuildColumns(sheet, table, _newRange).ToList();
        var filterColumns = table.FilterColumns
            .Where(filter => filter.ColumnId >= 0 && filter.ColumnId < columns.Count)
            .ToList();

        sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(
            table,
            range: _newRange,
            columns: columns,
            filterColumns: filterColumns);

        return new CommandOutcome(true, AffectedCells: [_newRange.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex >= 0)
            sheet.StructuredTables[tableIndex] = _previousTable;
        _previousTable = null;
    }

    private static string? ValidateResizeRange(StructuredTableModel table, GridRange range)
    {
        if (range.Start.Sheet != table.Range.Start.Sheet || range.End.Sheet != table.Range.End.Sheet)
            return "Table range must remain on the table sheet.";
        if (range.Start != table.Range.Start)
            return "Resize Table keeps the current table header cell fixed.";
        if (range.RowCount < 2)
            return "Table range must include at least two rows.";
        if (range.ColCount == 0)
            return "Table range must include at least one column.";

        return null;
    }

    private static IEnumerable<StructuredTableColumnModel> BuildColumns(
        Sheet sheet,
        StructuredTableModel table,
        GridRange range)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var targetColumnCount = checked((int)range.ColCount);

        for (var index = 0; index < targetColumnCount; index++)
        {
            var ordinal = index + 1;
            if (index < table.Columns.Count)
            {
                var existing = table.Columns[index];
                var name = string.IsNullOrWhiteSpace(existing.Name)
                    ? MakeUniqueColumnName($"Column{ordinal.ToString(CultureInfo.InvariantCulture)}", usedNames)
                    : existing.Name;
                usedNames.Add(name);
                yield return existing with { Id = ordinal, Name = name };
                continue;
            }

            var rawName = hasHeaderRow
                ? HeaderText(sheet.GetValue(range.Start.Row, range.Start.Col + (uint)index))
                : string.Empty;
            var baseName = string.IsNullOrWhiteSpace(rawName)
                ? $"Column{ordinal.ToString(CultureInfo.InvariantCulture)}"
                : rawName.Trim();
            var columnName = MakeUniqueColumnName(baseName, usedNames);
            usedNames.Add(columnName);
            yield return new StructuredTableColumnModel(ordinal, columnName);
        }
    }

    private static string HeaderText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToShortDateString(),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static string MakeUniqueColumnName(string baseName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(baseName))
            return baseName;

        for (var suffix = 2; suffix <= 10000; suffix++)
        {
            var candidate = $"{baseName}{suffix.ToString(CultureInfo.InvariantCulture)}";
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        return $"{baseName}{Guid.NewGuid():N}"[..Math.Min(31, baseName.Length + 32)];
    }
}

public sealed class ConvertStructuredTableToRangeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private StructuredTableModel? _removedTable;
    private int _removedIndex = -1;

    public string Label => "Convert to Range";

    public ConvertStructuredTableToRangeCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _removedTable = null;
        _removedIndex = -1;
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex < 0)
            return new CommandOutcome(false, "Table was not found.");

        _removedIndex = tableIndex;
        _removedTable = sheet.StructuredTables[tableIndex];
        sheet.StructuredTables.RemoveAt(tableIndex);

        return new CommandOutcome(true, AffectedCells: [_removedTable.Range.Start]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var insertIndex = _removedIndex >= 0 && _removedIndex <= sheet.StructuredTables.Count
            ? _removedIndex
            : sheet.StructuredTables.Count;
        sheet.StructuredTables.Insert(insertIndex, _removedTable);
        _removedTable = null;
        _removedIndex = -1;
    }
}

internal static class StructuredTableDesignCommandHelpers
{
    public static string? ValidateTableName(Workbook workbook, string? name, SheetId? exceptSheetId = null, int? exceptTableId = null)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
            return "Table name is invalid: it cannot be blank.";
        if (normalizedName.Length > 255)
            return "Table name is invalid: it cannot exceed 255 characters.";
        if (!IsValidTableNameStart(normalizedName[0]) || normalizedName.Any(ch => !IsValidTableNameChar(ch)))
            return "Table name is invalid: use letters, numbers, underscores, and periods; start with a letter or underscore.";
        if (CellAddress.TryParse(normalizedName, SheetId.New(), out _) || IsR1C1Reference(normalizedName))
            return "Table name is invalid: it cannot look like a cell reference.";
        if (workbook.NamedRanges.Keys.Any(existing => string.Equals(existing, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return $"A named range named '{normalizedName}' already exists.";

        foreach (var sheet in workbook.Sheets)
        foreach (var table in sheet.StructuredTables)
        {
            if (exceptSheetId == sheet.Id && exceptTableId == table.Id)
                continue;

            if (string.Equals(table.Name, normalizedName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(table.DisplayName, normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return $"A table named '{normalizedName}' already exists.";
            }
        }

        return null;
    }

    public static StructuredTableModel CopyTable(
        StructuredTableModel table,
        string? name = null,
        string? displayName = null,
        GridRange? range = null,
        IReadOnlyList<StructuredTableColumnModel>? columns = null,
        IReadOnlyList<StructuredTableFilterColumnModel>? filterColumns = null)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = name ?? table.Name,
            DisplayName = displayName ?? table.DisplayName,
            Range = range ?? table.Range,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = table.StyleName,
            ShowFirstColumn = table.ShowFirstColumn,
            ShowLastColumn = table.ShowLastColumn,
            ShowRowStripes = table.ShowRowStripes,
            ShowColumnStripes = table.ShowColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes,
            NativeChildXmls = table.NativeChildXmls,
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
        };

        copy.Columns.AddRange(columns ?? table.Columns);
        copy.FilterColumns.AddRange(filterColumns ?? table.FilterColumns);
        return copy;
    }

    private static bool IsValidTableNameStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsValidTableNameChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private static bool IsR1C1Reference(string name)
    {
        if (name.Length < 4 || char.ToUpperInvariant(name[0]) != 'R')
            return false;

        var cIndex = name.IndexOf("C", 1, StringComparison.OrdinalIgnoreCase);
        if (cIndex <= 1 || cIndex == name.Length - 1)
            return false;

        return uint.TryParse(name[1..cIndex], out var row) &&
               uint.TryParse(name[(cIndex + 1)..], out var col) &&
               row is >= 1 and <= CellAddress.MaxRow &&
               col is >= 1 and <= CellAddress.MaxCol;
    }
}
