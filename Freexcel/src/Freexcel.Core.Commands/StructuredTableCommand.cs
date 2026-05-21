using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class CreateStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly string? _styleName;
    private readonly bool _firstRowHasHeaders;
    private int? _createdTableId;

    public string Label => "Create Table";

    public CreateStructuredTableCommand(SheetId sheetId, GridRange range, string? styleName = null, bool firstRowHasHeaders = true)
    {
        _sheetId = sheetId;
        _range = range;
        _styleName = styleName;
        _firstRowHasHeaders = firstRowHasHeaders;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Table range must be on the target sheet.");
        if (_range.End.Row <= _range.Start.Row)
            return new CommandOutcome(false, "Table range must include a header row and at least one data row.");
        if (_range.End.Col < _range.Start.Col)
            return new CommandOutcome(false, "Table range is invalid.");

        var id = NextTableId(sheet);
        var name = NextTableName(sheet);
        var table = new StructuredTableModel
        {
            Id = id,
            Name = name,
            DisplayName = name,
            Range = _range,
            HasAutoFilter = true,
            StyleName = string.IsNullOrWhiteSpace(_styleName) ? null : _styleName,
            ShowRowStripes = true
        };

        foreach (var column in BuildColumns(sheet, _range, _firstRowHasHeaders))
            table.Columns.Add(column);

        sheet.StructuredTables.Add(table);
        _createdTableId = id;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_createdTableId is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.StructuredTables.RemoveAll(table => table.Id == _createdTableId.Value);
    }

    private static int NextTableId(Sheet sheet) =>
        sheet.StructuredTables.Count == 0 ? 1 : sheet.StructuredTables.Max(table => table.Id) + 1;

    private static string NextTableName(Sheet sheet)
    {
        for (var index = 1; index <= 10000; index++)
        {
            var name = $"Table{index.ToString(CultureInfo.InvariantCulture)}";
            if (sheet.StructuredTables.All(table =>
                    !string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(table.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }

        return $"Table{Guid.NewGuid():N}"[..31];
    }

    private static IEnumerable<StructuredTableColumnModel> BuildColumns(Sheet sheet, GridRange range, bool firstRowHasHeaders)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;
        for (var col = range.Start.Col; col <= range.End.Col; col++, ordinal++)
        {
            var rawName = firstRowHasHeaders
                ? HeaderText(sheet.GetValue(range.Start.Row, col))
                : string.Empty;
            var baseName = string.IsNullOrWhiteSpace(rawName)
                ? $"Column{ordinal.ToString(CultureInfo.InvariantCulture)}"
                : rawName.Trim();
            var name = MakeUnique(baseName, usedNames);
            usedNames.Add(name);
            yield return new StructuredTableColumnModel(ordinal, name);
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

    private static string MakeUnique(string baseName, HashSet<string> usedNames)
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

public sealed class ApplyStructuredTableFiltersCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private HashSet<uint>? _previousFilterHiddenRows;

    public string Label => "Apply Table Filter";

    public ApplyStructuredTableFiltersCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var table = sheet.StructuredTables.FirstOrDefault(candidate => candidate.Id == _tableId);
        if (table is null)
            return new CommandOutcome(false, "Table was not found.");

        var filters = BuildFilters(table).ToList();
        if (filters.Count != table.FilterColumns.Count)
            return new CommandOutcome(false, "Table filter refers to a missing column.");

        _previousFilterHiddenRows = [.. sheet.FilterHiddenRows];

        for (var row = table.Range.Start.Row + 1; row <= table.Range.End.Row; row++)
            sheet.FilterHiddenRows.Remove(row);

        if (filters.Count == 0)
            return new CommandOutcome(true);

        for (var row = table.Range.Start.Row + 1; row <= table.Range.End.Row; row++)
        {
            if (!RowMatchesAllFilters(sheet, row, filters))
                sheet.FilterHiddenRows.Add(row);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousFilterHiddenRows is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        sheet.FilterHiddenRows.Clear();
        sheet.FilterHiddenRows.UnionWith(_previousFilterHiddenRows);
    }

    private static IEnumerable<TableFilterState> BuildFilters(StructuredTableModel table)
    {
        foreach (var filterColumn in table.FilterColumns)
        {
            var tableColumnIndex = filterColumn.ColumnId;
            if (tableColumnIndex < 0 || tableColumnIndex >= table.Columns.Count)
                continue;

            yield return new TableFilterState(
                table.Range.Start.Col + (uint)tableColumnIndex,
                new HashSet<string>(filterColumn.Values, StringComparer.OrdinalIgnoreCase),
                filterColumn.IncludeBlank);
        }
    }

    private static bool RowMatchesAllFilters(Sheet sheet, uint row, IReadOnlyList<TableFilterState> filters)
    {
        foreach (var filter in filters)
        {
            var text = FilterValueFormatter.ToText(sheet.GetValue(row, filter.Column));
            if (text.Length == 0 && filter.IncludeBlank)
                continue;

            if (!filter.AllowedValues.Contains(text))
                return false;
        }

        return true;
    }

    private sealed record TableFilterState(uint Column, HashSet<string> AllowedValues, bool IncludeBlank);
}

public sealed class RefreshStructuredTableTotalsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly Dictionary<CellAddress, Cell?> _previousCells = [];

    public string Label => "Refresh Table Totals";

    public RefreshStructuredTableTotalsCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var table = sheet.StructuredTables.FirstOrDefault(candidate => candidate.Id == _tableId);
        if (table is null)
            return new CommandOutcome(false, "Table was not found.");
        if (!table.TotalsRowShown)
            return new CommandOutcome(false, "Table totals row is not shown.");
        if (table.Columns.Count == 0)
            return new CommandOutcome(false, "Table has no columns.");

        _previousCells.Clear();
        var totalsRow = table.Range.End.Row;
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var address = new CellAddress(_sheetId, totalsRow, table.Range.Start.Col + (uint)index);
            _previousCells[address] = sheet.GetCell(address.Row, address.Col)?.Clone();
            if (ResolveTotalsValue(sheet, table, table.Columns[index], index) is { } value)
                sheet.SetCell(address, value);
            else
                sheet.SetCell(address, BlankValue.Instance);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousCells.Count == 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, cell) in _previousCells)
        {
            if (cell is null)
                sheet.ClearCell(address.Row, address.Col);
            else
                sheet.SetCell(address, cell);
        }
        _previousCells.Clear();
    }

    private static ScalarValue? ResolveTotalsValue(
        Sheet sheet,
        StructuredTableModel table,
        StructuredTableColumnModel column,
        int columnIndex)
    {
        if (!string.IsNullOrWhiteSpace(column.TotalsRowLabel))
            return new TextValue(column.TotalsRowLabel);
        if (!string.IsNullOrWhiteSpace(column.TotalsRowFormula))
            return new TextValue(column.TotalsRowFormula);
        if (string.IsNullOrWhiteSpace(column.TotalsRowFunction))
            return null;

        var values = ReadColumnValues(sheet, table, columnIndex).ToList();
        var numbers = values
            .Select(TryGetNumber)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return column.TotalsRowFunction.Trim().ToLowerInvariant() switch
        {
            "sum" => new NumberValue(numbers.Sum()),
            "average" or "avg" => new NumberValue(numbers.Count == 0 ? 0 : numbers.Average()),
            "count" => new NumberValue(values.Count(IsNonBlank)),
            "countnums" or "countNums" => new NumberValue(numbers.Count),
            "min" => new NumberValue(numbers.Count == 0 ? 0 : numbers.Min()),
            "max" => new NumberValue(numbers.Count == 0 ? 0 : numbers.Max()),
            _ => null
        };
    }

    private static IEnumerable<ScalarValue> ReadColumnValues(Sheet sheet, StructuredTableModel table, int columnIndex)
    {
        var col = table.Range.Start.Col + (uint)columnIndex;
        var lastDataRow = table.TotalsRowShown ? table.Range.End.Row - 1 : table.Range.End.Row;
        for (var row = table.Range.Start.Row + 1; row <= lastDataRow; row++)
            yield return sheet.GetValue(row, col);
    }

    private static double? TryGetNumber(ScalarValue value) =>
        value switch
        {
            NumberValue number => number.Value,
            DateTimeValue date => date.Value,
            BoolValue boolean => boolean.Value ? 1 : 0,
            _ => null
        };

    private static bool IsNonBlank(ScalarValue value) => value is not BlankValue;
}

public sealed record StructuredTableStyleBanding(
    CellColor HeaderFill,
    CellColor OddRowFill,
    CellColor EvenRowFill,
    CellColor HeaderFontColor);

public sealed class CreateStyledStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly string? _styleName;
    private readonly bool _firstRowHasHeaders;
    private readonly StructuredTableStyleBanding _banding;
    private readonly List<IWorkbookCommand> _appliedStyleCommands = [];
    private CreateStructuredTableCommand? _createTableCommand;

    public string Label => "Format as Table";

    public CreateStyledStructuredTableCommand(
        SheetId sheetId,
        GridRange range,
        string? styleName,
        bool firstRowHasHeaders,
        StructuredTableStyleBanding banding)
    {
        _sheetId = sheetId;
        _range = range;
        _styleName = styleName;
        _firstRowHasHeaders = firstRowHasHeaders;
        _banding = banding;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _appliedStyleCommands.Clear();
        _createTableCommand = new CreateStructuredTableCommand(_sheetId, _range, _styleName, _firstRowHasHeaders);
        var createOutcome = _createTableCommand.Apply(ctx);
        if (!createOutcome.Success)
            return createOutcome;

        for (var row = _range.Start.Row; row <= _range.End.Row; row++)
        {
            var styleCommand = new ApplyStyleCommand(
                _sheetId,
                new GridRange(
                    new CellAddress(_sheetId, row, _range.Start.Col),
                    new CellAddress(_sheetId, row, _range.End.Col)),
                CreateRowStyleDiff(row));
            var styleOutcome = styleCommand.Apply(ctx);
            if (!styleOutcome.Success)
            {
                RevertAppliedCommands(ctx);
                return styleOutcome;
            }

            _appliedStyleCommands.Add(styleCommand);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx) => RevertAppliedCommands(ctx);

    private StyleDiff CreateRowStyleDiff(uint row)
    {
        if (row == _range.Start.Row)
        {
            return new StyleDiff(
                FillColor: _banding.HeaderFill,
                FontColor: _banding.HeaderFontColor,
                Bold: true);
        }

        return new StyleDiff(
            FillColor: row % 2 == 0 ? _banding.EvenRowFill : _banding.OddRowFill,
            FontColor: CellColor.Black,
            Bold: false);
    }

    private void RevertAppliedCommands(ICommandContext ctx)
    {
        for (var index = _appliedStyleCommands.Count - 1; index >= 0; index--)
            _appliedStyleCommands[index].Revert(ctx);
        _appliedStyleCommands.Clear();
        _createTableCommand?.Revert(ctx);
    }
}
