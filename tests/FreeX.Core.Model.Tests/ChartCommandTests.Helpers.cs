using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    private static GridRange CreateChartRange(Sheet sheet) =>
        new(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
