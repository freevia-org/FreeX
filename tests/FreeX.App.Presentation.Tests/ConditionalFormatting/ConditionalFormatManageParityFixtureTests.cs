using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatManageParityFixtureTests
{
    [Fact]
    public void CreatesTheAuthorityStateAtTheExactDialogSelection()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");

        var range = ConditionalFormatManageParityFixture.CreateRange(sheet.Id);
        var rules = ConditionalFormatManageParityFixture.CreateRules(sheet.Id);

        range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 6, 3)));
        rules.Should().HaveCount(2);
        rules.Select(rule => rule.RuleType).Should().Equal(CfRuleType.CellValue, CfRuleType.DataBar);
        rules[0].AppliesTo.Should().Be(range);
        rules[0].Operator.Should().Be(CfOperator.GreaterThanOrEqual);
        rules[0].Value1.Should().Be("1600");
        rules[0].FormatIfTrue.Should().NotBeNull();
        rules[0].FormatIfTrue!.Bold.Should().BeTrue();
        rules[1].AppliesTo.Should().Be(range);
        rules[1].DataBarColor.Should().Be(new RgbColor(99, 142, 198));
        rules[1].DataBarGradient.Should().BeFalse();
        rules[1].DataBarShowValue.Should().BeTrue();
        ConditionalFormatManageParityFixture.DialogWidth.Should().Be(560);
        ConditionalFormatManageParityFixture.DialogHeight.Should().Be(420);
    }
}
