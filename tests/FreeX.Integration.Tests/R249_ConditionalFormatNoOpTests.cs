using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r249: ApplyConditionalFormatCommand. The Conditional Formatting rules dialog pre-fills the rule
/// being edited, so pressing OK without changing anything replaces a rule with an equal one.
/// <para>
/// The comparison is <c>ConditionalFormat.SameAs</c>, and its coverage contract takes the field list
/// from <c>Clone</c> rather than from reflection or from me -- Clone has to enumerate every member
/// correctly or cloning loses data, so it is a source of truth that is maintained for an unrelated
/// reason. That contract caught two members missing from my generated comparison on its first run.
/// </para>
/// </summary>
public sealed class R249_ConditionalFormatNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static ConditionalFormat Rule(Sheet sheet, string value1) =>
        new()
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = value1,
        };

    [Fact]
    public void ReSubmittingAnIdenticalRule_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var rule = Rule(sheet, "10");
        new ApplyConditionalFormatCommand(sheet.Id, rule).Apply(ctx).Success.Should().BeTrue();

        var identical = rule.Clone();

        new ApplyConditionalFormatCommand(sheet.Id, identical).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void EditingTheRulesThreshold_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var rule = Rule(sheet, "10");
        new ApplyConditionalFormatCommand(sheet.Id, rule).Apply(ctx);

        var edited = rule.Clone();
        edited.Value1 = "20";

        var outcome = new ApplyConditionalFormatCommand(sheet.Id, edited).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ConditionalFormats[0].Value1.Should().Be("20");
    }

    [Fact]
    public void AddingANewRule_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ApplyConditionalFormatCommand(sheet.Id, Rule(sheet, "10")).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void EditingOnlyValue2_DoesNotReportNoOp()
    {
        // Value2 is one of the two members the coverage contract caught missing from the generated
        // comparison. Without it, changing the upper bound of a between-rule would have been
        // reported as no change.
        var (sheet, ctx) = Fixture();
        var rule = Rule(sheet, "10");
        rule.Operator = CfOperator.Between;
        rule.Value2 = "20";
        new ApplyConditionalFormatCommand(sheet.Id, rule).Apply(ctx);

        var edited = rule.Clone();
        edited.Value2 = "30";

        new ApplyConditionalFormatCommand(sheet.Id, edited).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }
}
