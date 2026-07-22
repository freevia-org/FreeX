using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R68-render-conditional-format-6-1: a data bar's Axis Position "Midpoint" was only ever honored
// when the range straddled zero (min < 0 && max >= 0). An all-positive dataset (e.g. 10/20/30) auto-
// clamps its automatic minimum to 0 (see EvaluateDataBar's zero-baseline comment) but that alone
// never makes min < 0, so the whole negative-axis branch — and with it axisAtMiddle — was skipped
// entirely, leaving Midpoint indistinguishable from Automatic/None for all-positive data. The fix
// honors axisAtMiddle regardless of whether the range straddles zero: it forces the axis to the
// cell's center (0.5) and scales bar length against half the cell width.
public partial class ConditionalFormatTests
{
    [Fact]
    public void DataBar_AxisMidpoint_AllPositiveData_DrawsAxisAtCenterWithHalfWidthBars()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(30)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarAxisPosition = "middle",
        });

        var viewport = GetViewport(wb, sheet);

        // Automatic minimum clamps to 0 (min(0,10)), automatic maximum is 30. With the axis forced
        // to the cell center, the max cell (30) should reach 0.5 + 1*0.5 = 1.0, not the left-anchored
        // full-width layout (0 -> 1.0) it would get under Automatic/None.
        var maxCell = GetCell(viewport, 3, 1);
        maxCell.ConditionalDataBar.Should().NotBeNull();
        var maxBar = maxCell.ConditionalDataBar!.Value;
        maxBar.IsNegative.Should().BeFalse("all values are positive");
        maxBar.AxisFraction.Should().BeApproximately(0.5, 0.0001, "Midpoint pins the axis at cell-center regardless of the all-positive skew");
        maxBar.StartFraction.Should().BeApproximately(0.5, 0.0001, "positive bars start at the forced-center axis");
        maxBar.EndFraction.Should().BeApproximately(1.0, 0.0001, "value 30 (== max) fills the whole right half");

        // Value 10 (== automatic min after zero-clamp is 0, so t = 10/30) should scale against the
        // half-width, not the full cell width.
        var minCell = GetCell(viewport, 1, 1);
        minCell.ConditionalDataBar.Should().NotBeNull();
        var minBar = minCell.ConditionalDataBar!.Value;
        minBar.AxisFraction.Should().BeApproximately(0.5, 0.0001);
        minBar.StartFraction.Should().BeApproximately(0.5, 0.0001);
        minBar.EndFraction.Should().BeApproximately(0.5 + (10d / 30d) * 0.5, 0.0001,
            "bar length is scaled against half the cell width (1 - axisFraction), not the full width");
    }

    [Fact]
    public void DataBar_AxisAutomatic_AllPositiveData_StillLeftAnchored_NoRegression()
    {
        // Sibling/no-regression: Automatic (unset) axis position on an all-positive range must
        // remain left-anchored full-width, exactly as before this fix.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(30)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
        });

        var viewport = GetViewport(wb, sheet);

        var maxCell = GetCell(viewport, 3, 1);
        maxCell.ConditionalDataBar.Should().NotBeNull();
        var maxBar = maxCell.ConditionalDataBar!.Value;
        maxBar.AxisFraction.Should().Be(0d, "Automatic axis position on an all-positive range has no axis");
        maxBar.StartFraction.Should().Be(0d, "left-anchored, unaffected by the Midpoint fix");
        maxBar.EndFraction.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void DataBar_AxisMidpoint_NegativeStraddlingRange_StillWorks()
    {
        // Sibling/no-regression: a range that already straddled zero must keep behaving exactly as
        // before — Midpoint still pins the axis at 0.5 there too (this was already correct pre-fix).
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
            DataBarAxisPosition = "middle",
        });

        var viewport = GetViewport(wb, sheet);

        var positiveCell = GetCell(viewport, 2, 1);
        positiveCell.ConditionalDataBar.Should().NotBeNull();
        var bar = positiveCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeFalse();
        bar.AxisFraction.Should().BeApproximately(0.5, 0.001);
        bar.StartFraction.Should().BeApproximately(0.5, 0.001);
        bar.EndFraction.Should().BeApproximately(1.0, 0.001);
    }
}
