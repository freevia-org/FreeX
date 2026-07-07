using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression test for FreeX round-11 finding R11-conditional-format-1.
///
/// A data bar rule with default automatic (Min/Max) thresholds over an all-negative range must
/// keep Excel's zero-baseline behavior: the automatic maximum clamps to 0 (Math.Max(0, actualMax)),
/// so the range never truly "has no positive side" from the axis-placement perspective -- the axis
/// sits at the right edge (fraction 1.0) and every bar grows leftward from it using the negative
/// fill color, with the most-negative value producing the longest bar. Before the fix, the
/// negative-axis branch required "max > 0" (strictly), which an all-negative range's clamped
/// max == 0 fails, so evaluation fell through to the positive-only path: length ordering was
/// inverted (least-negative got the longest bar) and the wrong (positive) fill color was used.
/// </summary>
public class FreeXR11B11Tests
{
    private static (Workbook workbook, Sheet sheet) MakeWorkbook() =>
        TestWorkbookFixture.CreateWorkbook();

    private static ViewportModel GetViewport(Workbook wb, Sheet sheet)
    {
        var svc = new ViewportService();
        return svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
    }

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void DataBar_AllNegativeRange_AutomaticThresholds_LongestBarIsMostNegativeUsingNegativeColor()
    {
        // Values -10, -20, -30 (all negative). Automatic min/max: min = Math.Min(0, -30) = -30,
        // max = Math.Max(0, -10) = 0 (Excel's zero-baseline clamp). Excel places the axis at the
        // right edge and draws every bar growing leftward in the negative fill color, with -30
        // (furthest from zero) producing the longest bar and -10 (closest to zero) the shortest.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(-20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(-30)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            // DataBarMinThresholdType/DataBarMaxThresholdType default to Min/Max (automatic).
            // DataBarAxisPosition unset -> automatic (not "none"), so the negative-axis branch applies.
        });

        var viewport = GetViewport(wb, sheet);

        var leastNegative = GetCell(viewport, 1, 1); // -10
        var midNegative = GetCell(viewport, 2, 1);   // -20
        var mostNegative = GetCell(viewport, 3, 1);  // -30

        leastNegative.ConditionalDataBar.Should().NotBeNull();
        midNegative.ConditionalDataBar.Should().NotBeNull();
        mostNegative.ConditionalDataBar.Should().NotBeNull();

        var leastBar = leastNegative.ConditionalDataBar!.Value;
        var midBar = midNegative.ConditionalDataBar!.Value;
        var mostBar = mostNegative.ConditionalDataBar!.Value;

        // Axis sits at the right edge (fraction 1.0) since max clamps to 0 and min = -30.
        leastBar.AxisFraction.Should().BeApproximately(1.0, 0.001);
        midBar.AxisFraction.Should().BeApproximately(1.0, 0.001);
        mostBar.AxisFraction.Should().BeApproximately(1.0, 0.001);

        // All three must render as negative bars using the negative fill color, growing leftward
        // from the axis (EndFraction pinned at the axis, StartFraction moving left as magnitude grows).
        leastBar.IsNegative.Should().BeTrue("an all-negative range must use the negative-axis path, not the positive fallthrough");
        midBar.IsNegative.Should().BeTrue();
        mostBar.IsNegative.Should().BeTrue();

        leastBar.FillColor.Should().Be(new RgbColor(255, 0, 0), "negative fill color must be used, not the positive DataBarColor");
        midBar.FillColor.Should().Be(new RgbColor(255, 0, 0));
        mostBar.FillColor.Should().Be(new RgbColor(255, 0, 0));

        leastBar.EndFraction.Should().BeApproximately(1.0, 0.001);
        midBar.EndFraction.Should().BeApproximately(1.0, 0.001);
        mostBar.EndFraction.Should().BeApproximately(1.0, 0.001);

        // Bar length (axis - start) must increase monotonically with magnitude: -30 longest, -10 shortest.
        var leastLength = leastBar.EndFraction - leastBar.StartFraction;
        var midLength = midBar.EndFraction - midBar.StartFraction;
        var mostLength = mostBar.EndFraction - mostBar.StartFraction;

        mostLength.Should().BeGreaterThan(midLength, "the most-negative value (-30) must have a longer bar than -20");
        midLength.Should().BeGreaterThan(leastLength, "-20 must have a longer bar than the least-negative value (-10)");

        // The most-negative cell must produce a full-length bar spanning the whole range (start at 0).
        mostBar.StartFraction.Should().BeApproximately(0.0, 0.001, "the most-negative value fills the entire bar width");
    }
}
