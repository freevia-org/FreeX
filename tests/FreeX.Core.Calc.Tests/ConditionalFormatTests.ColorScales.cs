using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void ColorScale_LargeSparseRange_UsesOccupiedCellsForAggregates()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(0, 255, 0),
            MaxColor = new RgbColor(255, 0, 0),
            UseThreeColorScale = false
        });

        var viewport = GetViewport(wb, sheet);

        GetCell(viewport, 1, 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0));
    }

    [Fact]
    public void ColorScale_InterpolatesColorForMidRangeValue()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            Priority  = 1,
            RuleType  = CfRuleType.ColorScale,
            MinColor  = new RgbColor(0, 255, 0),    // green
            MaxColor  = new RgbColor(255, 0, 0),    // red
            UseThreeColorScale = false
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert: mid-range cell (50 out of 0–100) should have roughly yellow (~128, ~128, 0)
        var a2 = GetCell(vp, 2, 1);
        a2.Style!.FillColor.Should().NotBeNull("color scale should set a fill");
        var fill = a2.Style!.FillColor!.Value;
        // Interpolation: R = 0 + 0.5*(255-0) = 127, G = 255 + 0.5*(0-255) = 127, B = 0
        fill.R.Should().BeCloseTo(127, 2, "R interpolated from 0→255 at t=0.5");
        fill.G.Should().BeCloseTo(127, 2, "G interpolated from 255→0 at t=0.5");
    }

    [Fact]
    public void ColorScale_ResolvesFormulaMidpointThreshold()
    {
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
    public void ColorScale_ShiftsRelativeFormulaThresholdsFromAppliesToAnchor()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(25)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(100)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinThresholdType = CfThresholdType.Formula,
            MinThresholdValue = "B1",
            MaxThresholdType = CfThresholdType.Formula,
            MaxThresholdValue = "C1",
            MinColor = new RgbColor(0, 0, 255),
            MaxColor = new RgbColor(255, 0, 0)
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(
            new CellColor(0, 0, 255),
            "relative threshold formulas should shift to B2 and C2 for the second applies-to cell");
    }
}
