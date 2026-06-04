using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

internal sealed class TestCommandContext(Workbook workbook) : ICommandContext
{
    public Workbook Workbook => workbook;

    public Sheet GetSheet(SheetId id) =>
        workbook.GetSheet(id) ?? throw new InvalidOperationException($"Sheet {id} not found");
}
