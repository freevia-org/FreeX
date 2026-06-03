using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void CloneWithPriority_PreservesAdvancedConditionalFormatFields()
    {
        var sourceSheet = SheetId.New();
        var source = new ConditionalFormat
        {
            Id = Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sourceSheet, 2, 2), new CellAddress(sourceSheet, 5, 4)),
            Priority = 7,
            RuleType = CfRuleType.IconSet,
            Operator = CfOperator.Between,
            Value1 = "1",
            Value2 = "10",
            FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) },
            MinColor = new RgbColor(10, 20, 30),
            MidColor = new RgbColor(40, 50, 60),
            MaxColor = new RgbColor(70, 80, 90),
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "5",
            MinThresholdGreaterThanOrEqual = false,
            MidThresholdType = CfThresholdType.Percent,
            MidThresholdValue = "50",
            MidThresholdGreaterThanOrEqual = true,
            MaxThresholdType = CfThresholdType.Formula,
            MaxThresholdValue = "A1",
            MaxThresholdGreaterThanOrEqual = false,
            DataBarColor = new RgbColor(9, 8, 7),
            DataBarMinThresholdType = CfThresholdType.Percentile,
            DataBarMinThresholdValue = "10",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "99",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            AboveAverage = false,
            FormulaText = "A1>0",
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true,
            TopBottomRank = 3,
            TopBottomPercent = true,
            TextRuleText = "urgent",
            DateOccurringPeriod = "last7Days",
            StopIfTrue = true,
            NativeAttributes = new Dictionary<string, string> { ["nativeAttr"] = "x" },
            NativeChildXmls = ["<extLst />"],
            NativePayloadAttributes = new Dictionary<string, string> { ["payloadAttr"] = "y" },
            NativePayloadChildXmls = ["<axisColor theme=\"1\" />"],
            NativeContainerAttributes = new Dictionary<string, string> { ["containerAttr"] = "z" },
            NativeContainerChildXmls = ["<extLst />"]
        };

        var clone = CloneWithPriority(source, 2);

        clone.Priority.Should().Be(2);
        clone.Id.Should().Be(source.Id);
        clone.Should().BeEquivalentTo(source, options => options
            .Excluding(rule => rule.Priority)
            .Excluding(rule => rule.FormatIfTrue));
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.FormatIfTrue.Should().Be(source.FormatIfTrue);
    }

    [Fact]
    public void CloneWithPriority_WithNewId_DropsExistingX14IdNativeChild()
    {
        var sourceSheet = SheetId.New();
        var source = new ConditionalFormat
        {
            Id = Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sourceSheet, 1, 1), new CellAddress(sourceSheet, 5, 1)),
            RuleType = CfRuleType.DataBar,
            NativeChildXmls =
            [
                """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext><ext uri="{FUTURE}" /></extLst>""",
                """<future xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />"""
            ],
            NativePayloadChildXmls = ["""<axisColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="1" />"""]
        };
        var newId = Guid.NewGuid();

        var clone = CloneWithPriority(source, 2, newId);

        clone.Id.Should().Be(newId);
        clone.NativeChildXmls.Should().HaveCount(2);
        clone.NativeChildXmls.Should().Contain(xml => xml.Contains("{FUTURE}", StringComparison.Ordinal));
        clone.NativeChildXmls.Should().Contain(xml => xml.Contains("future", StringComparison.Ordinal));
        clone.NativeChildXmls.Should().NotContain(xml => xml.Contains("11111111-2222-3333-4444-555555555555", StringComparison.Ordinal));
        clone.NativePayloadChildXmls.Should().BeEquivalentTo(source.NativePayloadChildXmls);
    }
}
