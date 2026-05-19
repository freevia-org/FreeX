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
        (endMode || (modifiers & ModifierKeys.Control) != 0);

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
        var maxRow = 1u;
        var maxCol = 1u;
        if (sheet is not null)
        {
            foreach (var (address, _) in sheet.GetUsedCells())
            {
                if (address.Row > maxRow)
                    maxRow = address.Row;
                if (address.Col > maxCol)
                    maxCol = address.Col;
            }
        }

        return new CellAddress(sheetId, maxRow, maxCol);
    }

    private static bool CellHasData(Sheet? sheet, uint row, uint col)
    {
        if (sheet is null)
            return false;

        var value = sheet.GetValue(new CellAddress(sheet.Id, row, col));
        return value is not null and not BlankValue;
    }
}
