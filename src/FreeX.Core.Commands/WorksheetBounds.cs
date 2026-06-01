using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class WorksheetBounds
{
    public static bool IsValidAddress(CellAddress address) =>
        address.Row is >= 1 and <= CellAddress.MaxRow &&
        address.Col is >= 1 and <= CellAddress.MaxCol;

    public static bool TryGetRectangleEnd(
        CellAddress start,
        ulong rowCount,
        ulong colCount,
        out CellAddress end)
    {
        end = default;
        if (!IsValidAddress(start))
            return false;

        if (rowCount == 0 || colCount == 0)
        {
            end = start;
            return true;
        }

        try
        {
            var endRow = checked((ulong)start.Row + rowCount - 1UL);
            var endCol = checked((ulong)start.Col + colCount - 1UL);
            if (endRow > CellAddress.MaxRow || endCol > CellAddress.MaxCol)
                return false;

            end = new CellAddress(start.Sheet, (uint)endRow, (uint)endCol);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryOffset(
        CellAddress start,
        SheetId sheetId,
        ulong rowOffset,
        ulong colOffset,
        out CellAddress address)
    {
        address = default;
        try
        {
            var row = checked((ulong)start.Row + rowOffset);
            var col = checked((ulong)start.Col + colOffset);
            if (row > CellAddress.MaxRow || col > CellAddress.MaxCol)
                return false;

            address = new CellAddress(sheetId, (uint)row, (uint)col);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryShift(
        CellAddress source,
        SheetId targetSheetId,
        int rowDelta,
        int colDelta,
        out CellAddress address)
    {
        address = default;
        try
        {
            var row = checked((long)source.Row + rowDelta);
            var col = checked((long)source.Col + colDelta);
            if (row is < 1 or > CellAddress.MaxRow || col is < 1 or > CellAddress.MaxCol)
                return false;

            address = new CellAddress(targetSheetId, (uint)row, (uint)col);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
