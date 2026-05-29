using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DataBarConditionalFormatCloneTests
{
    [Fact]
    public void PasteConditionalFormatsCommand_PreservesDataBarOptions()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(CreateAdvancedDataBar(sheet.Id));
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 10, 3), transpose: false)
            .Apply(new SimpleContext(workbook));

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        AssertAdvancedDataBar(pasted);
        pasted.DataBarGradient.Should().BeFalse();
    }

    [Fact]
    public void PasteConditionalFormatsCommand_PreservesColorScaleGteThresholds()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "0",
            MinThresholdGreaterThanOrEqual = false,
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50",
            MidThresholdGreaterThanOrEqual = true,
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "100",
            MaxThresholdGreaterThanOrEqual = false
        });
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 10, 3), transpose: false)
            .Apply(new SimpleContext(workbook));

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        pasted.MinThresholdGreaterThanOrEqual.Should().BeFalse();
        pasted.MidThresholdGreaterThanOrEqual.Should().BeTrue();
        pasted.MaxThresholdGreaterThanOrEqual.Should().BeFalse();
    }

    [Fact]
    public void PasteConditionalFormatsCommand_PreservesIconSetGteThresholds()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        source.IconSetThresholds.AddRange(
        [
            new CfThresholdModel(CfThresholdType.Number, "0", GreaterThanOrEqual: false),
            new CfThresholdModel(CfThresholdType.Percentile, "50", GreaterThanOrEqual: true)
        ]);
        sheet.ConditionalFormats.Add(source);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 10, 3), transpose: false)
            .Apply(new SimpleContext(workbook));

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        pasted.IconSetThresholds.Should().Equal(
            new CfThresholdModel(CfThresholdType.Number, "0", GreaterThanOrEqual: false),
            new CfThresholdModel(CfThresholdType.Percentile, "50", GreaterThanOrEqual: true));
    }

    [Fact]
    public void PasteConditionalFormatsCommand_DropsExistingX14IdNativeChild()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var source = CreateAdvancedDataBar(sheet.Id);
        source.NativeChildXmls =
        [
            """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext><ext uri="{FUTURE}" /></extLst>""",
            """<future xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />"""
        ];
        source.NativePayloadChildXmls = ["""<axisColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="1" />"""];
        sheet.ConditionalFormats.Add(source);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 10, 3), transpose: false)
            .Apply(new SimpleContext(workbook));

        var pasted = sheet.ConditionalFormats.Should().HaveCount(2).And.Subject.Last();
        pasted.NativeChildXmls.Should().HaveCount(2);
        pasted.NativeChildXmls.Should().Contain(xml => xml.Contains("{FUTURE}", StringComparison.Ordinal));
        pasted.NativeChildXmls.Should().Contain(xml => xml.Contains("future", StringComparison.Ordinal));
        pasted.NativeChildXmls.Should().NotContain(xml => xml.Contains("11111111-2222-3333-4444-555555555555", StringComparison.Ordinal));
        pasted.NativePayloadChildXmls.Should().BeEquivalentTo(source.NativePayloadChildXmls);
    }

    private static ConditionalFormat CreateAdvancedDataBar(SheetId sheetId) =>
        new()
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(10, 20, 30),
            DataBarShowValue = false,
            DataBarMinLength = 7,
            DataBarMaxLength = 88,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(1, 2, 3),
            DataBarNegativeFillColor = new RgbColor(4, 5, 6),
            DataBarNegativeBorderColor = new RgbColor(7, 8, 9)
        };

    private static void AssertAdvancedDataBar(ConditionalFormat rule)
    {
        rule.DataBarBorder.Should().BeTrue();
        rule.DataBarAxisPosition.Should().Be("middle");
        rule.DataBarAxisColor.Should().Be(new RgbColor(1, 2, 3));
        rule.DataBarNegativeFillColor.Should().Be(new RgbColor(4, 5, 6));
        rule.DataBarNegativeBorderColor.Should().Be(new RgbColor(7, 8, 9));
    }

    private sealed class SimpleContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
