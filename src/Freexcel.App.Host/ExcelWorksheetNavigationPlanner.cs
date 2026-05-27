using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ExcelWorksheetNavigationPlanner
{
    public static bool TryToggleEndMode(Key key, ModifierKeys modifiers, bool current, out bool next)
    {
        next = current;
        if (key != Key.End || modifiers != ModifierKeys.None)
            return false;

        next = !current;
        return true;
    }

    public static bool ShouldUseDataBoundary(Key key, ModifierKeys modifiers, bool endMode) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right &&
        (endMode
            ? modifiers is ModifierKeys.None or ModifierKeys.Shift
            : modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift));

    public static bool ShouldHandleWorksheetNavigationKey(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        bool endMode)
    {
        var effectiveKey = key == Key.None || key == Key.System ? systemKey : key;
        return effectiveKey switch
        {
            Key.Up or Key.Down or Key.Left or Key.Right =>
                endMode
                    ? modifiers is ModifierKeys.None or ModifierKeys.Shift
                    : modifiers is ModifierKeys.None or ModifierKeys.Shift or ModifierKeys.Control or
                        (ModifierKeys.Control | ModifierKeys.Shift),
            Key.Home =>
                modifiers is ModifierKeys.None or ModifierKeys.Shift or ModifierKeys.Control or
                    (ModifierKeys.Control | ModifierKeys.Shift),
            Key.End =>
                modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift),
            Key.PageUp or Key.PageDown =>
                modifiers is ModifierKeys.None or ModifierKeys.Shift or ModifierKeys.Alt or
                    (ModifierKeys.Alt | ModifierKeys.Shift),
            Key.Enter or Key.Tab =>
                modifiers is ModifierKeys.None or ModifierKeys.Shift,
            _ => false
        };
    }

    public static CellAddress? GetHorizontalPageTarget(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        CellAddress current,
        int pageSize)
    {
        if (modifiers is not ModifierKeys.Alt and not (ModifierKeys.Alt | ModifierKeys.Shift))
            return null;

        var effectiveKey = key == Key.None ? systemKey : key;
        return effectiveKey switch
        {
            Key.PageDown => new CellAddress(
                current.Sheet,
                current.Row,
                Math.Min(current.Col + (uint)Math.Max(1, pageSize), CellAddress.MaxCol)),
            Key.PageUp => new CellAddress(
                current.Sheet,
                current.Row,
                (uint)Math.Max(1, (int)current.Col - Math.Max(1, pageSize))),
            _ => null
        };
    }

    public static CellAddress FindVerticalDataBoundary(Sheet? sheet, CellAddress current, int rowDirection)
    {
        var startFull = CellHasData(sheet, current.Row, current.Col);
        if (!startFull)
            return FindVerticalDataBoundaryFromBlank(sheet, current, rowDirection);

        var row = current.Row;
        while (true)
        {
            var next = (long)row + rowDirection;
            if (next is < 1 or > CellAddress.MaxRow)
                break;

            var nextRow = (uint)next;
            var nextFull = CellHasData(sheet, nextRow, current.Col);
            if (startFull && !nextFull)
                break;

            row = nextRow;
            if (!startFull && nextFull)
                break;
        }

        return new CellAddress(current.Sheet, row, current.Col);
    }

    public static CellAddress FindHorizontalDataBoundary(Sheet? sheet, CellAddress current, int columnDirection)
    {
        var startFull = CellHasData(sheet, current.Row, current.Col);
        if (!startFull)
            return FindHorizontalDataBoundaryFromBlank(sheet, current, columnDirection);

        var column = current.Col;
        while (true)
        {
            var next = (long)column + columnDirection;
            if (next is < 1 or > CellAddress.MaxCol)
                break;

            var nextColumn = (uint)next;
            var nextFull = CellHasData(sheet, current.Row, nextColumn);
            if (startFull && !nextFull)
                break;

            column = nextColumn;
            if (!startFull && nextFull)
                break;
        }

        return new CellAddress(current.Sheet, current.Row, column);
    }

    public static CellAddress GetCtrlEndCell(Sheet? sheet, SheetId sheetId)
    {
        var usedRangeEnd = sheet?.GetUsedRange()?.End;
        return usedRangeEnd ?? new CellAddress(sheetId, 1, 1);
    }

    private static bool CellHasData(Sheet? sheet, uint row, uint col)
    {
        if (sheet is null)
            return false;

        var value = sheet.GetValue(new CellAddress(sheet.Id, row, col));
        return value is not null and not BlankValue;
    }

    private static CellAddress FindVerticalDataBoundaryFromBlank(Sheet? sheet, CellAddress current, int rowDirection)
    {
        if (sheet is null)
            return new CellAddress(
                current.Sheet,
                rowDirection > 0 ? CellAddress.MaxRow : 1,
                current.Col);

        uint? targetRow = null;
        foreach (var address in sheet.EnumerateValueBearingCells())
        {
            if (address.Col != current.Col)
                continue;

            if (rowDirection > 0)
            {
                if (address.Row <= current.Row)
                    continue;

                if (targetRow is null || address.Row < targetRow.Value)
                    targetRow = address.Row;
            }
            else
            {
                if (address.Row >= current.Row)
                    continue;

                if (targetRow is null || address.Row > targetRow.Value)
                    targetRow = address.Row;
            }
        }

        return new CellAddress(
            current.Sheet,
            targetRow ?? (rowDirection > 0 ? CellAddress.MaxRow : 1),
            current.Col);
    }

    private static CellAddress FindHorizontalDataBoundaryFromBlank(Sheet? sheet, CellAddress current, int columnDirection)
    {
        if (sheet is null)
            return new CellAddress(
                current.Sheet,
                current.Row,
                columnDirection > 0 ? CellAddress.MaxCol : 1);

        uint? targetColumn = null;
        foreach (var address in sheet.EnumerateValueBearingCells())
        {
            if (address.Row != current.Row)
                continue;

            if (columnDirection > 0)
            {
                if (address.Col <= current.Col)
                    continue;

                if (targetColumn is null || address.Col < targetColumn.Value)
                    targetColumn = address.Col;
            }
            else
            {
                if (address.Col >= current.Col)
                    continue;

                if (targetColumn is null || address.Col > targetColumn.Value)
                    targetColumn = address.Col;
            }
        }

        return new CellAddress(
            current.Sheet,
            current.Row,
            targetColumn ?? (columnDirection > 0 ? CellAddress.MaxCol : 1));
    }
}
