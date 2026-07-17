using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

public sealed class FillSeriesPlannerTests
{
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData(" -2.5 ", true, -2.5)]
    [InlineData("0", true, 0)]
    [InlineData("", false, 0)]
    [InlineData("NaN", false, 0)]
    [InlineData("Infinity", false, 0)]
    [InlineData("step", false, 0)]
    public void TryParseStep_ParsesFiniteNumericStep(string input, bool expected, double expectedStep)
    {
        FillSeriesPlanner.TryParseStep(input, out var step).Should().Be(expected);
        step.Should().Be(expectedStep);
    }

    [Fact]
    public void TryParseStep_WithExplicitCulture_ParsesCultureDecimalBeforeInvariantThousands()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        FillSeriesPlanner.TryParseStep("1,5", culture, out var step).Should().BeTrue();

        step.Should().Be(1.5);
    }

    [Fact]
    public void DefaultOptions_MatchExcelSeriesDialogDefaults()
    {
        FillSeriesPlanner.DefaultOptions.Should().Be(new FillSeriesOptions(
            Step: 1,
            SeriesIn: FillSeriesDirection.Columns,
            Type: FillSeriesType.Linear,
            DateUnit: FillSeriesDateUnit.Day));

        FillSeriesPlanner.CreateDefaultOptions(2.5).Should().Be(
            FillSeriesPlanner.DefaultOptions with { Step = 2.5 });
    }

    [Theory]
    [InlineData(FillSeriesType.Linear, false)]
    [InlineData(FillSeriesType.Growth, false)]
    [InlineData(FillSeriesType.Date, true)]
    [InlineData(FillSeriesType.AutoFill, false)]
    public void IsDateUnitEnabled_OnlyEnablesDateUnitsForDateSeries(FillSeriesType type, bool expected)
    {
        FillSeriesPlanner.IsDateUnitEnabled(type).Should().Be(expected);
    }

    [Theory]
    [InlineData(FillSeriesInputError.InvalidStep, FillSeriesInputFocusTarget.StepValue)]
    [InlineData(FillSeriesInputError.InvalidStop, FillSeriesInputFocusTarget.StopValue)]
    [InlineData(FillSeriesInputError.None, FillSeriesInputFocusTarget.StepValue)]
    public void FocusTargetFor_MapsValidationErrorsToDialogInput(
        FillSeriesInputError error,
        FillSeriesInputFocusTarget expected)
    {
        FillSeriesPlanner.FocusTargetFor(error).Should().Be(expected);
    }

    [Theory]
    [InlineData(FillCellsDirection.Down, 2, 1, true)]
    [InlineData(FillCellsDirection.Up, 2, 1, true)]
    [InlineData(FillCellsDirection.Right, 1, 2, true)]
    [InlineData(FillCellsDirection.Left, 1, 2, true)]
    [InlineData(FillCellsDirection.Down, 1, 2, false)]
    [InlineData(FillCellsDirection.Right, 2, 1, false)]
    public void CanFill_RequiresMultipleCellsInFillDirection(FillCellsDirection direction, uint rows, uint columns, bool expected)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, rows, columns));

        FillSeriesPlanner.CanFill(range, direction).Should().Be(expected);
    }

    [Fact]
    public void TryCreateOptions_RejectsInvalidStep()
    {
        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Rows, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "x", stopText: null, out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(FillSeriesInputError.InvalidStep);
    }

    [Fact]
    public void TryCreateOptions_RejectsPresentButInvalidStop()
    {
        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Rows, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "1", stopText: "nope", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(FillSeriesInputError.InvalidStop);
    }

    [Fact]
    public void TryCreateOptions_AcceptsBlankStopAsOpenEnded()
    {
        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Columns, FillSeriesType.Growth, FillSeriesDateUnit.Day,
            stepText: "2", stopText: "  ", out var options, out var error);

        ok.Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);
        options.Step.Should().Be(2);
        options.StopValue.Should().BeNull();
        options.Type.Should().Be(FillSeriesType.Growth);
        options.SeriesIn.Should().Be(FillSeriesDirection.Columns);
    }

    [Fact]
    public void TryCreateOptions_WithExplicitCulture_ParsesStepAndStopInThatCulture()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        var ok = FillSeriesPlanner.TryCreateOptions(
            FillSeriesDirection.Columns, FillSeriesType.Linear, FillSeriesDateUnit.Day,
            stepText: "1,5", stopText: "3,5", culture: culture, out var options, out var error);

        ok.Should().BeTrue();
        error.Should().Be(FillSeriesInputError.None);
        options.Step.Should().Be(1.5);
        options.StopValue.Should().Be(3.5);
    }

    [Fact]
    public void BuildLinearSeriesEdits_FillsRowMajorCellsAfterStartingCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Rows);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 14, 16);
    }

    [Fact]
    public void BuildLinearSeriesEdits_UsesColumnMajorOrderForExcelSeriesInColumns()
    {
        // A single seed with the rest of the selection blank: Excel enumerates column-major and,
        // since the second column has no seed of its own, continues the running series into it.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Columns);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 14, 16);
    }

    [Fact]
    public void BuildLinearSeriesEdits_TreatsEachColumnAsItsOwnSeriesWhenBothColumnsHaveSeeds()
    {
        // Excel treats "Series in Columns" as independent per-column series: each column's own
        // top cell is its seed, and a column that already has a value is never overwritten or
        // chained into from the previous column's running value.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 2, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(50));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Columns);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 52);
    }

    [Fact]
    public void BuildLinearSeriesEdits_StopsAtAscendingStopValue()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(0));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1, FillSeriesDirection.Rows, stopValue: 3);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void BuildLinearSeriesEdits_StopsAtDescendingStopValue()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(0));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: -1, FillSeriesDirection.Rows, stopValue: -3);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(-1, -2, -3);
    }

    [Fact]
    public void BuildSeriesEdits_RoutesGrowthThroughGeometricSeries()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4));
        sheet.SetCell(range.Start, new NumberValue(2));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(Step: 2, SeriesIn: FillSeriesDirection.Rows, Type: FillSeriesType.Growth));

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(4, 8, 16);
    }

    [Fact]
    public void BuildSeriesEdits_DateSeriesAdvancesByDays()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        var start = new DateTime(2026, 1, 1);
        sheet.SetCell(range.Start, new DateTimeValue(start.ToOADate()));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(Step: 1, SeriesIn: FillSeriesDirection.Rows, Type: FillSeriesType.Date, DateUnit: FillSeriesDateUnit.Day));

        edits.Select(edit => DateTime.FromOADate(((DateTimeValue)edit.NewCell.Value).Value).Date)
            .Should().Equal(new DateTime(2026, 1, 2), new DateTime(2026, 1, 3));
    }

    [Fact]
    public void BuildSeriesEdits_DateSeriesPreservesEndOfMonth()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 1, 31)));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(Step: 1, SeriesIn: FillSeriesDirection.Rows, Type: FillSeriesType.Date, DateUnit: FillSeriesDateUnit.Month));

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(
                new DateTime(2026, 2, 28),
                new DateTime(2026, 3, 31),
                new DateTime(2026, 4, 30));
    }

    [Fact]
    public void BuildDateSeriesEdits_SkipsWeekendsForExcelWeekdayUnit()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 5, 29)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet,
            range,
            step: 1,
            seriesIn: FillSeriesDirection.Rows,
            dateUnit: FillSeriesDateUnit.Weekday);

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(new DateTime(2026, 6, 1), new DateTime(2026, 6, 2));
    }

    [Fact]
    public void BuildSeriesEdits_ReturnsEmptyWhenSeedIsNotNumeric()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        sheet.SetCell(range.Start, new TextValue("hello"));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(Step: 1, Type: FillSeriesType.Linear));

        edits.Should().BeEmpty();
    }

    [Fact]
    public void BuildLinearSeriesEdits_ReturnsNoEditsWhenStartingCellIsNotNumeric()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        sheet.SetCell(range.Start, new TextValue("Start"));

        FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1, FillSeriesDirection.Rows).Should().BeEmpty();
    }

    // R48-commands-fill-series-3-2: a multi-column selection whose very FIRST column lacks a
    // valid seed must not wipe out the OTHER columns' perfectly valid, independent seeds --
    // Excel fills every column whose own top cell has a valid seed and leaves only the
    // invalid-seed columns untouched.
    [Fact]
    public void BuildLinearSeriesEdits_FillsOtherColumnsWhenOnlyFirstColumnLacksASeed()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 3, 4));
        // Column B (col 2): no seed at all (left blank) -- must be left alone.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(5));   // Column C seed
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(10));  // Column D seed

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1, FillSeriesDirection.Columns);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 2, 4),
            new CellAddress(sheet.Id, 3, 4));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(6, 7, 11, 12);
    }

    // Sibling no-regression case: the previously-correct "single seed, continue the running
    // series into later blank columns" behavior must still work once per-column validation
    // stops aborting the whole plan for an invalid FIRST seed.
    [Fact]
    public void BuildLinearSeriesEdits_StillChainsIntoLaterBlankColumnAfterASingleSeed()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Columns);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 14, 16);
    }
}
