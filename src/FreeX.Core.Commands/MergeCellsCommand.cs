using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Merges a rectangular range into a single cell region.</summary>
public sealed class MergeCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;
    private List<GridRange>? _absorbedRegions;

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

        // Real Excel allows merging a range that fully CONTAINS one or more smaller existing merged
        // regions: the smaller region(s) are silently absorbed (un-merged) and replaced by the single
        // new merge over the full selection. Only a genuinely PARTIAL overlap -- the new range
        // straddles an existing region's boundary without fully containing it -- is a real conflict
        // and still gets rejected, matching Excel's "That would remove merged cells..." refusal.
        var absorbed = new List<GridRange>();
        foreach (var existing in sheet.MergedRegions)
        {
            if (!Overlaps(_range, existing))
                continue;

            if (_range.Contains(existing))
            {
                absorbed.Add(existing);
                continue;
            }

            return new CommandOutcome(false, "Range overlaps an existing merged region.");
        }

        foreach (var table in sheet.StructuredTables)
        {
            if (Overlaps(_range, table.Range))
                return new CommandOutcome(false, "Cannot merge cells that overlap a table.");
        }

        _snapshot = [];
        foreach (var addr in _range.AllCells())
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));

        foreach (var region in absorbed)
            sheet.RemoveMergedRegion(region);
        _absorbedRegions = absorbed;

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

        if (_absorbedRegions is not null)
        {
            foreach (var region in _absorbedRegions)
                sheet.AddMergedRegion(region);
        }

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
