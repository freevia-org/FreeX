using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Tests for R81-backlog-fillseries-Trend: Excel's Fill ▸ Series dialog has a "Trend" checkbox
/// (enabled for the Linear and Growth series types) that, when checked, ignores the Step value and
/// instead continues a least-squares best-fit line (Linear) or best-fit exponential curve (Growth)
/// computed from ALL of a line's already-populated seed values -- not the fixed step the dialog
/// otherwise chains from just the line's leading cell. FreeX previously had no such mode at all
/// (<see cref="FillSeriesOptions"/> had no Trend field, and BuildLinearSeriesEdits/BuildGrowthSeriesEdits
/// only ever chained a fixed step); these tests exercise the new <c>trend</c> parameter/<see
/// cref="FillSeriesOptions.Trend"/> flag added to close that gap.
/// </summary>
public sealed class R81_FillSeriesTrendTests
{
    [Fact]
    public void BuildLinearSeriesEdits_TrendTrue_NonCollinearSeeds_ContinuesLeastSquaresExtrapolation()
    {
        // Seeds 1, 2, 4, 5 in A1:A4 (x = 0, 1, 2, 3) are not perfectly linear. The least-squares
        // fit has slope 1.4 and intercept 0.9 (fitted line y = 0.9 + 1.4x), so continuing it for
        // x = 4, 5, 6, 7 gives 6.5, 7.9, 9.3, 10.7 -- a completely different sequence than the
        // fixed-step chain (2, 3, 4, 5, 6, 7, 8) the non-Trend engine would produce from the same
        // seed cells with the default step of 1.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(5));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, trend: true);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 1),
            new CellAddress(sheet.Id, 8, 1));
        // Compared with a tolerance since 7.9/9.3 aren't exactly representable in binary
        // floating-point and the fitted-line arithmetic (slope * offset + anchor) can differ from
        // the literal by a few ULPs even though both are mathematically 7.9 and 9.3.
        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(
            [6.5, 7.9, 9.3, 10.7], (actual, expected) => Math.Abs(actual - expected) < 1e-9);
    }

    [Fact]
    public void BuildSeriesEdits_LinearTrendTrue_RoutesThroughLeastSquaresExtrapolation()
    {
        // Same seeds/expectation as above, but driven through the public FillSeriesOptions/
        // BuildSeriesEdits dispatch entry point (the actual Fill ▸ Series dialog call path) to
        // confirm the Trend flag is wired all the way through, not just reachable via the
        // lower-level BuildLinearSeriesEdits overload.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(5));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(Step: 1, SeriesIn: FillSeriesDirection.Columns, Type: FillSeriesType.Linear, Trend: true));

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(
            [6.5, 7.9, 9.3, 10.7], (actual, expected) => Math.Abs(actual - expected) < 1e-9);
    }

    [Fact]
    public void BuildGrowthSeriesEdits_TrendTrue_NonGeometricSeeds_ContinuesLogLinearExtrapolation()
    {
        // Seeds 2, 3, 7, 20 in A1:A4 (x = 0, 1, 2, 3) are not a constant-ratio geometric run. The
        // least-squares fit of ln(y) against x has slope ~0.7755 / intercept ~0.5201, so the
        // best-fit exponential continues as ~37.417, ~81.257, ~176.465 for x = 4, 5, 6 -- verified
        // independently (PowerShell) against the exact same least-squares formula.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(20));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(
            sheet, range, step: 2, FillSeriesDirection.Columns, trend: true);

        edits.Select(e => e.Address).Should().Equal(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 1));
        var values = edits.Select(e => ((NumberValue)e.NewCell.Value).Value).ToList();
        values[0].Should().BeApproximately(37.4165738677394, 1e-8);
        values[1].Should().BeApproximately(81.2571706718652, 1e-7);
        values[2].Should().BeApproximately(176.465322798822, 1e-6);
    }

    [Fact]
    public void BuildLinearSeriesEdits_TrendFalseByDefault_StillUsesFixedStep()
    {
        // No-regression sibling: with Trend left at its default (false), a single seed with a
        // fixed step must keep chaining exactly like before this change -- 1 -> 2, 3, 4, 5.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1, FillSeriesDirection.Columns);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(2, 3, 4, 5);
    }

    [Fact]
    public void BuildSeriesEdits_GrowthDefaultOptions_TrendFalse_StillUsesFixedRatio()
    {
        // No-regression sibling for Growth: FillSeriesOptions without an explicit Trend value
        // defaults to false, so the fixed-ratio chain (2 -> 4, 8, 16) is unaffected.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 4));
        sheet.SetCell(range.Start, new NumberValue(2));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(Step: 2, SeriesIn: FillSeriesDirection.Rows, Type: FillSeriesType.Growth));

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(4, 8, 16);
    }

    [Fact]
    public void BuildLinearSeriesEdits_TrendTrue_SingleSeed_FallsBackToStep()
    {
        // Degenerate case: a lone seed has no trend line to fit, so Trend falls back to the
        // manually entered Step value (Excel's own behavior for a single known point) instead of
        // failing or no-oping: 10 with step 3 -> 13, 16, 19.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(
            sheet, range, step: 3, FillSeriesDirection.Columns, trend: true);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(13, 16, 19);
    }

    [Fact]
    public void BuildGrowthSeriesEdits_TrendTrue_SingleSeed_FallsBackToStepAsRatio()
    {
        // Same degenerate case for Growth-Trend: a lone seed falls back to the Step value used as
        // a multiplicative ratio: 2 with step 3 -> 6, 18, 54.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(
            sheet, range, step: 3, FillSeriesDirection.Columns, trend: true);

        edits.Select(e => ((NumberValue)e.NewCell.Value).Value).Should().Equal(6, 18, 54);
    }

    [Fact]
    public void BuildGrowthSeriesEdits_TrendTrue_SeedRunContainsNonPositiveValue_LeavesLineUntouched()
    {
        // Degenerate case: a zero/negative value has no logarithm, so it cannot be growth-fitted
        // (Excel raises #NUM! for this). FreeX has no per-line error channel here, so it leaves
        // the line untouched (no edits) rather than fabricating a bogus fit or crashing.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(-3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));

        var edits = FillSeriesPlanner.BuildGrowthSeriesEdits(
            sheet, range, step: 2, FillSeriesDirection.Columns, trend: true);

        edits.Should().BeEmpty();
    }

    [Fact]
    public void BuildLinearSeriesEdits_TrendTrue_MultipleIndependentColumns_FitEachLineSeparately()
    {
        // "Series in Columns" treats each column as its own independent line even in Trend mode:
        // column B's non-collinear seeds (1, 2, 4, 5) fit their own trend, unaffected by column
        // C's own (different) seeds (0, 10) fitting a completely different line.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 8, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, trend: true);

        var columnB = edits.Where(e => e.Address.Col == 2).Select(e => ((NumberValue)e.NewCell.Value).Value);
        var columnC = edits.Where(e => e.Address.Col == 3).Select(e => ((NumberValue)e.NewCell.Value).Value);
        // Column B has 4 seed rows (1-4), leaving 4 blank rows (5-8) to fill: 6.5, 7.9, 9.3, 10.7
        // (tolerance-compared for the same binary floating-point reason as the standalone test).
        columnB.Should().Equal([6.5, 7.9, 9.3, 10.7], (actual, expected) => Math.Abs(actual - expected) < 1e-9);
        // Column C's 2-point seed (0, 10) is perfectly linear already (slope 10, intercept 0), so
        // its regression line passes exactly through both points and continues into all 6 blank
        // rows (3-8): 20, 30, 40, 50, 60, 70.
        columnC.Should().Equal(20, 30, 40, 50, 60, 70);
    }
}
