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

        foreach (var column in StructuredTableColumnBuilder.BuildColumns(sheet, _range, _firstRowHasHeaders))
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
