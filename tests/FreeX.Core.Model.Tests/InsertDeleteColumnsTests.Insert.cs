using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    [Fact]
    public void InsertColumn_ShiftsCellsRight()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));

        new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1).Apply(ctx);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(100));
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void InsertColumnRevert_RestoresOriginalState()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(100));
        sheet.GetCell(1, 4).Should().BeNull();
    }

    [Fact]
    public void InsertColumn_ShiftsCustomColumnWidthsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[3] = 15;
        sheet.ColumnWidths[5] = 25;

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnWidths.Should().NotContainKey(3);
        sheet.ColumnWidths.Should().NotContainKey(4);
        sheet.ColumnWidths[5].Should().Be(15);
        sheet.ColumnWidths[7].Should().Be(25);

        cmd.Revert(ctx);

        sheet.ColumnWidths[3].Should().Be(15);
        sheet.ColumnWidths[5].Should().Be(25);
        sheet.ColumnWidths.Should().NotContainKey(7);
    }

    [Fact]
    public void InsertColumn_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 2, 3);
        var shifted = new CellAddress(sheet.Id, 2, 5);
        sheet.Comments[original] = "Check this";

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(original);
        sheet.Comments[shifted].Should().Be("Check this");

        cmd.Revert(ctx);

        sheet.Comments[original].Should().Be("Check this");
        sheet.Comments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertColumn_ShiftsThreadedCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 2, 3);
        var shifted = new CellAddress(sheet.Id, 2, 5);
        sheet.ThreadedComments[original] = new ThreadedComment("Check this", "Quinn");

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ThreadedComments.Should().NotContainKey(original);
        sheet.ThreadedComments[shifted].Should().Be(new ThreadedComment("Check this", "Quinn"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[original].Should().Be(new ThreadedComment("Check this", "Quinn"));
        sheet.ThreadedComments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertColumn_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 2, 6)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Col.Should().Be(7);
        validation.AppliesTo.End.Col.Should().Be(8);
        format.AppliesTo.Start.Col.Should().Be(7);
        format.AppliesTo.End.Col.Should().Be(8);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Col.Should().Be(5);
        validation.AppliesTo.End.Col.Should().Be(6);
        format.AppliesTo.Start.Col.Should().Be(5);
        format.AppliesTo.End.Col.Should().Be(6);
    }

    [Fact]
    public void InsertColumn_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 1, 6)));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(7);
        wb.NamedRanges["Sales"].End.Col.Should().Be(8);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(5);
        wb.NamedRanges["Sales"].End.Col.Should().Be(6);
    }

    [Fact]
    public void InsertColumn_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 3, 6));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(7);
        sheet.PrintArea.Value.End.Col.Should().Be(8);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(5);
        sheet.PrintArea.Value.End.Col.Should().Be(6);
    }

    [Fact]
    public void InsertColumn_ShiftsColumnPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnPageBreaks.Add(3);
        sheet.ColumnPageBreaks.Add(8);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnPageBreaks.Should().Equal(5u, 10u);

        cmd.Revert(ctx);

        sheet.ColumnPageBreaks.Should().Equal(3u, 8u);
    }
}
