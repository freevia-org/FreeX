using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for R66-commands-fill-series-6-1: Fill ▸ Series ▸ AutoFill silently no-opped
/// for a NUMERIC (or date) seed because BuildAutoFillSeriesEdits only ever checked whether a
/// line's leading cell was a TextValue. Excel's AutoFill detects a 2+ seed arithmetic/date series
/// from the SEED CELLS already in the selection and continues it (a single numeric/date seed
/// defaults to a plain +1/+1-day step, like the fill handle's own lone-cell default).
/// </summary>
public sealed class R66_FillSeriesPlannerAutoFillNumericTests
{
    [Fact]
    public void BuildAutoFillSeriesEdits_TwoNumberSeeds_ContinuesTheArithmeticStep()
    {
        // 1, 2 seeded in A1:A2, selection A1:A5 -> A3=3, A4=4, A5=5.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1));
        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(3, 4, 5);
    }

    [Fact]
    public void BuildAutoFillSeriesEdits_SingleNumberSeed_DefaultsToPlusOneStep()
    {
        // A single numeric seed (5) has no natural trend to fit, so it defaults to +1, matching
        // the fill handle's own lone-cell numeric default.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(6, 7, 8);
    }

    [Fact]
    public void BuildAutoFillSeriesEdits_DateSeed_ContinuesByDay()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var seedDate = new DateTime(2026, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(seedDate));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((DateTimeValue)e.NewCell.Value).ToDateTime().Date).Should().Equal(
            seedDate.AddDays(1), seedDate.AddDays(2));
    }

    /// <summary>Sibling no-regression: a text seed still works exactly as before (R36 fix).</summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_TextSeed_StillContinuesTrailingNumberSeries()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(range.Start, new TextValue("Item 1"));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((TextValue)e.NewCell.Value).Value).Should().Equal("Item 2", "Item 3", "Item 4");
    }

    /// <summary>Sibling no-regression: a non-series text seed still no-ops (unchanged behavior).</summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_NonSeriesTextSeed_StillNoOps()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(range.Start, new TextValue("hello"));

        FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns).Should().BeEmpty();
    }
}
