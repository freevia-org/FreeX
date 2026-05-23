using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class PasteColumnWidthsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly uint _destinationStartCol;
    private Dictionary<uint, double>? _previousWidths;

    public string Label => "Paste Column Widths";

    public PasteColumnWidthsCommand(SheetId sheetId, GridRange sourceRange, uint destinationStartCol)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destinationStartCol = destinationStartCol;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet)
            return new CommandOutcome(false, "Source range must be on one sheet.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var destinationEndCol = _destinationStartCol + _sourceRange.ColCount - 1;
        _previousWidths = new Dictionary<uint, double>();
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
        {
            if (targetSheet.ColumnWidths.TryGetValue(col, out var width))
                _previousWidths[col] = width;
        }

        for (uint offset = 0; offset < _sourceRange.ColCount; offset++)
        {
            var sourceCol = _sourceRange.Start.Col + offset;
            var destinationCol = _destinationStartCol + offset;
            if (sourceSheet.ColumnWidths.TryGetValue(sourceCol, out var width))
                targetSheet.ColumnWidths[destinationCol] = width;
            else
                targetSheet.ColumnWidths.Remove(destinationCol);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousWidths is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var destinationEndCol = _destinationStartCol + _sourceRange.ColCount - 1;
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
            sheet.ColumnWidths.Remove(col);
        foreach (var (col, width) in _previousWidths)
            sheet.ColumnWidths[col] = width;
    }
}
