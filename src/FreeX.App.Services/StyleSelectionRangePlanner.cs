using FreeX.Core.Model;

namespace FreeX.App.Services;

internal static class StyleSelectionRangePlanner
{
    public static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(
            new CellAddress(sheetId, range.Start.Row, range.Start.Col),
            new CellAddress(sheetId, range.End.Row, range.End.Col));
}
