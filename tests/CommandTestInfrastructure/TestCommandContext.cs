using FreeX.Core.Commands;
using FreeX.Core.Model;

internal sealed class TestCommandContext(Workbook workbook) : ICommandContext
{
    public Workbook Workbook { get; } = workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
}
