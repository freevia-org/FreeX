using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R45-render-databar-iconset-grid-3-1: Data Bar "Bar Direction" (x14:dataBar/@direction) was parsed
/// and round-tripped on load/save but never consulted by ViewportConditionalFormatEvaluator when
/// rendering the bar -- every data bar rendered left-to-right regardless of an explicit
/// direction="rightToLeft" rule, and regardless of the sheet's own right-to-left reading order for
/// the default "Context" direction. Fixed by mirroring StartFraction/EndFraction/AxisFraction about
/// the cell's horizontal center when the resolved direction is right-to-left.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void DataBar_ExplicitRightToLeftDirection_MirrorsBarEvenOnLtrSheet()
    {
        // Range 0..100, cell value 50 -> on a left-to-right bar this would be StartFraction=0,
        // EndFraction=0.5. With an explicit direction="rightToLeft" rule, Excel grows the bar from
        // the cell's right edge instead, which mirrors to StartFraction=0.5, EndFraction=1.0.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarDirection = "rightToLeft",
        });

        var viewport = GetViewport(wb, sheet);

        var mid = GetCell(viewport, 2, 1);
        mid.ConditionalDataBar.Should().NotBeNull();
        var bar = mid.ConditionalDataBar!.Value;
        bar.StartFraction.Should().BeApproximately(0.5, 0.0001, "an explicit right-to-left bar direction mirrors the bar to grow from the right edge");
        bar.EndFraction.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void DataBar_ExplicitLeftToRightDirection_StaysLeftToRightEvenOnRtlSheet()
    {
        // Regression guard / sibling case: an explicit direction="leftToRight" rule must NOT follow
        // the sheet's right-to-left reading order -- it always forces left-to-right layout.
        var (wb, sheet) = MakeWorkbook();
        sheet.IsRightToLeft = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarDirection = "leftToRight",
        });

        var viewport = GetViewport(wb, sheet);

        var mid = GetCell(viewport, 2, 1);
        mid.ConditionalDataBar.Should().NotBeNull();
        var bar = mid.ConditionalDataBar!.Value;
        bar.StartFraction.Should().Be(0d, "an explicit left-to-right bar direction always wins over the sheet's reading order");
        bar.EndFraction.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void DataBar_ContextDirection_FollowsRightToLeftSheetReadingOrder()
    {
        // Default ("Context") direction on a sheet authored right-to-left (sheetView/@rightToLeft)
        // must mirror automatically, matching Excel's behavior for RTL-authored workbooks.
        var (wb, sheet) = MakeWorkbook();
        sheet.IsRightToLeft = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            // DataBarDirection left null, which is the "Context" default.
        });

        var viewport = GetViewport(wb, sheet);

        var mid = GetCell(viewport, 2, 1);
        mid.ConditionalDataBar.Should().NotBeNull();
        var bar = mid.ConditionalDataBar!.Value;
        bar.StartFraction.Should().BeApproximately(0.5, 0.0001, "Context direction follows the sheet's right-to-left reading order");
        bar.EndFraction.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void DataBar_ContextDirection_StaysLeftToRightOnLtrSheet_NoRegression()
    {
        // Sibling no-regression case: the ordinary (no direction attribute, LTR sheet) path used by
        // every pre-existing data bar test must be completely unaffected by this change.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
        });

        var viewport = GetViewport(wb, sheet);

        var mid = GetCell(viewport, 2, 1);
        mid.ConditionalDataBar.Should().NotBeNull();
        var bar = mid.ConditionalDataBar!.Value;
        bar.StartFraction.Should().Be(0d);
        bar.EndFraction.Should().BeApproximately(0.5, 0.0001);
    }

    [Fact]
    public void DataBar_ExplicitRightToLeftDirection_MirrorsNegativeAxisBarsToo()
    {
        // Range -50..50 straddling zero with axis at 0.5 in the normal LTR layout. With an explicit
        // right-to-left direction, the axis mirrors to 1 - 0.5 = 0.5 (middle stays put), but the
        // positive/negative bar segments and the axis-relative growth direction all mirror: the
        // positive value (50) which normally grows rightward from the axis (0.5 -> 1.0) now grows
        // leftward from the axis (0.0 -> 0.5), matching a right-to-left mirrored layout.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-50)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(0, 112, 192),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            DataBarDirection = "rightToLeft",
        });

        var viewport = GetViewport(wb, sheet);

        var positiveCell = GetCell(viewport, 2, 1);
        positiveCell.ConditionalDataBar.Should().NotBeNull();
        var bar = positiveCell.ConditionalDataBar!.Value;
        bar.AxisFraction.Should().BeApproximately(0.5, 0.001, "axis sits at the mirrored zero crossing, which is still 0.5 for a symmetric range");
        bar.StartFraction.Should().BeApproximately(0.0, 0.001, "mirrored positive bar now grows leftward from the axis toward the left edge");
        bar.EndFraction.Should().BeApproximately(0.5, 0.001, "mirrored positive bar ends at the axis instead of starting there");
    }
}
