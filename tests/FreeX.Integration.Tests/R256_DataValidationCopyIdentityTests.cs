using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r256: the two validation-COPY commands, and the identity question underneath them.
/// <para>
/// Copying a rule mints a fresh <c>Id</c> on every copy (<c>CloneWithNewIdentity</c>), so running
/// Format Painter or Paste Validation twice with the same source and target produces a rule list
/// identical in every member EXCEPT Id. That churn is what r221 recorded as the reason these two
/// could not be decided.
/// </para>
/// <para>
/// The decision recorded here is to KEEP minting -- see the note on
/// <c>DataValidation.CloneWithNewIdentity</c> -- and the collision tests below are the evidence for
/// it: a copy that kept its source's Id would sit alongside the source in the SAME
/// <c>Sheet.DataValidations</c> list, where <c>SetDataValidationCommand</c> resolves a rule by Id
/// and would edit the wrong one.
/// </para>
/// </summary>
public sealed class R256_DataValidationCopyIdentityTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint endRow) =>
        new(new CellAddress(sheet.Id, startRow, 1), new CellAddress(sheet.Id, endRow, 1));

    private static DataValidation SeedRule(Sheet sheet)
    {
        var rule = new DataValidation
        {
            AppliesTo = Range(sheet, 1, 2),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "10",
        };
        sheet.DataValidations.Add(rule);
        return rule;
    }

    // ---- the identity question itself -------------------------------------------------

    [Fact]
    public void CopyingARule_MintsAFreshIdentity_SoItCannotCollideWithItsSource()
    {
        var (sheet, ctx) = Fixture();
        var source = SeedRule(sheet);

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 5))
            .Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Select(rule => rule.Id).Should().OnlyHaveUniqueItems(
            "a copy that kept its source's Id would sit in the same list under the same identity");
        sheet.DataValidations[1].SameAs(source, ignoreIdentity: true).Should().BeFalse(
            "the copy covers the painted range, not the source range");
    }

    [Fact]
    public void EditingTheCopiedRule_LeavesTheSourceRuleAlone()
    {
        // The concrete cost of a shared Id: SetDataValidationCommand resolves the rule to replace by
        // Id (FindDataValidationReplacement/FindDataValidationIndex) and takes the FIRST match, so a
        // copy carrying its source's Id would send an edit of the copy into the source rule instead.
        var (sheet, ctx) = Fixture();
        var source = SeedRule(sheet);

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 5))
            .Apply(ctx);
        var copy = sheet.DataValidations[1];

        var edited = copy.CloneForRanges(copy.AppliesTo, copy.AdditionalRanges, copy.Id);
        edited.Formula1 = "99";
        new SetDataValidationCommand(sheet.Id, edited).Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Single(rule => rule.AppliesTo == Range(sheet, 5, 5))
            .Formula1.Should().Be("99");
        sheet.DataValidations.Single(rule => rule.AppliesTo == source.AppliesTo)
            .Formula1.Should().Be("10", "the source rule was not the one edited");
    }

    // ---- Format Painter no-op ---------------------------------------------------------

    [Fact]
    public void RepaintingTheSameValidationOntoTheSameTarget_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 6))
            .Apply(ctx).IsNoOp.Should().BeFalse();

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 6))
            .Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PaintingOntoADifferentTarget_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 6)).Apply(ctx);

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 8, 9))
            .Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void PaintingADifferentRuleOverTheSameTarget_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 3, 3),
            Type = DvType.WholeNumber,
            Operator = DvOperator.LessThan,
            Formula1 = "4",
        });

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 5)).Apply(ctx);

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 3, 3), Range(sheet, 5, 5))
            .Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ANoOpRepaint_LeavesTheRuleListExactlyAsItWas()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);
        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 6)).Apply(ctx);
        var before = sheet.DataValidations.ToList();

        new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 6))
            .Apply(ctx).IsNoOp.Should().BeTrue();

        sheet.DataValidations.Should().Equal(before,
            "a command that reports IsNoOp is never pushed, so it must not have left the sheet "
            + "holding different rule instances -- an Id churn nothing can undo");
    }

    // ---- Paste Validation no-op -------------------------------------------------------

    [Fact]
    public void RepastingTheSameValidationOntoTheSameDestination_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);

        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 5, 1), transpose: false)
            .Apply(ctx).IsNoOp.Should().BeFalse();

        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 5, 1), transpose: false)
            .Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void PastingOntoADifferentDestination_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);

        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 5, 1), transpose: false).Apply(ctx);

        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 9, 1), transpose: false)
            .Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void PastingOverAnExistingDifferentRule_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 5, 6),
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "3",
        });

        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 5, 1), transpose: false)
            .Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ANoOpRepaste_LeavesTheRuleListExactlyAsItWas()
    {
        var (sheet, ctx) = Fixture();
        SeedRule(sheet);
        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 5, 1), transpose: false).Apply(ctx);
        var before = sheet.DataValidations.ToList();

        new PasteDataValidationCommand(
            sheet.Id, Range(sheet, 1, 2), new CellAddress(sheet.Id, 5, 1), transpose: false)
            .Apply(ctx).IsNoOp.Should().BeTrue();

        sheet.DataValidations.Should().Equal(before);
    }

    // ---- undo keeps identity ----------------------------------------------------------

    [Fact]
    public void RevertingAPaint_RestoresTheRulesUnderTheirOriginalIdentities()
    {
        var (sheet, ctx) = Fixture();
        var source = SeedRule(sheet);

        var command = new FormatPainterDataValidationCommand(sheet.Id, Range(sheet, 1, 1), Range(sheet, 5, 5));
        command.Apply(ctx);
        command.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle()
            .Which.Id.Should().Be(source.Id,
                "undo restores a snapshot, and a snapshot that re-mints identities makes undo an "
                + "edit of its own -- SetDataValidationCommand resolves rules by Id");
    }
}
