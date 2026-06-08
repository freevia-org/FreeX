using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionForecastSheetTests
{
    [Fact]
    public void ExecuteForecastSheetPlan_CreatesForecastSheetSelectsOutputAndMarksDirty()
    {
        var (session, workbook, plan) = CreateForecastSheetSession();

        var result = session.ExecuteForecastSheetPlan(plan);

        result.Success.Should().BeTrue();
        workbook.SheetCount.Should().Be(2);

        var forecast = workbook.GetSheetAt(1);
        forecast.Name.Should().Be("Forecast");
        AssertForecastData(forecast);

        var outputRange = Range(forecast.Id, 1, 1, 6, 5);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveSheet.Id.Should().Be(forecast.Id);
        session.ActiveCell.Should().Be(outputRange.Start);
        session.SelectedRange.Should().Be(outputRange);
    }

    [Fact]
    public void ExecuteForecastSheetPlan_UndoRedoRemovesAndRecreatesForecastSheet()
    {
        var (session, workbook, plan) = CreateForecastSheetSession();

        session.ExecuteForecastSheetPlan(plan).Success.Should().BeTrue();
        var forecastSheetId = workbook.GetSheetAt(1).Id;

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        workbook.SheetCount.Should().Be(1);
        workbook.GetSheet(forecastSheetId).Should().BeNull();
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        workbook.SheetCount.Should().Be(2);
        var recreatedForecast = workbook.GetSheetAt(1);
        recreatedForecast.Name.Should().Be("Forecast");
        AssertForecastData(recreatedForecast);
        session.ActiveSheet.Id.Should().Be(recreatedForecast.Id);
    }

    [Fact]
    public void ExecuteForecastSheetPlan_FailedCommandDoesNotDirtyOrMoveSelection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sales");
        workbook.ActiveSheetIndex = 0;
        var selected = Address(sheet, 6, 6);
        var session = CreateSession(workbook);
        session.SelectCell(selected);
        var staleSourceRange = Range(SheetId.New(), 1, 1, 4, 2);
        var plan = new ForecastSheetPlan(
            ForecastSheetWorkflowState.Ready,
            ForecastSheetPlanStatus.Ready,
            "Ready",
            staleSourceRange,
            InputExpectation: null,
            ForecastPeriods: 2);

        var result = session.ExecuteForecastSheetPlan(plan);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("source range");
        workbook.SheetCount.Should().Be(1);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(new GridRange(selected, selected));
    }

    private static (WorkbookSession Session, Workbook Workbook, ForecastSheetPlan Plan) CreateForecastSheetSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sales");
        workbook.ActiveSheetIndex = 0;
        SeedForecastSource(sheet);
        var plan = ForecastSheetPlanner.CreatePlan(
            workbook,
            Range(sheet.Id, 1, 1, 4, 2),
            forecastPeriods: 2);
        plan.IsReady.Should().BeTrue();

        return (CreateSession(workbook), workbook, plan);
    }

    private static void AssertForecastData(Sheet forecast)
    {
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
        forecast.Charts.Should().HaveCount(1);
        forecast.Charts[0].DataRange.Should().Be(Range(forecast.Id, 1, 1, 6, 5));
    }

    private static void SeedForecastSource(Sheet sheet)
    {
        Set(sheet, 1, 1, "Month");
        Set(sheet, 1, 2, "Revenue");
        Set(sheet, 2, 1, 1);
        Set(sheet, 2, 2, 10);
        Set(sheet, 3, 1, 2);
        Set(sheet, 3, 2, 20);
        Set(sheet, 4, 1, 3);
        Set(sheet, 4, 2, 30);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(Address(sheet, row, col), new TextValue(text));

    private static void Set(Sheet sheet, uint row, uint col, double number) =>
        sheet.SetCell(Address(sheet, row, col), new NumberValue(number));
}
