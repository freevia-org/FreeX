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

        _previousRowLevels = new Dictionary<uint, int>(sheet.RowOutlineLevels);
        _previousColumnLevels = new Dictionary<uint, int>(sheet.ColOutlineLevels);
        _previousGroupHiddenRows = [.. sheet.GroupHiddenRows];
        _previousGroupHiddenColumns = [.. sheet.GroupHiddenCols];

        sheet.RowOutlineLevels.Clear();
        sheet.ColOutlineLevels.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.GroupHiddenCols.Clear();

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRowLevels is null ||
            _previousColumnLevels is null ||
            _previousGroupHiddenRows is null ||
            _previousGroupHiddenColumns is null)
        {
            return;
        }

        var sheet = ctx.GetSheet(_sheetId);
        sheet.RowOutlineLevels.Clear();
        sheet.ColOutlineLevels.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.GroupHiddenCols.Clear();

        foreach (var (row, level) in _previousRowLevels)
            sheet.RowOutlineLevels[row] = level;
        foreach (var (column, level) in _previousColumnLevels)
            sheet.ColOutlineLevels[column] = level;
        foreach (var row in _previousGroupHiddenRows)
            sheet.GroupHiddenRows.Add(row);
        foreach (var column in _previousGroupHiddenColumns)
            sheet.GroupHiddenCols.Add(column);
    }

    private static CommandOutcome? RejectProtectedOutlineClear(Sheet sheet)
    {
        if (!sheet.IsProtected)
            return null;

        var touchesRows = sheet.RowOutlineLevels.Count != 0 || sheet.GroupHiddenRows.Count != 0;
        var touchesColumns = sheet.ColOutlineLevels.Count != 0 || sheet.GroupHiddenCols.Count != 0;

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
