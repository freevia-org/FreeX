using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression tests for round-13 fix bucket S3:
/// R13-drag-fill-series-2 (a Month/Year date series chained AddMonths/AddYears off the previous,
/// already day-clamped cell instead of measuring from the original seed date, so a seed that isn't
/// the last day of its month permanently lost its day-of-month after the series crossed a short
/// month like February), and R13-drag-fill-series-3 (Fill Series over a 2D selection treated only
/// the single top-left cell as a seed, so a second column/row that already held its own seed value
/// was silently overwritten and chained into instead of being preserved as an independent series).
/// </summary>
public sealed class FreeXR13S3Tests
{
    // ── R13-drag-fill-series-2: Month/Year date series must clamp from the ORIGINAL seed, not the previous cell ──

    [Fact]
    public void BuildDateSeriesEdits_MonthUnit_ClampsEachTargetFromOriginalSeedNotPreviousCell()
    {
        // Seed 30-Jan-2026 is NOT the last day of January, so preserveEndOfMonth is false. Excel
        // computes every target as seed.AddMonths(step * i), clamping the day only against the
        // seed's day (30) for each target month: 30-Jan, 28-Feb (Feb has 28 days), 30-Mar, 30-Apr,
        // 30-May. A buggy implementation that chains AddMonths off the previous (clamped) cell would
        // instead carry the 28-Feb clamp forward forever, producing 28-Mar/28-Apr/28-May.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 1, 30)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet,
            range,
            step: 1,
            seriesIn: FillSeriesDirection.Rows,
            dateUnit: FillSeriesDateUnit.Month);

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(
                new DateTime(2026, 2, 28),
                new DateTime(2026, 3, 30),
                new DateTime(2026, 4, 30),
                new DateTime(2026, 5, 30));
    }

    // ── R13-drag-fill-series-3: 2D Fill Series must treat each column as its own series line ──

    [Fact]
    public void BuildLinearSeriesEdits_SeriesInColumns_NeverOverwritesASeededSecondColumn()
    {
        // B1=10, C1=50, "Series in Columns", Linear step 2. Excel fills each column
        // independently from its own seed (B: 10,12; C: 50,52) and never overwrites or chains
        // through C1 -- a buggy single-continuous-chain implementation would overwrite C1 with 14.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 2, 3));
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(b1, new NumberValue(10));
        sheet.SetCell(c1, new NumberValue(50));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2, FillSeriesDirection.Columns);

        // C1 must not appear among the edits at all: its seed is preserved untouched.
        edits.Select(edit => edit.Address).Should().NotContain(c1);
        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 52);
    }
}
