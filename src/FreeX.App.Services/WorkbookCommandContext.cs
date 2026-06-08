using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookCommandContext(Workbook workbook) : ICommandContext
{
    public Workbook Workbook => workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
}
