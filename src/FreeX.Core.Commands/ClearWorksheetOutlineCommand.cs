using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Clears row and column outline levels and restores rows/columns hidden only by outline groups.</summary>
public sealed class ClearWorksheetOutlineCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private Dictionary<uint, int>? _previousRowLevels;
    private Dictionary<uint, int>? _previousColumnLevels;
    private HashSet<uint>? _previousGroupHiddenRows;
    private HashSet<uint>? _previousGroupHiddenColumns;
    private HashSet<uint>? _previousCollapsedAnchorRows;
    private HashSet<uint>? _previousCollapsedAnchorColumns;

    public string Label => "Clear Outline";

    public ClearWorksheetOutlineCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (RejectProtectedOutlineClear(sheet) is { } protectedOutcome)
            return protectedOutcome;

        // r200: nothing to clear means nothing to undo. Pushing an entry anyway clears redo, so
        // Clear Outline on a sheet that has no outline discarded a real redo the user still had.
        if (sheet.RowOutlineLevels.Count == 0 && sheet.ColOutlineLevels.Count == 0 &&
            sheet.GroupHiddenRows.Count == 0 && sheet.GroupHiddenCols.Count == 0 &&
            sheet.CollapsedAnchorRows.Count == 0 && sheet.CollapsedAnchorCols.Count == 0)
        {
            return new CommandOutcome(true, IsNoOp: true);
        }

        _previousRowLevels = new Dictionary<uint, int>(sheet.RowOutlineLevels);
        _previousColumnLevels = new Dictionary<uint, int>(sheet.ColOutlineLevels);
        _previousGroupHiddenRows = [.. sheet.GroupHiddenRows];
        _previousGroupHiddenColumns = [.. sheet.GroupHiddenCols];
        _previousCollapsedAnchorRows = [.. sheet.CollapsedAnchorRows];
        _previousCollapsedAnchorColumns = [.. sheet.CollapsedAnchorCols];

        sheet.RowOutlineLevels.Clear();
        sheet.ColOutlineLevels.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.GroupHiddenCols.Clear();
        sheet.CollapsedAnchorRows.Clear();
        sheet.CollapsedAnchorCols.Clear();

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRowLevels is null ||
            _previousColumnLevels is null ||
            _previousGroupHiddenRows is null ||
            _previousGroupHiddenColumns is null ||
            _previousCollapsedAnchorRows is null ||
            _previousCollapsedAnchorColumns is null)
        {
            return;
        }

        var sheet = ctx.GetSheet(_sheetId);
        sheet.RowOutlineLevels.Clear();
        sheet.ColOutlineLevels.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.GroupHiddenCols.Clear();
        sheet.CollapsedAnchorRows.Clear();
        sheet.CollapsedAnchorCols.Clear();

        foreach (var (row, level) in _previousRowLevels)
            sheet.RowOutlineLevels[row] = level;
        foreach (var (column, level) in _previousColumnLevels)
            sheet.ColOutlineLevels[column] = level;
        foreach (var row in _previousGroupHiddenRows)
            sheet.GroupHiddenRows.Add(row);
        foreach (var column in _previousGroupHiddenColumns)
            sheet.GroupHiddenCols.Add(column);
        foreach (var row in _previousCollapsedAnchorRows)
            sheet.CollapsedAnchorRows.Add(row);
        foreach (var column in _previousCollapsedAnchorColumns)
            sheet.CollapsedAnchorCols.Add(column);
    }

    private static CommandOutcome? RejectProtectedOutlineClear(Sheet sheet)
    {
        if (!sheet.IsProtected)
            return null;

        var touchesRows = sheet.RowOutlineLevels.Count != 0 ||
                          sheet.GroupHiddenRows.Count != 0 ||
                          sheet.CollapsedAnchorRows.Count != 0;
        var touchesColumns = sheet.ColOutlineLevels.Count != 0 ||
                             sheet.GroupHiddenCols.Count != 0 ||
                             sheet.CollapsedAnchorCols.Count != 0;

        if (touchesRows &&
            !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.FormatRows))
        {
            return new CommandOutcome(false, "The sheet is protected.");
        }

        if (touchesColumns &&
            !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.FormatColumns))
        {
            return new CommandOutcome(false, "The sheet is protected.");
        }

        return null;
    }
}
