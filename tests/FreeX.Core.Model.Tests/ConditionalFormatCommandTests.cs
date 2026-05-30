using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class ConditionalFormatCommandTests
{
    [Fact]
    public void ApplyConditionalFormatCommand_AddsRuleAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var rule = NewRule(sheet.Id);

        var command = new ApplyConditionalFormatCommand(sheet.Id, rule);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(rule);

        command.Revert(ctx);

        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ApplyConditionalFormatCommand_RejectsInvalidRuleChoices()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var original = NewRule(sheet.Id);
        sheet.ConditionalFormats.Add(original);

        var invalidType = NewRule(sheet.Id);
        invalidType.RuleType = (CfRuleType)99;
        var invalidOperator = NewRule(sheet.Id);
        invalidOperator.Operator = (CfOperator)99;

        foreach (var invalidRule in new[] { invalidType, invalidOperator })
        {
            var outcome = new ApplyConditionalFormatCommand(sheet.Id, invalidRule).Apply(ctx);

            outcome.Success.Should().BeFalse();
            sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(original);
        }
    }

    [Fact]
    public void ApplyConditionalFormatCommand_RejectsRuleRangeOnAnotherSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var other = wb.AddSheet("Other");
        var ctx = new SimpleCtx(wb);

        var outcome = new ApplyConditionalFormatCommand(sheet.Id, NewRule(other.Id)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ApplyConditionalFormatCommand_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        var ctx = new SimpleCtx(wb);
        var rule = NewRule(sheet.Id);

        var outcome = new ApplyConditionalFormatCommand(sheet.Id, rule).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(rule);
    }

    [Fact]
    public void ReplaceAllConditionalFormatsCommand_RejectsInvalidRulesBeforeClearingExistingRules()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var original = NewRule(sheet.Id);
        sheet.ConditionalFormats.Add(original);
        var invalid = NewRule(sheet.Id);
        invalid.RuleType = (CfRuleType)99;

        var outcome = new ReplaceAllConditionalFormatsCommand(sheet.Id, [invalid]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(original);
    }

    [Fact]
    public void ReplaceAllConditionalFormatsCommand_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        var ctx = new SimpleCtx(wb);
        var replacement = NewRule(sheet.Id);

        var outcome = new ReplaceAllConditionalFormatsCommand(sheet.Id, [replacement]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().ContainSingle().Which.Should().BeSameAs(replacement);
    }

    [Fact]
    public void ReplaceAllConditionalFormatsCommand_AppliesManagerEditDeleteReorderAsOneUndoStep()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var first = NewRule(sheet.Id);
        var second = NewRule(sheet.Id);
        var third = NewRule(sheet.Id);
        first.Priority = 1;
        second.Priority = 2;
        third.Priority = 3;
        sheet.ConditionalFormats.AddRange([first, second, third]);

        var editedThird = new ConditionalFormat
        {
            Id = third.Id,
            AppliesTo = third.AppliesTo,
            Priority = 1,
            RuleType = third.RuleType,
            Operator = third.Operator,
            Value1 = "25",
            FormatIfTrue = new CellStyle { Italic = true },
            StopIfTrue = true
        };
        var command = new ReplaceAllConditionalFormatsCommand(sheet.Id, [editedThird, first]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Select(rule => rule.Id).Should().Equal(third.Id, first.Id);
        sheet.ConditionalFormats[0].Value1.Should().Be("25");
        sheet.ConditionalFormats[0].StopIfTrue.Should().BeTrue();

        command.Revert(ctx);

        sheet.ConditionalFormats.Select(rule => rule.Id).Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void ClearConditionalFormats_RuleStartsOutsideClearRange_ButOverlaps_IsRemoved()
    {
        // Rule applies to A1:Z100; clear B2:Y50 — start (A1) is outside but range overlaps
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 100, 26)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(rule);

        var clearRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 50, 25));
        new ClearConditionalFormatsCommand(sheet.Id, clearRange).Apply(ctx);

        sheet.ConditionalFormats.Should().BeEmpty();
    }

    [Fact]
    public void ClearConditionalFormatsCommand_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        var ctx = new SimpleCtx(wb);
        sheet.ConditionalFormats.Add(NewRule(sheet.Id));

        var outcome = new ClearConditionalFormatsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1))).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    private static ConditionalFormat NewRule(SheetId sheetId) =>
        new()
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true }
        };

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
