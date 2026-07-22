using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R68-render-conditional-format-6-2: a 3-color scale's resolved midpoint that happens to land
// exactly on min (or max) — e.g. a skewed dataset {1,1,1,1,10} whose percentile-50 is 1, same as the
// dataset's min — used to null out `mid` entirely (the old `resolvedMid > min && resolvedMid < max`
// guard), collapsing the WHOLE range to a plain Min->Max lerp and silently erasing MidColor for
// EVERY value, not just the degenerate point itself. The fix clamps the resolved midpoint into
// [min,max] and keeps the 3-stop interpolation path, so values above a degenerate min-midpoint still
// blend Mid->Max.
public partial class ConditionalFormatTests
{
    [Fact]
    public void ColorScale_DegenerateMidpointEqualToMin_StillBlendsMidToMaxAboveIt()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), Cell.FromValue(new NumberValue(5)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50", // percentile-50 of {1,1,1,1,10,5} lands on/near the low end
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(248, 105, 107),  // red
            MidColor = new RgbColor(255, 235, 132),  // yellow
            MaxColor = new RgbColor(99, 190, 123),   // green
        });

        var vp = GetViewport(wb, sheet);

        // min=1, mid resolves to 1 (== min, the degenerate case), max=10. Value 5 must blend
        // Mid(yellow)->Max(green) since it sits strictly above the degenerate min/mid point — it
        // must NOT be a plain Min(red)->Max(green) blend, which would ignore MidColor entirely.
        var value5Cell = GetCell(vp, 6, 1);
        value5Cell.Style!.FillColor.Should().NotBeNull();
        var fill = value5Cell.Style!.FillColor!.Value;

        // Min->Max direct lerp at t=(5-1)/(10-1)=4/9 would give R=248+4/9*(99-248)≈182, G=105+4/9*(190-105)≈143.
        // Mid->Max lerp at t=(5-1)/(10-1)=4/9 (mid==min here) gives R=255+4/9*(99-255)≈186, G=235+4/9*(190-235)≈215.
        // The distinguishing channel is G: ~143 (wrong, Min->Max) vs ~215 (correct, Mid->Max).
        fill.G.Should().BeGreaterThan(180, "value 5 must blend from MidColor (yellow, G=235) toward MaxColor (green, G=190), not from MinColor (red, G=105)");
    }

    [Fact]
    public void ColorScale_DegenerateMidpointEqualToMin_ValueAtDegeneratePoint_UsesMidColor()
    {
        // The degenerate point itself (cellValue == min == mid) is the single-pixel edge case: it
        // must render as MidColor exactly (no Min<->Mid segment exists to blend across).
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Number,
            MidThresholdValue = "1", // explicitly pinned to == min
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(248, 105, 107),
            MidColor = new RgbColor(255, 235, 132),
            MaxColor = new RgbColor(99, 190, 123),
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132), "the degenerate min==mid point renders as MidColor exactly");
    }

    [Fact]
    public void ColorScale_NormalNonDegenerateThreeStop_Unchanged_NoRegression()
    {
        // Sibling/no-regression: an ordinary 3-color scale where mid is strictly between min and
        // max must keep interpolating exactly as before.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Formula,
            MidThresholdValue = "$A$2",
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(0, 0, 255),
            MidColor = new RgbColor(255, 255, 255),
            MaxColor = new RgbColor(255, 0, 0)
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 255, 255));
    }

    [Fact]
    public void ColorScale_TwoColorScale_Unaffected_ByDegenerateMidpointFix()
    {
        // Sibling/no-regression: a 2-color scale (UseThreeColorScale == false) never resolves a
        // mid at all and must keep interpolating Min->Max directly, unaffected by the fix.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(0, 255, 0),
            MaxColor = new RgbColor(255, 0, 0),
            UseThreeColorScale = false
        });

        var vp = GetViewport(wb, sheet);

        var fill = GetCell(vp, 2, 1).Style!.FillColor!.Value;
        fill.R.Should().BeCloseTo(127, 2);
        fill.G.Should().BeCloseTo(127, 2);
    }
}
