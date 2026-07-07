using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B5 HIGH findings.
///
/// P48: default (automatic) Excel data bars must use a zero baseline, matching the x14
/// autoMin/autoMax semantics Excel always applies to data bars. Resolving the classic cfvo
/// type="min"/"max" straight to the range's actual minimum/maximum (as is correct for icon sets
/// and color scales) makes an all-positive range's smallest cell resolve to fraction 0 and render
/// no bar at all, when Excel actually draws a proportional bar with zero baseline.
/// </summary>
public class FreeXCleanupB5Tests
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
    public void DataBar_DefaultAutomaticThresholds_AllPositiveRange_UsesZeroBaselineNotActualMinimum()
    {
        // A1:A3 = 10, 20, 30 with the default (automatic) data bar thresholds (DataBarMinThresholdType
        // / DataBarMaxThresholdType left at their CfThresholdType.Min/Max defaults, matching a plain
        // Excel-authored data bar with no explicit min/max override). Excel draws these with a zero
        // baseline: ~1/3, ~2/3, full-length bars. Before the fix, min resolved to the actual range
        // minimum (10), so the smallest cell's fraction was (10-10)/(30-10)=0 -> no bar at all.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(30)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198)
            // DataBarMinThresholdType/DataBarMaxThresholdType left at defaults (Min/Max) --
            // this is what an Excel-authored "default" data bar looks like once loaded.
        });

        var viewport = GetViewport(wb, sheet);

        var smallest = GetCell(viewport, 1, 1);
        smallest.ConditionalDataBar.Should().NotBeNull(
            "Excel draws a proportional bar (baseline 0) for the smallest cell in an all-positive range, not an empty bar");
        smallest.ConditionalDataBar!.Value.EndFraction.Should().BeApproximately(
            10d / 30d, 0.0001, "automatic minimum is min(0, actual min) = 0, so 10 is 1/3 of the 0..30 span");

        var middle = GetCell(viewport, 2, 1);
        middle.ConditionalDataBar.Should().NotBeNull();
        middle.ConditionalDataBar!.Value.EndFraction.Should().BeApproximately(20d / 30d, 0.0001);

        var largest = GetCell(viewport, 3, 1);
        largest.ConditionalDataBar.Should().NotBeNull();
        largest.ConditionalDataBar!.Value.EndFraction.Should().BeApproximately(1d, 0.0001);
    }

    [Fact]
    public void DataBar_DefaultAutomaticThresholds_AllNegativeRange_UsesZeroBaselineNotActualMaximum()
    {
        // A1:A3 = -30, -20, -10. Automatic maximum is max(0, actual max) = 0, so bars grow toward
        // zero: the cell closest to zero (-10) gets the longest bar, matching Excel.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(-20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(-10)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198)
        });

        var viewport = GetViewport(wb, sheet);

        var closestToZero = GetCell(viewport, 3, 1);
        closestToZero.ConditionalDataBar.Should().NotBeNull();
        closestToZero.ConditionalDataBar!.Value.EndFraction.Should().BeApproximately(
            20d / 30d, 0.0001, "automatic maximum is max(0, actual max) = 0, so -10 is 20/30 of the -30..0 span");
    }

    [Fact]
    public void DataBar_ExplicitNumericMinimum_IsNotZeroClamped()
    {
        // Regression guard: an explicit (non-automatic) numeric minimum must be used as-is, not
        // clamped to zero -- the zero-baseline behavior is specific to the automatic Min/Max
        // threshold type, not every data bar.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(30)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "10",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "30"
        });

        var viewport = GetViewport(wb, sheet);

        var smallest = GetCell(viewport, 1, 1);
        smallest.ConditionalDataBar.Should().BeNull(
            "an explicit numeric minimum of 10 means value 10 has fraction 0 -- no zero-baseline clamp applies");
    }
}
