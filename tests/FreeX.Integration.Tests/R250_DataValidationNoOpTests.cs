using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r250: SetDataValidationCommand. The Data Validation dialog pre-fills the rule being edited, so
/// pressing OK without changing anything replaces a rule with an equal one.
/// <para>
/// r242 looked at this family and put it back on the debt because <c>DataValidation</c> is a plain
/// class with reference equality and the snapshot is built with a clone -- so any comparison written
/// with <c>Equals</c> would never fire. The remedy is the r249 shape: a content comparison whose
/// coverage contract derives its field list from the type's own <c>CloneForRanges</c>.
/// </para>
/// </summary>
public sealed class R250_DataValidationNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static DataValidation Rule(Sheet sheet, string formula1) =>
        new()
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = formula1,
        };

    [Fact]
    public void ReSubmittingAnIdenticalRule_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var rule = Rule(sheet, "10");
        new SetDataValidationCommand(sheet.Id, rule).Apply(ctx).Success.Should().BeTrue();

        var identical = rule.CloneForRanges(rule.AppliesTo, rule.AdditionalRanges, rule.Id);

        new SetDataValidationCommand(sheet.Id, identical).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void EditingTheCriteria_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var rule = Rule(sheet, "10");
        new SetDataValidationCommand(sheet.Id, rule).Apply(ctx);

        var edited = rule.CloneForRanges(rule.AppliesTo, rule.AdditionalRanges, rule.Id);
        edited.Formula1 = "20";

        new SetDataValidationCommand(sheet.Id, edited).Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void EditingOnlyTheErrorMessage_DoesNotReportNoOp()
    {
        // A member far from the criteria, and one a comparison focused on "the rule" might skip.
        var (sheet, ctx) = Fixture();
        var rule = Rule(sheet, "10");
        new SetDataValidationCommand(sheet.Id, rule).Apply(ctx);

        var edited = rule.CloneForRanges(rule.AppliesTo, rule.AdditionalRanges, rule.Id);
        edited.ErrorMessage = "Please enter a number above ten.";

        new SetDataValidationCommand(sheet.Id, edited).Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void AddingANewRule_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetDataValidationCommand(sheet.Id, Rule(sheet, "10")).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }
}
