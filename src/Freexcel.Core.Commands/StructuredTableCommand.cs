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
            var tableColumnIndex = table.Columns.FindIndex(column => column.Id == filterColumn.ColumnId);
            if (tableColumnIndex < 0)
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

        return new StyleDiff(FillColor: row % 2 == 0 ? _banding.EvenRowFill : _banding.OddRowFill);
    }

    private void RevertAppliedCommands(ICommandContext ctx)
    {
        for (var index = _appliedStyleCommands.Count - 1; index >= 0; index--)
            _appliedStyleCommands[index].Revert(ctx);
        _appliedStyleCommands.Clear();
        _createTableCommand?.Revert(ctx);
    }
}
