using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for R74-commands-fill-4-1: a Fill ▸ Series Stop Value must only clamp the
/// series LINE (column, for "Series in Columns"; row, for "Series in Rows") that actually
/// crossed it. Before the fix, the single foreach over the flat, line-major
/// EnumerateSeriesAddresses used a plain "break" once any line's running value passed the stop,
/// which exited the whole loop and silently abandoned every later line's independent fill --
/// even though each column/row is its own series (per the existing
/// BuildLinearSeriesEdits_TreatsEachColumnAsItsOwnSeries design).
/// </summary>
public sealed class R74_FillSeriesPlannerStopValueTests
{
    [Fact]
    public void BuildLinearSeriesEdits_StopValueOnlyClampsTheLineThatCrossedIt()
    {
        // Series in Columns over B1:C3. Column B's own seed (99) crosses the stop value (100)
        // one step in; column C's own seed (1) never gets anywhere near it. Before the fix, once
        // column B's running value passed 100 the whole loop broke and column C was left
        // completely unfilled.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(99));  // B1 seed
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(1));   // C1 seed

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1, FillSeriesDirection.Columns, stopValue: 100);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 2),   // B2: 100, exactly at the stop, still included
            new CellAddress(sheet.Id, 2, 3),   // C2
            new CellAddress(sheet.Id, 3, 3));  // C3
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(100, 2, 3);

        // B3 (101, past the stop) must never appear -- but only B3, not the rest of column C.
        edits.Select(edit => edit.Address).Should().NotContain(new CellAddress(sheet.Id, 3, 2));
    }

    [Fact]
    public void BuildGrowthSeriesEdits_StopValueOnlyClampsTheLineThatCrossedIt()
    {
        // Series in Columns over B1:C3. Column B's seed (40) crosses the stop (100) after one
        // doubling; column C's seed (1) stays far below it for both fill cells.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(40));  // B1 seed
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(1));   // C1 seed

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Columns, stopValue: 100);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 2),   // B2: 80, still below the stop
            new CellAddress(sheet.Id, 2, 3),   // C2
            new CellAddress(sheet.Id, 3, 3));  // C3
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(80, 2, 4);

        // B3 (160, past the stop) must never appear -- but only B3, not the rest of column C.
        edits.Select(edit => edit.Address).Should().NotContain(new CellAddress(sheet.Id, 3, 2));
    }

    [Fact]
    public void BuildDateSeriesEdits_StopValueOnlyClampsTheLineThatCrossedIt()
    {
        // Series in Columns over B1:C3. Column B's seed (2026-01-01) reaches the stop after one
        // day; column C's seed (2020-01-01) is years away from it for both fill cells.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 3, 3));
        var stop = new DateTime(2026, 1, 2).ToOADate();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 1, 1))); // B1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), DateTimeValue.FromDateTime(new DateTime(2020, 1, 1))); // C1

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, seriesIn: FillSeriesDirection.Columns, dateUnit: FillSeriesDateUnit.Day, stopValue: stop);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 2),   // B2: 2026-01-02, exactly at the stop
            new CellAddress(sheet.Id, 2, 3),   // C2
            new CellAddress(sheet.Id, 3, 3));  // C3
        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date).Should().Equal(
            new DateTime(2026, 1, 2),
            new DateTime(2020, 1, 2),
            new DateTime(2020, 1, 3));

        // B3 (2026-01-03, past the stop) must never appear -- but only B3, not the rest of column C.
        edits.Select(edit => edit.Address).Should().NotContain(new CellAddress(sheet.Id, 3, 2));
    }

    // ── Sibling no-regression: a single-line selection must still stop at the right cell ──────

    [Fact]
    public void BuildLinearSeriesEdits_SingleLineStopValueStillClampsCorrectly()
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
    public void BuildGrowthSeriesEdits_SingleLineStopValueStillClampsCorrectly()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(sheet, range, step: 3, FillSeriesDirection.Rows, stopValue: 100);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(30, 90);
    }

    [Fact]
    public void BuildDateSeriesEdits_SingleLineStopValueStillClampsCorrectly()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 1, 1)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, seriesIn: FillSeriesDirection.Rows, dateUnit: FillSeriesDateUnit.Day,
            stopValue: new DateTime(2026, 1, 3).ToOADate());

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date).Should().Equal(
            new DateTime(2026, 1, 2),
            new DateTime(2026, 1, 3));
    }
}
