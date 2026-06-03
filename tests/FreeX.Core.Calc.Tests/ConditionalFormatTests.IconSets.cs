using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void IconSet_AttachesTrafficLightDisplayIconsByValueBand()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 1, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, true));
    }

    [Fact]
    public void IconSet_RespectsReverseAndIconsOnlyDisplay()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetReverse = true,
            IconSetShowValue = false
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, false));
        GetCell(vp, 1, 1).DisplayText.Should().BeEmpty();
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, false));
        GetCell(vp, 2, 1).DisplayText.Should().BeEmpty();
    }

    [Fact]
    public void IconSet_ResolvesFourAndFiveIconBandsFromExplicitThresholds()
    {
        var (wb, sheet) = MakeWorkbook();
        var fourBandValues = new[] { 0, 50, 85, 100 };
        var fiveBandValues = new[] { 10, 50, 88, 93, 100 };
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(fourBandValues[row - 1])));
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(fiveBandValues[row - 1])));

        var fourBandRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "4Arrows"
        };
        fourBandRule.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "10"),
            new CfThresholdModel(CfThresholdType.Percent, "80"),
            new CfThresholdModel(CfThresholdType.Percent, "90")
        ]);
        sheet.ConditionalFormats.Add(fourBandRule);

        var fiveBandRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 5, 2)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows"
        };
        fiveBandRule.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Number, "15"),
            new CfThresholdModel(CfThresholdType.Number, "85"),
            new CfThresholdModel(CfThresholdType.Number, "90"),
            new CfThresholdModel(CfThresholdType.Number, "95")
        ]);
        sheet.ConditionalFormats.Add(fiveBandRule);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 0, 4, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 1, 4, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 2, 4, true));
        GetCell(vp, 4, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 3, 4, true));
        GetCell(vp, 1, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 0, 5, true));
        GetCell(vp, 2, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 1, 5, true));
        GetCell(vp, 3, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 2, 5, true));
        GetCell(vp, 4, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 3, 5, true));
        GetCell(vp, 5, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 4, 5, true));
    }

    [Fact]
    public void IconSet_WithPerThresholdOverrides_AppliesCustomIconForEachBucket()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "40"),
            new CfThresholdModel(CfThresholdType.Percent, "70")
        ]);
        cf.IconOverrides.AddRange([
            new CfIconOverride("3Arrows", 0),
            new CfIconOverride("3Arrows", 1),
            new CfIconOverride("3Arrows", 2)
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Values 10, 50, 90 with thresholds at 40% (42) and 70% (66) of range [10,90]
        // → buckets 0, 1, 2 respectively
        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 0, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 1, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 2, 3, true));
    }

    [Fact]
    public void IconSet_ResolvesPercentileFormulaThresholdsAndStrictComparison()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row * 10)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "4Arrows"
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Number, "20", GreaterThanOrEqual: false),
            new CfThresholdModel(CfThresholdType.Percentile, "50"),
            new CfThresholdModel(CfThresholdType.Formula, "$A$5")
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(
            new ConditionalFormatIcon("4Arrows", 0, 4, true),
            "gte=false should keep a value equal to the first threshold in the lower bucket");
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 2, 4, true));
        GetCell(vp, 5, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 3, 4, true));
    }

    [Fact]
    public void IconSet_ShiftsRelativeFormulaThresholdsFromAppliesToAnchor()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(40)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new NumberValue(50)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Formula, "B1"),
            new CfThresholdModel(CfThresholdType.Formula, "C1")
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(
            new ConditionalFormatIcon("3TrafficLights1", 1, 3, true),
            "relative threshold formulas should shift to B2 and C2 for the second applies-to cell");
    }
}
