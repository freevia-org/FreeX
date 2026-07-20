using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for round-36 Fill ▸ Series / fill-handle findings:
///   R36-commands-fill-series-2-1: BuildGrowthSeriesEdits must derive its ascending/descending
///     Stop Value clamp direction from the step's effect on the seed, not from comparing the
///     seed to the stop value.
///   R36-commands-fill-series-2-2: BuildGrowthSeriesEdits/BuildDateSeriesEdits must honor each
///     line's own pre-existing seed, like BuildLinearSeriesEdits already does.
///   R36-commands-fill-series-2-3: BuildSeriesEdits must route FillSeriesType.AutoFill through
///     the fill-handle's text/list detection (AutofillCommand), not the numeric-only Linear
///     builder.
///   R36-commands-fill-series-2-4: AutofillCommand must be able to match a user-defined custom
///     autofill list, not just the 4 hardcoded Excel default lists.
/// </summary>
public sealed class R36_FillSeriesPlannerTests
{
    // ── R36-commands-fill-series-2-1 ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildGrowthSeriesEdits_ClampsImmediatelyWhenStepOvershootsMismatchedStop()
    {
        // seed=10, step=3 is an ASCENDING growth trend (10 -> 30 -> 90 -> ...). Before the fix,
        // "ascending" was derived from startValue(10) <= stop(5) = false, so the stop check used
        // "value < stop" -- which increasing values never satisfy -- and the series ran away
        // (30, 90, 270, 810) instead of clamping. Real Excel fills nothing beyond the seed here
        // because the very first computed term (30) already exceeds the stop value (5).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(sheet, range, step: 3, FillSeriesDirection.Rows, stopValue: 5);

        edits.Should().BeEmpty();
    }

    [Fact]
    public void BuildGrowthSeriesEdits_StillStopsAtALegitimateAscendingStopValue()
    {
        // Sibling no-regression case: an ascending growth series with a stop value that is
        // actually above the seed must still clamp at the right point (unchanged behavior).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(sheet, range, step: 3, FillSeriesDirection.Rows, stopValue: 100);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(30, 90);
    }

    [Fact]
    public void BuildGrowthSeriesEdits_DescendingSeriesStillClampsOnItsOwnDirection()
    {
        // A descending growth series (step < 1) must keep using "value < stop" to clamp,
        // regardless of how the seed compares to the stop value.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(80));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(sheet, range, step: 0.5, FillSeriesDirection.Rows, stopValue: 15);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(40, 20);
    }

    // ── R36-commands-fill-series-2-2 ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildGrowthSeriesEdits_TreatsEachColumnAsItsOwnSeriesWhenBothColumnsHaveSeeds()
    {
        // Mirrors BuildLinearSeriesEdits_TreatsEachColumnAsItsOwnSeriesWhenBothColumnsHaveSeeds:
        // column A's seed (1) and column B's own pre-existing seed (100) must each drive their
        // own independent growth series. Before the fix, B1's 100 was silently overwritten by
        // the running value chained from column A.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(100));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Columns);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 2));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(2, 4, 200, 400);

        // B1's own seed must never appear as an edit (it is preserved, not overwritten).
        edits.Select(edit => edit.Address).Should().NotContain(new CellAddress(sheet.Id, 1, 2));
    }

    [Fact]
    public void BuildDateSeriesEdits_TreatsEachColumnAsItsOwnSeriesWhenBothColumnsHaveSeeds()
    {
        // Same per-line-seed gap for Date series: column B's own end-of-month seed must reseed
        // its own Month-unit clamp instead of continuing column A's running value/end-of-month
        // flag.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 1, 31)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 2, 14)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, seriesIn: FillSeriesDirection.Columns, dateUnit: FillSeriesDateUnit.Month);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 2));

        // Column A (end-of-month seed) keeps clamping to the end of each month.
        ((DateTimeValue)edits[0].NewCell.Value).ToDateTime().Date.Should().Be(new DateTime(2026, 2, 28));
        ((DateTimeValue)edits[1].NewCell.Value).ToDateTime().Date.Should().Be(new DateTime(2026, 3, 31));

        // Column B's own seed (the 14th, not end-of-month) must drive its own series untouched
        // by column A's end-of-month clamp.
        ((DateTimeValue)edits[2].NewCell.Value).ToDateTime().Date.Should().Be(new DateTime(2026, 3, 14));
        ((DateTimeValue)edits[3].NewCell.Value).ToDateTime().Date.Should().Be(new DateTime(2026, 4, 14));
    }

    // ── R36-commands-fill-series-2-3 ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildSeriesEdits_AutoFillRoutesThroughBuiltInWeekdayListInsteadOfLinear()
    {
        // Before the fix, FillSeriesType.AutoFill fell into BuildSeriesEdits' default arm
        // (BuildLinearSeriesEdits), which requires a NumberValue seed and silently returns no
        // edits for a text seed like "Monday".
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(range.Start, new TextValue("Monday"));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet, range, new FillSeriesOptions(Step: 1, SeriesIn: FillSeriesDirection.Columns, Type: FillSeriesType.AutoFill));

        edits.Select(edit => ((TextValue)edit.NewCell.Value).Value).Should().Equal("Tuesday", "Wednesday", "Thursday", "Friday");
    }

    [Fact]
    public void BuildAutoFillSeriesEdits_ContinuesATrailingNumberSeries()
    {
        // Sibling no-regression / broader coverage: AutoFill's other text-series detection
        // (trailing number) must also work, per-line, the same way the fill handle itself does.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(range.Start, new TextValue("Item 1"));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(edit => ((TextValue)edit.NewCell.Value).Value).Should().Equal("Item 2", "Item 3", "Item 4");
    }

    [Fact]
    public void BuildAutoFillSeriesEdits_ReturnsNoEditsForANonSeriesTextSeed()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(range.Start, new TextValue("hello"));

        FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns).Should().BeEmpty();
    }

    // ── R36-commands-fill-series-2-4 ──────────────────────────────────────────────────────────

    [Fact]
    public void AutofillCommand_MatchesUserDefinedCustomList()
    {
        // Before the fix, AutofillCommand.BuiltInLists was the only source TryCreateListSeries
        // ever consulted, so a user-defined custom list like "North, South, East, West" (Excel:
        // File > Options > Advanced > Edit Custom Lists) fell through to a plain copy instead of
        // wrapping like a built-in weekday/month list would.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        var customLists = new List<IReadOnlyList<string>> { new[] { "North", "South", "East", "West" } };

        new AutofillCommand(sheet.Id, sourceRange, fillRange, customLists: customLists).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new TextValue("South"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("North"));
    }

    [Fact]
    public void AutofillCommand_WithoutCustomLists_StillPlainCopiesAnUnrecognizedWord()
    {
        // No-regression sibling: with no custom lists supplied (the existing default), a word
        // that matches none of Excel's built-in lists keeps falling back to a plain copy.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("North"));
    }

    [Fact]
    public void AutofillCommand_TryCreateAutoFillTextSeries_WrapsACustomListCaseInsensitively()
    {
        // Matching stays case-insensitive: the all-lowercase seed "east" still matches the
        // list's "East" and wraps West -> North. But Excel reproduces the seed's own case
        // STYLE in the generated series (an all-lowercase seed continues in lowercase) rather
        // than emitting the list's canonical Title Case verbatim -- the same uniform autofill
        // case-reproduction Excel applies to built-in day/month lists (R55 fill-series-5-2 fix).
        var series = AutofillCommand.TryCreateAutoFillTextSeries(
            ["east"],
            [["North", "South", "East", "West"]]);

        series.Should().NotBeNull();
        series!(1).Should().Be(new TextValue("west"));
        series(2).Should().Be(new TextValue("north"));
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
