using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for R68-meta-3: BuildAutoFillSeriesEdits' numeric/date seed continuation used a
/// naive endpoint-average step ((numbers[^1] - numbers[0]) / (n - 1)) anchored at the last seed,
/// which is WRONG for a 3+ cell seed run that isn't already perfectly arithmetic. Excel's fill
/// handle (and FreeX's own AutofillCommand.FitScalarLine) instead fits a least-squares regression
/// line through ALL the seed points and continues that line, so Fill ▸ Series ▸ AutoFill must agree
/// with the fill-handle/Excel result for the same seed cells.
/// </summary>
public sealed class R68_FillSeriesPlannerAutoFillRegressionFitTests
{
    [Fact]
    public void BuildAutoFillSeriesEdits_ThreeNonArithmeticSeeds_ContinuesLeastSquaresRegressionLine()
    {
        // Seed 1, 2, 6 in A1:A3, fill to A6. The naive endpoint-average step ((6-1)/2 = 2.5,
        // anchored at 6) would wrongly produce 8.5, 11, 13.5. The correct least-squares fit
        // through (0,1), (1,2), (2,6) has slope 2.5 and intercept 0.5, so it continues as
        // 8, 10.5, 13.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(6));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1));
        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(8, 10.5, 13);
    }

    /// <summary>Sibling no-regression: a 2-point seed still continues its plain two-point slope.</summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_TwoPointSeed_StillContinuesPlainTwoPointSlope()
    {
        // 1, 2 seeded in A1:A2, selection A1:A5 -> A3=3, A4=4, A5=5 (unchanged).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(3, 4, 5);
    }

    /// <summary>
    /// Sibling no-regression: a perfectly-arithmetic 3-cell seed continues unchanged, since the
    /// regression line then passes exactly through every sampled point.
    /// </summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_PerfectlyArithmeticThreeSeeds_StillContinuesUnchanged()
    {
        // 1, 2, 3 seeded in A1:A3, selection A1:A6 -> A4=4, A5=5, A6=6 (unchanged).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(4, 5, 6);
    }

    /// <summary>
    /// Sibling no-regression: a date seed run's day-step continuation is unaffected by the
    /// regression-fit change (a single date seed still has no trend to fit and defaults to +1 day).
    /// </summary>
    [Fact]
    public void BuildAutoFillSeriesEdits_DateSeed_StillContinuesByDayUnchanged()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var seedDate = new DateTime(2026, 1, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(seedDate));

        var edits = FillSeriesPlanner.BuildAutoFillSeriesEdits(sheet, range, FillSeriesDirection.Columns);

        edits.Select(e => ((DateTimeValue)e.NewCell.Value).ToDateTime().Date).Should().Equal(
            seedDate.AddDays(1), seedDate.AddDays(2));
    }
}
