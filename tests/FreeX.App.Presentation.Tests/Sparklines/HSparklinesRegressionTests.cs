using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// Regression tests for group H-sparklines findings:
/// H13 (a custom/group axis-max override smaller than the data's own magnitude must still clamp
/// column/win-loss bars — the override "replaces" the per-sparkline max, it does not merely grow
/// it), and
/// H47 (SparklineModel.DisplayHidden must include hidden-row/column source values in the series
/// read for rendering, matching Excel's "Show data in hidden rows and columns").
/// </summary>
public sealed class HSparklinesRegressionTests
{
    private static readonly LayoutRect Cell = new(0, 0, 100, 40);

    // ══════════════════════════════════════════════════════════════════════════
    // H13 — Column/win-loss axis-max override must clamp, not just grow
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void VisitColumnLayout_OverrideMaxAbsSmallerThanData_RescalesUnclampedBar()
    {
        // Mixed-sign data keeps the centered axis (half cell height per side) so this regression
        // stays focused on the override clamping math, independent of the R14 all-one-sign
        // zero-baseline fix (which only changes the axis/height for same-signed data).
        var values = new List<double> { 10, -1 };
        var maxBarHeight = Cell.Height / 2; // 20

        var withOverride = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false, overrideMaxAbs: 2.0);

        // bar for value=-1: |-1|/2 * 20 = 10 (unclamped, since 10 <= maxBarHeight).
        withOverride.Bars[1].Rect.Height.Should().Be(10,
            because: "a Custom axis max smaller than the data max must still rescale bars against it");

        // bar for value=10: |10|/2 * 20 = 100, clamped to maxBarHeight (20).
        withOverride.Bars[0].Rect.Height.Should().Be(maxBarHeight);
    }

    [Fact]
    public void VisitColumnLayout_OverrideMaxAbsLargerThanData_StillGrowsAsBefore()
    {
        // Existing behavior (override larger than data max) must be unaffected by the fix.
        var values = new List<double> { 2, -2 };

        var noOverride = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false, overrideMaxAbs: null);
        var withOverride = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false, overrideMaxAbs: 4.0);

        withOverride.Bars[0].Rect.Height.Should().BeApproximately(noOverride.Bars[0].Rect.Height / 2.0, 0.0001,
            because: "doubling the override max should halve the bar height, same as before this fix");
    }

    [Fact]
    public void VisitColumnLayout_WinLoss_OverrideMaxAbsIsIgnored()
    {
        // Win/loss bars are fixed half-height keyed only on sign — an axis-max override must not
        // change their geometry either way (matches Excel: win/loss ignores value magnitude).
        var values = new List<double> { 10, -3 };

        var layout = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: true, overrideMaxAbs: 2.0);

        layout.Bars.Should().OnlyContain(b => b.Rect.Height == Cell.Height / 2);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // H47 — DisplayHidden must include hidden-row/column values in the series
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadSeries_DisplayHiddenTrue_IncludesHiddenRowAndColumnValues()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.HiddenRows.Add(2);

        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 1)),
            Location = new CellAddress(sheet.Id, 1, 2),
            Kind = SparklineKind.Line,
            DisplayHidden = true
        };

        var series = SparklineSeriesReader.ReadSeries(workbook, sheet, sparkline);

        series.Should().Equal(
            new double[] { 1, 2, 3, 4 },
            "DisplayHidden=true must include values from hidden rows, matching Excel's " +
            "'Show data in hidden rows and columns'");
    }

    [Fact]
    public void ReadSeries_DisplayHiddenFalse_StillSkipsHiddenRowAndColumnValues()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.HiddenRows.Add(2);

        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 1)),
            Location = new CellAddress(sheet.Id, 1, 2),
            Kind = SparklineKind.Line,
            DisplayHidden = false
        };

        var series = SparklineSeriesReader.ReadSeries(workbook, sheet, sparkline);

        series.Should().Equal(
            new double[] { 1, 3, 4 },
            "default DisplayHidden=false must keep excluding hidden rows/columns");
    }
}
