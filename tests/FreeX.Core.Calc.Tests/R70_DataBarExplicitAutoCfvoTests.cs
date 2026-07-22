using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R70-io-cf-databar-cfvo-6-3 (deferred since round 61): the model must
/// distinguish an EXPLICIT Lowest/Highest Value data-bar endpoint (<see cref="CfThresholdType.Min"/>/
/// <see cref="CfThresholdType.Max"/>) from Excel's "Automatic" endpoint (<see cref="CfThresholdType.AutoMin"/>/
/// <see cref="CfThresholdType.AutoMax"/>), and only Automatic gets the zero-baseline clamp in
/// ViewportConditionalFormatEvaluator.Thresholds.cs. Before this fix, CfThresholdType had no Auto*
/// variants: every data-bar Min/Max threshold -- explicit or automatic alike -- was clamped
/// identically, so an explicit "Lowest Value"/"Highest Value" choice was silently treated as
/// Automatic.
/// </summary>
public sealed class R70_DataBarExplicitAutoCfvoTests
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

    /// <summary>
    /// Same all-positive data (10/20/30) fed through TWO data-bar rules that differ only in
    /// DataBarMinThresholdType: AutoMin (Automatic) must clamp the minimum to 0, giving the smallest
    /// value a ~1/3-length bar, while the explicit Min ("Lowest Value") must NOT clamp -- the smallest
    /// value equals the resolved minimum, so it gets no bar at all. Before the fix both rows used the
    /// same (always-clamped) behavior, so the explicit-Min row would incorrectly also show a bar.
    /// </summary>
    [Fact]
    public void DataBar_AutoMinVsExplicitMin_SameAllPositiveRange_ClampDiffers()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row * 10))); // 10/20/30
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(row * 10))); // 10/20/30

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.AutoMin,
            DataBarMaxThresholdType = CfThresholdType.AutoMax,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2)),
            Priority = 2,
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Min,
            DataBarMaxThresholdType = CfThresholdType.Max,
        });

        var viewport = GetViewport(wb, sheet);

        // Automatic: minimum clamps to min(0, 10) = 0, so the smallest value (10) of range 10..30
        // gets a proportional (~1/3) bar rather than an empty one.
        var autoSmallest = GetCell(viewport, 1, 1);
        autoSmallest.ConditionalDataBar.Should().NotBeNull("AutoMin clamps the automatic minimum to 0");
        autoSmallest.ConditionalDataBar!.Value.StartFraction.Should().Be(0);
        autoSmallest.ConditionalDataBar.Value.EndFraction.Should().BeApproximately(1d / 3d, 1e-9);

        // Explicit "Lowest Value": the resolved minimum IS the actual minimum (10, unclamped), so the
        // smallest cell's fraction is (10-10)/(30-10)=0 -- an authoritative empty bar.
        var explicitSmallest = GetCell(viewport, 1, 2);
        explicitSmallest.ConditionalDataBar.Should().BeNull(
            "an explicit Lowest Value endpoint must not receive Excel's Automatic zero-baseline clamp");
    }

    /// <summary>
    /// Mirrors the min-side test for the maximum endpoint over an all-negative range (-30/-20/-10).
    /// AutoMax clamps the resolved maximum to max(0, -10) = 0, which (since the resolved minimum -30
    /// is already &lt;= 0) puts the range through the negative-axis path with the axis pinned to the
    /// right edge -- so even the MOST negative value (-30) gets a full-length negative bar. Explicit
    /// Max ("Highest Value") leaves the resolved maximum at the actual -10 (unclamped, still &lt; 0),
    /// which never satisfies the negative-axis path's "max >= 0" condition, so it falls through to the
    /// plain left-anchored fraction instead -- and the most negative value (-30), being the resolved
    /// minimum itself, resolves to fraction 0: an authoritative empty bar.
    /// </summary>
    [Fact]
    public void DataBar_AutoMaxVsExplicitMax_SameAllNegativeRange_ClampDiffers()
    {
        var (wb, sheet) = MakeWorkbook();
        var values = new[] { -30, -20, -10 };
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(values[row - 1])));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(values[row - 1])));
        }

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.AutoMin,
            DataBarMaxThresholdType = CfThresholdType.AutoMax,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2)),
            Priority = 2,
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Min,
            DataBarMaxThresholdType = CfThresholdType.Max,
        });

        var viewport = GetViewport(wb, sheet);

        // Automatic: max clamps to max(0, -10) = 0 -- with min already -30 (<=0), this enters the
        // negative-axis path with the axis pinned to the right edge (axisFraction = 1), so the most
        // negative value (-30) fills the FULL negative side, not an empty bar.
        var autoMostNegative = GetCell(viewport, 1, 1);
        autoMostNegative.ConditionalDataBar.Should().NotBeNull("AutoMax clamps the automatic maximum to 0");
        autoMostNegative.ConditionalDataBar!.Value.IsNegative.Should().BeTrue();
        autoMostNegative.ConditionalDataBar.Value.AxisFraction.Should().BeApproximately(1d, 1e-9);
        autoMostNegative.ConditionalDataBar.Value.StartFraction.Should().BeApproximately(0d, 1e-9);
        autoMostNegative.ConditionalDataBar.Value.EndFraction.Should().BeApproximately(1d, 1e-9);

        // Explicit "Highest Value": max stays at the actual -10 (unclamped, still negative), so the
        // negative-axis path never engages; the plain left-anchored fraction for the most negative
        // value (-30, which equals the resolved minimum) is (-30 - -30)/(-10 - -30) = 0 -- empty bar.
        var explicitMostNegative = GetCell(viewport, 1, 2);
        explicitMostNegative.ConditionalDataBar.Should().BeNull(
            "an explicit Highest Value endpoint must not receive Excel's Automatic zero-baseline clamp");
    }
}
