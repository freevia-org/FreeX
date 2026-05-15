using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ForecastSheetCommandTests
{
    [Fact]
    public void ForecastSheetCommand_CreatesForecastWorksheetAndUndoRemovesIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sales");
        var ctx = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var command = new ForecastSheetCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            forecastPeriods: 2);

        command.Apply(ctx).Success.Should().BeTrue();

        workbook.SheetCount.Should().Be(2);
        var forecast = workbook.GetSheetAt(1);
        forecast.Name.Should().Be("Forecast");
        forecast.GetValue(1, 1).Should().Be(new TextValue("Month"));
        forecast.GetValue(1, 2).Should().Be(new TextValue("Revenue"));
        forecast.GetValue(1, 3).Should().Be(new TextValue("Forecast"));
        forecast.GetValue(1, 4).Should().Be(new TextValue("Lower Confidence Bound"));
        forecast.GetValue(1, 5).Should().Be(new TextValue("Upper Confidence Bound"));
        forecast.GetValue(5, 1).Should().Be(new NumberValue(4));
        forecast.GetCell(5, 3)!.FormulaText.Should().Be("FORECAST.LINEAR(A5,B2:B4,A2:A4)");
        forecast.GetCell(5, 4)!.FormulaText.Should().Be("C5-CONFIDENCE.NORM(0.05,STEYX(B2:B4,A2:A4),COUNT(A2:A4))");
        forecast.GetCell(5, 5)!.FormulaText.Should().Be("C5+CONFIDENCE.NORM(0.05,STEYX(B2:B4,A2:A4),COUNT(A2:A4))");
        forecast.GetValue(6, 1).Should().Be(new NumberValue(5));
        forecast.GetCell(6, 3)!.FormulaText.Should().Be("FORECAST.LINEAR(A6,B2:B4,A2:A4)");
        forecast.GetCell(6, 4)!.FormulaText.Should().Be("C6-CONFIDENCE.NORM(0.05,STEYX(B2:B4,A2:A4),COUNT(A2:A4))");
        forecast.GetCell(6, 5)!.FormulaText.Should().Be("C6+CONFIDENCE.NORM(0.05,STEYX(B2:B4,A2:A4),COUNT(A2:A4))");

        command.Revert(ctx);

        workbook.SheetCount.Should().Be(1);
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
