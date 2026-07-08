using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-13 bucket S10 fix verification (fill-handle linear-trend anchor).
/// See scratchpad r13-S10.md for the full finding text.
/// </summary>
public class FreeXR13S10Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // R13-drag-fill-series-1: for a non-collinear numeric source (1, 2, 6), Excel's fill handle
    // continues the least-squares regression line itself (intercept 0.5, slope 2.5), not a step
    // applied from the raw last sampled value (6). The old code anchored on numbers[^1] = 6, so
    // filling one cell down produced 6 + 2.5*1 = 8.5 instead of Excel's fitted-line value 8 (and
    // 10.5 / 13 for the next two cells instead of 11 / 13.5).
    [Fact]
    public void FillNumberSeries_Down_NonCollinearValues_AnchorsOnFittedRegressionLine_NotLastActualValue()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(6));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 6, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(4, 1).Should().Be(new NumberValue(8));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(10.5));
        sheet.GetValue(6, 1).Should().Be(new NumberValue(13));
    }
}
