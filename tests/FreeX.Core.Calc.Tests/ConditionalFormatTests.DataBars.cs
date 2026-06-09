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
}
