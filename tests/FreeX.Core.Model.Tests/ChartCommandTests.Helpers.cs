using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{
    private static GridRange CreateChartRange(Sheet sheet) =>
        new(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
}
