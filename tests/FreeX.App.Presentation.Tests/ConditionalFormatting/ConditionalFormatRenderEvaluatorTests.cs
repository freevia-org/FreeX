using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatRenderEvaluatorTests
{
    [Fact]
    public void Evaluate_OrdersApplicableRulesByPriorityThenInsertionOrder()
    {
        var sheet = CreateSheet();
        var target = At(sheet, 1, 1);
        sheet.SetCell(target, new NumberValue(10));

        sheet.ConditionalFormats.Add(CellValueRule(target, priority: 3, new CellStyle
        {
            FillColor = new CellColor(255, 0, 0),
            Italic = true,
        }));
        sheet.ConditionalFormats.Add(CellValueRule(At(sheet, 1, 2), priority: 0, new CellStyle
        {
            FillColor = new CellColor(0, 0, 0),
        }));
        sheet.ConditionalFormats.Add(CellValueRule(target, priority: 1, new CellStyle
        {
            FillColor = new CellColor(0, 255, 0),
            Bold = true,
        }));
        sheet.ConditionalFormats.Add(CellValueRule(target, priority: 1, new CellStyle
        {
            FillColor = new CellColor(0, 0, 255),
            Underline = true,
        }));

        var result = new ConditionalFormatRenderEvaluator(sheet).Evaluate(target, sheet.GetValue(target));

        result.Style.Should().NotBeNull();
        result.Style!.Value.FillColor.Should().Be(new CellColor(0, 255, 0),
            "the first inserted priority-1 rule sets the winning fill");
        result.Style.Value.Bold.Should().BeTrue();
        result.Style.Value.Italic.Should().BeTrue("lower-priority rules still contribute unset properties");
        result.Style.Value.Underline.Should().BeTrue("same-priority rules stack in insertion order");
    }

    [Fact]
    public void Evaluate_MatchingStopIfTrueSuppressesLowerPriorityRules()
    {
        var sheet = CreateSheet();
        var target = At(sheet, 1, 1);
        sheet.SetCell(target, new NumberValue(10));

        var stopRule = CellValueRule(target, priority: 1, style: null);
        stopRule.StopIfTrue = true;
        sheet.ConditionalFormats.Add(stopRule);
        sheet.ConditionalFormats.Add(CellValueRule(target, priority: 2, new CellStyle
        {
            FillColor = new CellColor(255, 0, 0),
        }));

        var result = new ConditionalFormatRenderEvaluator(sheet).Evaluate(target, sheet.GetValue(target));

        result.Should().Be(default(ConditionalFormatCellPlan),
            "a matching rule stops evaluation even when it has no differential style of its own");
    }

    [Fact]
    public void Evaluate_NonMatchingStopIfTrueAllowsLowerPriorityRules()
    {
        var sheet = CreateSheet();
        var target = At(sheet, 1, 1);
        sheet.SetCell(target, new NumberValue(10));

        var stopRule = CellValueRule(target, priority: 1, style: null, threshold: "100");
        stopRule.StopIfTrue = true;
        sheet.ConditionalFormats.Add(stopRule);
        sheet.ConditionalFormats.Add(CellValueRule(target, priority: 2, new CellStyle
        {
            FillColor = new CellColor(255, 0, 0),
        }));

        var result = new ConditionalFormatRenderEvaluator(sheet).Evaluate(target, sheet.GetValue(target));

        result.Style!.Value.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void Evaluate_StatisticsUseSparseNumericEnumerationAndDeduplicateOverlappingRanges()
    {
        var sheet = CreateSheet();
        var first = At(sheet, 1, 1);
        var duplicated = At(sheet, 2, 1);
        var target = At(sheet, 10_001, 1);
        sheet.SetCell(first, new DateTimeValue(1));
        sheet.SetCell(duplicated, new NumberValue(10));
        sheet.SetCell(target, new NumberValue(6));

        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            AppliesTo = new GridRange(first, target),
            AdditionalRanges = [new GridRange(duplicated, duplicated)],
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 192, 0) },
        };
        sheet.ConditionalFormats.Add(rule);

        var result = new ConditionalFormatRenderEvaluator(sheet).Evaluate(target, sheet.GetValue(target));

        result.Style!.Value.FillColor.Should().Be(new CellColor(255, 192, 0),
            "the sparse scan should include date serials and count an overlapping cell only once");
    }

    [Fact]
    public void Evaluate_ProducesColorScaleDataBarAndIconSetPlans()
    {
        var sheet = CreateSheet();
        var first = At(sheet, 1, 1);
        var target = At(sheet, 2, 1);
        var last = At(sheet, 3, 1);
        sheet.SetCell(first, new NumberValue(0));
        sheet.SetCell(target, new NumberValue(50));
        sheet.SetCell(last, new NumberValue(100));
        var range = new GridRange(first, last);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            Priority = 1,
            AppliesTo = range,
            MinColor = new RgbColor(0, 0, 0),
            MaxColor = new RgbColor(255, 255, 255),
            MinThresholdType = CfThresholdType.Min,
            MaxThresholdType = CfThresholdType.Max,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            Priority = 2,
            AppliesTo = range,
            DataBarMinThresholdType = CfThresholdType.AutoMin,
            DataBarMaxThresholdType = CfThresholdType.AutoMax,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            Priority = 3,
            AppliesTo = range,
            IconSetStyle = "3TrafficLights1",
        });

        var result = new ConditionalFormatRenderEvaluator(sheet).Evaluate(target, sheet.GetValue(target));

        result.Style!.Value.FillColor.Should().Be(new CellColor(128, 128, 128));
        result.DataBar.Should().NotBeNull();
        result.DataBar!.Value.EndFraction.Should().BeApproximately(0.5, 1e-9);
        result.IconSet.Should().NotBeNull();
        result.IconSet!.Value.Style.Should().Be("3TrafficLights1");
        result.IconSet.Value.BucketIndex.Should().Be(1);
    }

    private static ConditionalFormat CellValueRule(
        CellAddress address,
        int priority,
        CellStyle? style,
        string threshold = "0") =>
        new()
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = threshold,
            Priority = priority,
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = style,
        };

    private static CellAddress At(Sheet sheet, uint row, uint column) => new(sheet.Id, row, column);

    private static Sheet CreateSheet()
    {
        var workbook = new Workbook();
        return workbook.AddSheet("Sheet1");
    }
}
