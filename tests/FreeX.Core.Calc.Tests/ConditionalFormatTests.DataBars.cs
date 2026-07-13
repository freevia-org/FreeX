using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void DataBar_ProducesProportionalDisplayPayloadWithoutFullCellFill()
    {
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
            DataBarGradient = true,
            DataBarShowValue = true
        });

        var viewport = GetViewport(wb, sheet);

        var mid = GetCell(viewport, 2, 1);
        mid.ConditionalDataBar.Should().NotBeNull();
        mid.ConditionalDataBar!.Value.StartFraction.Should().Be(0);
        mid.ConditionalDataBar.Value.EndFraction.Should().BeApproximately(0.5, 0.0001);
        mid.ConditionalDataBar.Value.FillColor.Should().Be(new RgbColor(99, 142, 198));
        mid.ConditionalDataBar.Value.Gradient.Should().BeTrue();
        mid.ConditionalDataBar.Value.ShowValue.Should().BeTrue();
        mid.Style?.FillColor.Should().BeNull("data bars render as bars, not full-cell conditional fills");
    }

    [Fact]
    public void DataBar_RespectsShowValueAndLengthSettings()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = false,
            DataBarMinLength = 10,
            DataBarMaxLength = 80
        });

        var viewport = GetViewport(wb, sheet);

        var max = GetCell(viewport, 2, 1);
        max.DisplayText.Should().BeEmpty();
        max.ConditionalDataBar.Should().NotBeNull();
        max.ConditionalDataBar!.Value.EndFraction.Should().BeApproximately(0.8, 0.0001);
        max.ConditionalDataBar.Value.ShowValue.Should().BeFalse();
    }

    // ── Negative axis tests ────────────────────────────────────────────────────

    [Fact]
    public void DataBar_NegativeAxis_PositiveValueExtendsRightFromAxis()
    {
        // Range -50..50, axis at 0.5. Cell value = 50 (max positive) → bar from 0.5 to 1.0.
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
            DataBarAxisColor = new RgbColor(0, 0, 0),
            // axisPosition defaults to "middle" (not "none") — automatic axis applies
        });

        var viewport = GetViewport(wb, sheet);

        var positiveCell = GetCell(viewport, 2, 1);
        positiveCell.ConditionalDataBar.Should().NotBeNull();
        var bar = positiveCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeFalse();
        bar.AxisFraction.Should().BeApproximately(0.5, 0.001, "axis is at zero crossing of range -50..50");
        bar.StartFraction.Should().BeApproximately(0.5, 0.001, "positive bar starts at axis");
        bar.EndFraction.Should().BeApproximately(1.0, 0.001, "positive max value fills the right half");
        bar.FillColor.Should().Be(new RgbColor(0, 112, 192));
    }

    [Fact]
    public void DataBar_NegativeAxis_NegativeValueExtendsLeftFromAxis()
    {
        // Range -50..50, axis at 0.5. Cell value = -50 (min negative) → bar from 0.0 to 0.5.
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
            DataBarAxisColor = new RgbColor(0, 0, 0),
        });

        var viewport = GetViewport(wb, sheet);

        var negativeCell = GetCell(viewport, 1, 1);
        negativeCell.ConditionalDataBar.Should().NotBeNull();
        var bar = negativeCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeTrue();
        bar.AxisFraction.Should().BeApproximately(0.5, 0.001);
        bar.StartFraction.Should().BeApproximately(0.0, 0.001, "negative min fills from left edge");
        bar.EndFraction.Should().BeApproximately(0.5, 0.001, "negative bar ends at axis");
        bar.FillColor.Should().Be(new RgbColor(255, 0, 0), "negative fill color is used for negative bars");
    }

    [Fact]
    public void DataBar_NegativeAxis_NegativeFillColorDefaultsToExcelAutomaticRedWhenNotSet()
    {
        // Excel's "automatic" negative data-bar fill is a solid red (FF0000), not the positive
        // fill color, when the rule has no explicit negativeFillColor.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            // DataBarNegativeFillColor is NOT set — should default to Excel automatic red.
        });

        var viewport = GetViewport(wb, sheet);

        var negativeCell = GetCell(viewport, 1, 1);
        negativeCell.ConditionalDataBar.Should().NotBeNull();
        var bar = negativeCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeTrue();
        bar.FillColor.Should().Be(new RgbColor(0xFF, 0x00, 0x00), "Excel automatic negative data-bar fill is red, not the positive fill color");
        bar.NegativeFillColor.Should().BeNull("no explicit negative fill color was configured on the rule");
    }

    [Fact]
    public void DataBar_NegativeAxis_ExplicitNegativeFillColorStillHonored()
    {
        // Regression guard: an explicit negativeFillColor must still win over the automatic red default.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(99, 142, 198),
            DataBarNegativeFillColor = new RgbColor(12, 34, 56),
        });

        var viewport = GetViewport(wb, sheet);

        var negativeCell = GetCell(viewport, 1, 1);
        negativeCell.ConditionalDataBar.Should().NotBeNull();
        var bar = negativeCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeTrue();
        bar.FillColor.Should().Be(new RgbColor(12, 34, 56), "an explicit negative fill color overrides the automatic red default");
    }

    [Fact]
    public void DataBar_NegativeAxis_AxisColorCarriedToDto()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));

        var axisColor = new RgbColor(1, 2, 3);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarAxisColor = axisColor,
        });

        var viewport = GetViewport(wb, sheet);

        var positiveCell = GetCell(viewport, 2, 1);
        positiveCell.ConditionalDataBar.Should().NotBeNull();
        positiveCell.ConditionalDataBar!.Value.AxisColor.Should().Be(axisColor);
    }

    [Fact]
    public void DataBar_NegativeAxis_AxisNone_AllBarsLeftAnchored()
    {
        // When axisPosition=="none", even a mixed range should use left-anchored layout.
        // Use three cells so the middle (0) is not at the min boundary and produces a bar.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(-10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(10)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarAxisPosition = "none",
        });

        var viewport = GetViewport(wb, sheet);

        // The cell with value 0 sits at the midpoint of -10..10.  With left-anchored layout it
        // should produce StartFraction=0, EndFraction≈0.5, IsNegative=false.
        var midCell = GetCell(viewport, 2, 1);
        midCell.ConditionalDataBar.Should().NotBeNull();
        var bar = midCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeFalse("axis=none disables negative-axis layout");
        bar.AxisFraction.Should().Be(0d, "axis=none means no axis");
        bar.StartFraction.Should().Be(0d, "left-anchored");
        bar.EndFraction.Should().BeApproximately(0.5, 0.001, "0 is midway between -10 and 10");
    }

    [Fact]
    public void DataBar_PositiveOnlyRange_NoAxisNoNegativeFlag()
    {
        // Regression guard: all-positive range must not set IsNegative or AxisFraction.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
        });

        var viewport = GetViewport(wb, sheet);

        var midCell = GetCell(viewport, 2, 1);
        midCell.ConditionalDataBar.Should().NotBeNull();
        var bar = midCell.ConditionalDataBar!.Value;
        bar.IsNegative.Should().BeFalse();
        bar.AxisFraction.Should().Be(0d);
        bar.StartFraction.Should().Be(0d, "left-anchored for positive-only range");
    }
}
