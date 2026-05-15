using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Removes duplicate rows in a range as one undoable command.</summary>
public sealed class RemoveDuplicateRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly List<DeleteRowsCommand> _deletes = [];

    public int RemovedRowCount { get; private set; }

    public string Label => "Remove Duplicates";

    public RemoveDuplicateRowsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _deletes.Clear();
        RemovedRowCount = 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rowsToDelete = new List<uint>();
        for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
        {
            var keyParts = new List<string>();
            for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
                keyParts.Add(sheet.GetValue(row, col).ToString() ?? string.Empty);

            if (!seen.Add(string.Join("\t", keyParts)))
                rowsToDelete.Add(row);
        }

        foreach (var row in rowsToDelete.OrderByDescending(r => r))
        {
            var delete = new DeleteRowsCommand(_sheetId, row);
            var outcome = delete.Apply(ctx);
            if (!outcome.Success)
            {
                Revert(ctx);
                return outcome;
            }

            _deletes.Add(delete);
        }

        RemovedRowCount = _deletes.Count;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        for (int i = _deletes.Count - 1; i >= 0; i--)
            _deletes[i].Revert(ctx);
    }
}
