using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class PasteCommentsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly bool _transpose;
    private Dictionary<CellAddress, string?>? _previous;
    private Dictionary<CellAddress, ThreadedComment?>? _previousThreaded;

    public string Label => "Paste Comments";

    public PasteCommentsCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste comments source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        var sourceComments = _sourceRange.AllCells()
            .Where(sourceSheet.Comments.ContainsKey)
            .Select(address => (Address: address, Comment: sourceSheet.Comments[address]))
            .ToList();
        var sourceThreadedComments = _sourceRange.AllCells()
            .Where(sourceSheet.ThreadedComments.ContainsKey)
            .Select(address => (Address: address, Comment: sourceSheet.ThreadedComments[address]))
            .ToList();
        _previous = [];
        _previousThreaded = [];
        var affected = new List<CellAddress>();
        foreach (var (source, comment) in sourceComments)
        {
            var destination = MapDestination(source, _sourceRange, _destination, _transpose);
            _previous[destination] = targetSheet.Comments.TryGetValue(destination, out var oldComment)
                ? oldComment
                : null;
            targetSheet.Comments[destination] = comment;
            affected.Add(destination);
        }

        foreach (var (source, comment) in sourceThreadedComments)
        {
            var destination = MapDestination(source, _sourceRange, _destination, _transpose);
            _previousThreaded[destination] = targetSheet.ThreadedComments.TryGetValue(destination, out var oldComment)
                ? oldComment
                : null;
            targetSheet.ThreadedComments[destination] = comment;
            affected.Add(destination);
        }

        return new CommandOutcome(true, AffectedCells: affected.Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previous is null || _previousThreaded is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, comment) in _previous)
        {
            if (comment is null)
                sheet.Comments.Remove(address);
            else
                sheet.Comments[address] = comment;
        }

        foreach (var (address, comment) in _previousThreaded)
        {
            if (comment is null)
                sheet.ThreadedComments.Remove(address);
            else
                sheet.ThreadedComments[address] = comment;
        }
    }

    private static CellAddress MapDestination(
        CellAddress source,
        GridRange sourceRange,
        CellAddress destination,
        bool transpose)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return transpose
            ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
            : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
    }
}
