using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

internal sealed class TestCommandContext(Workbook workbook) : ICommandContext
{
    public Workbook Workbook { get; } = workbook;

    public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
}
