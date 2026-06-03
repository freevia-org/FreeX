using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ExcelEditKeyPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly CellAddress Current = new(SheetId, 10, 5);
}
