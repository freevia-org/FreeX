using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class CreateStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly string? _styleName;
    private readonly bool _firstRowHasHeaders;
    private int? _createdTableId;

    public string Label => "Create Table";
    public int? CreatedTableId => _createdTableId;

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

public sealed record StructuredTableStyleBanding(
    CellColor HeaderFill,
    CellColor OddRowFill,
    CellColor EvenRowFill,
    CellColor HeaderFontColor);

public sealed class ApplyStructuredTableStyleCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly StructuredTableStyleBanding _banding;
    private readonly string? _styleName;
    private readonly bool _updateStyleName;
    private readonly bool? _showFirstColumn;
    private readonly bool? _showLastColumn;
    private readonly bool? _showRowStripes;
    private readonly bool? _showColumnStripes;
    private readonly bool? _hasAutoFilter;
    private readonly bool? _totalsRowShown;
    private ConfigureStructuredTableStyleOptionsCommand? _configureCommand;
    private readonly List<IWorkbookCommand> _appliedStyleCommands = [];

    public string Label => "Apply Table Style";

    public ApplyStructuredTableStyleCommand(
        SheetId sheetId,
        int tableId,
        StructuredTableStyleBanding banding,
        string? styleName = null,
        bool updateStyleName = false,
        bool? showFirstColumn = null,
        bool? showLastColumn = null,
        bool? showRowStripes = null,
        bool? showColumnStripes = null,
        bool? hasAutoFilter = null,
        bool? totalsRowShown = null)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _banding = banding;
        _styleName = styleName;
        _updateStyleName = updateStyleName;
        _showFirstColumn = showFirstColumn;
        _showLastColumn = showLastColumn;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _hasAutoFilter = hasAutoFilter;
        _totalsRowShown = totalsRowShown;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _appliedStyleCommands.Clear();
        _configureCommand = null;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var table = sheet.StructuredTables.FirstOrDefault(candidate => candidate.Id == _tableId);
        if (table is null)
            return new CommandOutcome(false, "Table was not found.");

        var showFirstColumn = _showFirstColumn ?? table.ShowFirstColumn;
        var showLastColumn = _showLastColumn ?? table.ShowLastColumn;
        var showRowStripes = _showRowStripes ?? table.ShowRowStripes;
        var showColumnStripes = _showColumnStripes ?? table.ShowColumnStripes;
        _configureCommand = new ConfigureStructuredTableStyleOptionsCommand(
            _sheetId,
            _tableId,
            showFirstColumn,
            showLastColumn,
            showRowStripes,
            showColumnStripes,
            _styleName,
            _updateStyleName,
            _hasAutoFilter,
            _totalsRowShown);

        var configureOutcome = _configureCommand.Apply(ctx);
        if (!configureOutcome.Success)
            return configureOutcome;

        table = sheet.StructuredTables.First(candidate => candidate.Id == _tableId);
        foreach (var styleCommand in BuildStyleCommands(table))
        {
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

    private IEnumerable<IWorkbookCommand> BuildStyleCommands(StructuredTableModel table)
    {
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var hasTotalsRow = table.TotalsRowShown;
        var dataStartRow = table.Range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var dataEndRow = table.Range.End.Row - (hasTotalsRow && table.Range.End.Row > table.Range.Start.Row ? 1u : 0u);

        if (hasHeaderRow)
        {
            yield return CreateRangeStyleCommand(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.Start.Row,
                table.Range.End.Col,
                new StyleDiff(FillColor: _banding.HeaderFill, FontColor: _banding.HeaderFontColor, Bold: true));
        }

        if (dataStartRow <= dataEndRow)
        {
            for (var row = dataStartRow; row <= dataEndRow; row++)
            {
                var rowOffset = row - dataStartRow;
                var fill = table.ShowRowStripes
                    ? rowOffset % 2 == 0 ? _banding.EvenRowFill : _banding.OddRowFill
                    : CellColor.White;
                yield return CreateRangeStyleCommand(
                    row,
                    table.Range.Start.Col,
                    row,
                    table.Range.End.Col,
                    new StyleDiff(FillColor: fill, FontColor: CellColor.Black, Bold: false));
            }

            if (table.ShowColumnStripes)
            {
                for (var col = table.Range.Start.Col; col <= table.Range.End.Col; col++)
                {
                    var colOffset = col - table.Range.Start.Col;
                    var fill = colOffset % 2 == 0 ? _banding.EvenRowFill : _banding.OddRowFill;
                    yield return CreateRangeStyleCommand(
                        dataStartRow,
                        col,
                        dataEndRow,
                        col,
                        new StyleDiff(FillColor: fill));
                }
            }
        }

        if (hasTotalsRow)
        {
            yield return CreateRangeStyleCommand(
                table.Range.End.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.End.Col,
                new StyleDiff(FillColor: _banding.HeaderFill, FontColor: _banding.HeaderFontColor, Bold: true));
        }

        if (table.ShowFirstColumn)
        {
            yield return CreateRangeStyleCommand(
                table.Range.Start.Row,
                table.Range.Start.Col,
                table.Range.End.Row,
                table.Range.Start.Col,
                new StyleDiff(Bold: true));
        }

        if (table.ShowLastColumn && table.Range.End.Col != table.Range.Start.Col)
        {
            yield return CreateRangeStyleCommand(
                table.Range.Start.Row,
                table.Range.End.Col,
                table.Range.End.Row,
                table.Range.End.Col,
                new StyleDiff(Bold: true));
        }
    }

    private ApplyStyleCommand CreateRangeStyleCommand(
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        StyleDiff diff) =>
        new(
            _sheetId,
            new GridRange(
                new CellAddress(_sheetId, startRow, startCol),
                new CellAddress(_sheetId, endRow, endCol)),
            diff);

    private void RevertAppliedCommands(ICommandContext ctx)
    {
        for (var index = _appliedStyleCommands.Count - 1; index >= 0; index--)
            _appliedStyleCommands[index].Revert(ctx);
        _appliedStyleCommands.Clear();
        _configureCommand?.Revert(ctx);
        _configureCommand = null;
    }
}

public sealed class ConfigureStructuredTableStyleOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly string? _styleName;
    private readonly bool _updateStyleName;
    private readonly bool? _hasAutoFilter;
    private readonly bool? _totalsRowShown;
    private readonly bool _showFirstColumn;
    private readonly bool _showLastColumn;
    private readonly bool _showRowStripes;
    private readonly bool _showColumnStripes;
    private StructuredTableModel? _previousTable;

    public string Label => "Configure Table Style Options";

    public ConfigureStructuredTableStyleOptionsCommand(
        SheetId sheetId,
        int tableId,
        bool showFirstColumn,
        bool showLastColumn,
        bool showRowStripes,
        bool showColumnStripes,
        string? styleName = null,
        bool updateStyleName = false,
        bool? hasAutoFilter = null,
        bool? totalsRowShown = null)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _showFirstColumn = showFirstColumn;
        _showLastColumn = showLastColumn;
        _showRowStripes = showRowStripes;
        _showColumnStripes = showColumnStripes;
        _styleName = styleName;
        _updateStyleName = updateStyleName;
        _hasAutoFilter = hasAutoFilter;
        _totalsRowShown = totalsRowShown;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex < 0)
            return new CommandOutcome(false, "Table was not found.");

        _previousTable = sheet.StructuredTables[tableIndex];
        sheet.StructuredTables[tableIndex] = CopyWithStyleOptions(
            _previousTable,
            _showFirstColumn,
            _showLastColumn,
            _showRowStripes,
            _showColumnStripes,
            _styleName,
            _updateStyleName,
            _hasAutoFilter,
            _totalsRowShown);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTable is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var tableIndex = sheet.StructuredTables.FindIndex(table => table.Id == _tableId);
        if (tableIndex >= 0)
            sheet.StructuredTables[tableIndex] = _previousTable;
    }

    private static StructuredTableModel CopyWithStyleOptions(
        StructuredTableModel table,
        bool showFirstColumn,
        bool showLastColumn,
        bool showRowStripes,
        bool showColumnStripes,
        string? styleName,
        bool updateStyleName,
        bool? hasAutoFilter,
        bool? totalsRowShown)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = table.Range,
            HasAutoFilter = hasAutoFilter ?? table.HasAutoFilter,
            TotalsRowShown = totalsRowShown ?? table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = updateStyleName ? styleName : table.StyleName,
            ShowFirstColumn = showFirstColumn,
            ShowLastColumn = showLastColumn,
            ShowRowStripes = showRowStripes,
            ShowColumnStripes = showColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes,
            NativeChildXmls = table.NativeChildXmls,
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
        };

        copy.Columns.AddRange(table.Columns);
        copy.FilterColumns.AddRange(table.FilterColumns);
        return copy;
    }
}

public sealed class CreateStyledStructuredTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly string? _styleName;
    private readonly bool _firstRowHasHeaders;
    private readonly StructuredTableStyleBanding _banding;
    private ApplyStructuredTableStyleCommand? _applyStyleCommand;
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
        _applyStyleCommand = null;
        _createTableCommand = new CreateStructuredTableCommand(_sheetId, _range, _styleName, _firstRowHasHeaders);
        var createOutcome = _createTableCommand.Apply(ctx);
        if (!createOutcome.Success)
            return createOutcome;

        if (_createTableCommand.CreatedTableId is not { } tableId)
            return new CommandOutcome(false, "Table was not created.");

        _applyStyleCommand = new ApplyStructuredTableStyleCommand(_sheetId, tableId, _banding);
        var styleOutcome = _applyStyleCommand.Apply(ctx);
        if (styleOutcome.Success)
            return styleOutcome;

        RevertAppliedCommands(ctx);
        return styleOutcome;
    }

    public void Revert(ICommandContext ctx) => RevertAppliedCommands(ctx);

    private void RevertAppliedCommands(ICommandContext ctx)
    {
        _applyStyleCommand?.Revert(ctx);
        _applyStyleCommand = null;
        _createTableCommand?.Revert(ctx);
    }
}
