using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ForecastSheetPlannerTests
{
    [Fact]
    public void CreatePlan_BuildsReadyPlanWithTimelineAndValueExpectations()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Data");
        var sourceRange = Range(sheet.Id, "A1:B4");

        var plan = ForecastSheetPlanner.CreatePlan(workbook, sourceRange, 2);

        plan.IsReady.Should().BeTrue();
        plan.IsDeferred.Should().BeFalse();
        plan.WorkflowState.Should().Be(ForecastSheetWorkflowState.Ready);
        plan.Status.Should().Be(ForecastSheetPlanStatus.Ready);
        plan.StatusText.Should().Be("Ready to create Forecast Sheet from A1:B4 with 3 historical data rows and 2 forecast periods.");
        plan.SourceRange.Should().Be(sourceRange);
        plan.ForecastPeriods.Should().Be(2);
        plan.InvalidText.Should().BeEmpty();

        plan.InputExpectation.Should().NotBeNull();
        var input = plan.InputExpectation!;
        input.SourceRange.Should().Be(sourceRange);
        input.TimelineHeaderCell.Should().Be(Address(sheet.Id, "A1"));
        input.ValueHeaderCell.Should().Be(Address(sheet.Id, "B1"));
        input.TimelineRange.Should().Be(Range(sheet.Id, "A1:A4"));
        input.ValueRange.Should().Be(Range(sheet.Id, "B1:B4"));
        input.TimelineDataRange.Should().Be(Range(sheet.Id, "A2:A4"));
        input.ValueDataRange.Should().Be(Range(sheet.Id, "B2:B4"));
        input.HistoricalDataRowCount.Should().Be(3);

        plan.TryCreateCommand().Should().BeOfType<ForecastSheetCommand>().Which.Label.Should().Be("Forecast Sheet");
    }

    [Theory]
    [InlineData(" 12 ", 12u)]
    [InlineData("1", 1u)]
    public void CreatePlan_ParsesPositiveForecastPeriodsText(string input, uint expectedPeriods)
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Data");

        var plan = ForecastSheetPlanner.CreatePlan(workbook, Range(sheet.Id, "C5:D7"), input);

        plan.IsReady.Should().BeTrue();
        plan.ForecastPeriods.Should().Be(expectedPeriods);
    }

    [Theory]
    [InlineData(null, ForecastSheetPlanStatus.NoSelection, "")]
    [InlineData("A1:A3", ForecastSheetPlanStatus.SourceRangeRequiresTwoColumns, "A1:A3")]
    [InlineData("A1:C3", ForecastSheetPlanStatus.SourceRangeRequiresTwoColumns, "A1:C3")]
    [InlineData("A1:B2", ForecastSheetPlanStatus.SourceRangeRequiresHeaderAndTwoDataRows, "A1:B2")]
    public void CreatePlan_DefersWhenSelectionIsMissingOrNotForecastSheetShaped(
        string? rangeText,
        ForecastSheetPlanStatus expectedStatus,
        string expectedRangeText)
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Data");
        GridRange? sourceRange = rangeText is null ? null : Range(sheet.Id, rangeText);

        var plan = ForecastSheetPlanner.CreatePlan(workbook, sourceRange, 3);

        plan.IsReady.Should().BeFalse();
        plan.IsDeferred.Should().BeTrue();
        plan.WorkflowState.Should().Be(ForecastSheetWorkflowState.Deferred);
        plan.Status.Should().Be(expectedStatus);
        plan.InputExpectation.Should().BeNull();
        plan.TryCreateCommand().Should().BeNull();
        plan.SourceRange?.ToString().Should().Be(string.IsNullOrEmpty(expectedRangeText) ? null : expectedRangeText);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("0", "0")]
    [InlineData("-1", "-1")]
    [InlineData("1.5", "1.5")]
    [InlineData("three", "three")]
    public void CreatePlan_DefersInvalidForecastPeriodsButKeepsValidRangeExpectations(
        string periodsText,
        string expectedInvalidText)
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Data");
        var sourceRange = Range(sheet.Id, "A1:B3");

        var plan = ForecastSheetPlanner.CreatePlan(workbook, sourceRange, periodsText);

        plan.IsReady.Should().BeFalse();
        plan.IsDeferred.Should().BeTrue();
        plan.Status.Should().Be(ForecastSheetPlanStatus.InvalidForecastPeriods);
        plan.InvalidText.Should().Be(expectedInvalidText);
        plan.SourceRange.Should().Be(sourceRange);
        plan.InputExpectation.Should().NotBeNull();
        plan.InputExpectation!.HistoricalDataRowCount.Should().Be(2);
        plan.TryCreateCommand().Should().BeNull();
    }

    [Fact]
    public void CreatePlan_DefersWhenSourceRangeDoesNotBelongToWorkbook()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Data");
        var sourceRange = Range(SheetId.New(), "A1:B3");

        var plan = ForecastSheetPlanner.CreatePlan(workbook, sourceRange, 3);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(ForecastSheetPlanStatus.SourceRangeOutsideWorkbook);
        plan.SourceRange.Should().Be(sourceRange);
        plan.InputExpectation.Should().BeNull();
    }

    [Fact]
    public void CreatePlan_DefersWhenWorkbookStructureIsProtected()
    {
        var workbook = new Workbook { IsStructureProtected = true };
        var sheet = workbook.AddSheet("Data");
        var sourceRange = Range(sheet.Id, "A1:B3");

        var plan = ForecastSheetPlanner.CreatePlan(workbook, sourceRange, 3);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(ForecastSheetPlanStatus.WorkbookStructureProtected);
        plan.SourceRange.Should().Be(sourceRange);
        plan.InputExpectation.Should().BeNull();
    }

    [Fact]
    public void CreatePlan_DefersWhenWorkbookIsMissing()
    {
        var sourceRange = Range(SheetId.New(), "A1:B3");

        var plan = ForecastSheetPlanner.CreatePlan(null, sourceRange, 3);

        plan.IsReady.Should().BeFalse();
        plan.Status.Should().Be(ForecastSheetPlanStatus.NoWorkbook);
        plan.SourceRange.Should().BeNull();
        plan.InputExpectation.Should().BeNull();
    }

    private static CellAddress Address(SheetId sheetId, string address) =>
        CellAddress.Parse(address, sheetId);

    private static GridRange Range(SheetId sheetId, string range) =>
        GridRange.Parse(range, sheetId);
}
