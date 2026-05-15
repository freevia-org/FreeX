using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class AddSparklineCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SparklineModel _sparkline;
    private bool _added;

    public string Label => "Insert Sparkline";

    public AddSparklineCommand(
        SheetId sheetId,
        GridRange dataRange,
        CellAddress location,
        SparklineKind kind)
    {
        _sheetId = sheetId;
        _sparkline = new SparklineModel
        {
            DataRange = dataRange,
            Location = location,
            Kind = kind
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sparkline.DataRange.Start.Sheet != _sheetId ||
            _sparkline.DataRange.End.Sheet != _sheetId ||
            _sparkline.Location.Sheet != _sheetId)
        {
            return new CommandOutcome(false, "Sparkline data range and location must be on the target sheet.");
        }

        var sheet = ctx.GetSheet(_sheetId);
        sheet.Sparklines.Add(_sparkline);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_sparkline.Location]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).Sparklines.Remove(_sparkline);
        _added = false;
    }
}
