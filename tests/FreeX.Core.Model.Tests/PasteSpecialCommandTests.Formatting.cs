using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteColumnWidthsCommand_CopiesWidthsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 24;
        sheet.ColumnWidths[5] = 9;

        var command = new PasteColumnWidthsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            destinationStartCol: 5);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnWidths[5].Should().Be(18);
        sheet.ColumnWidths[6].Should().Be(24);

        command.Revert(ctx);

        sheet.ColumnWidths[5].Should().Be(9);
        sheet.ColumnWidths.Should().NotContainKey(6);
    }

    [Fact]
    public void PasteColumnWidthsCommand_CopiesWidthsAcrossSheets()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        sourceSheet.ColumnWidths[1] = 18;
        sourceSheet.ColumnWidths[2] = 24;
        targetSheet.ColumnWidths[5] = 9;

        var command = new PasteColumnWidthsCommand(
            targetSheet.Id,
            new GridRange(new CellAddress(sourceSheet.Id, 1, 1), new CellAddress(sourceSheet.Id, 3, 2)),
            destinationStartCol: 5);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.ColumnWidths[5].Should().Be(18);
        targetSheet.ColumnWidths[6].Should().Be(24);

        command.Revert(ctx);

        targetSheet.ColumnWidths[5].Should().Be(9);
        targetSheet.ColumnWidths.Should().NotContainKey(6);
    }

    [Fact]
    public void PasteColumnWidthsCommand_RejectsProtectedSheetWithoutFormatColumnsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ColumnWidths[1] = 18;
        sheet.IsProtected = true;

        var outcome = new PasteColumnWidthsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            destinationStartCol: 5).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ColumnWidths.Should().NotContainKey(5);
    }

    [Fact]
    public void PasteColumnWidthsCommand_AllowsProtectedSheetWithFormatColumnsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ColumnWidths[1] = 18;
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatColumns);

        var outcome = new PasteColumnWidthsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            destinationStartCol: 5).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ColumnWidths[5].Should().Be(18);
    }

    [Fact]
    public void PasteConditionalFormatsCommand_RejectsProtectedSheetWithoutFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = sourceRange, RuleType = CfRuleType.CellValue });
        sheet.IsProtected = true;

        var outcome = new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 5, 5), transpose: false)
            .Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ConditionalFormats.Should().ContainSingle();
    }

    [Fact]
    public void PasteConditionalFormatsCommand_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = sourceRange, RuleType = CfRuleType.CellValue });
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);

        var outcome = new PasteConditionalFormatsCommand(sheet.Id, sourceRange, new CellAddress(sheet.Id, 5, 5), transpose: false)
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().HaveCount(2);
        sheet.ConditionalFormats.Should().Contain(rule => rule.AppliesTo.Start.Row == 5 && rule.AppliesTo.Start.Col == 5);
    }

}
