using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Merges a rectangular range into a single cell region.</summary>
public sealed class MergeCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Merge Cells";

    public MergeCellsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range   = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        foreach (var existing in sheet.MergedRegions)
        {
            if (Overlaps(_range, existing))
                return new CommandOutcome(false, "Range overlaps an existing merged region.");
        }

        _snapshot = [];
        foreach (var addr in _range.AllCells())
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));

        var topLeft = _range.Start;
        foreach (var addr in _range.AllCells())
        {
            if (addr == topLeft) continue;
            sheet.ClearCell(addr);
        }

        sheet.AddMergedRegion(_range);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);

        sheet.RemoveMergedRegion(_range);

        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(addr);
            else
                sheet.SetCell(addr, oldCell.Clone());
        }
    }

    private static bool Overlaps(GridRange a, GridRange b) =>
        a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row &&
        a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
}

/// <summary>Removes a merged cell region (makes cells independent again).</summary>
public sealed class UnmergeCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private bool _removed;

    public string Label => "Unmerge Cells";

    public UnmergeCellsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range   = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        _removed = sheet.RemoveMergedRegion(_range);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_removed)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        if (!sheet.MergedRegions.Contains(_range))
            sheet.AddMergedRegion(_range);
        _removed = false;
    }
}
