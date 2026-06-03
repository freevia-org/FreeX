using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    [Fact]
    public void DeleteColumn_RemovesAndShiftsLeft()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1).Apply(ctx);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(30));
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void DeleteColumnRevert_RestoresCells()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void DeleteColumn_ShiftsCustomColumnWidthsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[2] = 12;
        sheet.ColumnWidths[4] = 24;
        sheet.ColumnWidths[6] = 36;

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnWidths[2].Should().Be(12);
        sheet.ColumnWidths[4].Should().Be(36);
        sheet.ColumnWidths.Should().NotContainKey(3);
        sheet.ColumnWidths.Should().NotContainKey(6);

        cmd.Revert(ctx);

        sheet.ColumnWidths[2].Should().Be(12);
        sheet.ColumnWidths[4].Should().Be(24);
        sheet.ColumnWidths[6].Should().Be(36);
    }

    [Fact]
    public void DeleteColumn_ShiftsHiddenColumnsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenCols.Add(2);
        sheet.HiddenCols.Add(4);
        sheet.HiddenCols.Add(6);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 4u });

        cmd.Revert(ctx);

        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 4u, 6u });
    }

    [Fact]
    public void DeleteColumn_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 2, 3);
        var originalRight = new CellAddress(sheet.Id, 2, 6);
        var shiftedRight = new CellAddress(sheet.Id, 2, 4);
        sheet.Comments[deleted] = "Remove with column";
        sheet.Comments[originalRight] = "Move left";

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(deleted);
        sheet.Comments.Should().NotContainKey(originalRight);
        sheet.Comments[shiftedRight].Should().Be("Move left");

        cmd.Revert(ctx);

        sheet.Comments[deleted].Should().Be("Remove with column");
        sheet.Comments[originalRight].Should().Be("Move left");
        sheet.Comments.Should().NotContainKey(shiftedRight);
    }

    [Fact]
    public void DeleteColumn_ShiftsThreadedCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 2, 3);
        var originalRight = new CellAddress(sheet.Id, 2, 6);
        var shiftedRight = new CellAddress(sheet.Id, 2, 4);
        sheet.ThreadedComments[deleted] = new ThreadedComment("Remove with column", "Quinn");
        sheet.ThreadedComments[originalRight] = new ThreadedComment("Move left", "Codex");

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ThreadedComments.Should().NotContainKey(deleted);
        sheet.ThreadedComments.Should().NotContainKey(originalRight);
        sheet.ThreadedComments[shiftedRight].Should().Be(new ThreadedComment("Move left", "Codex"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[deleted].Should().Be(new ThreadedComment("Remove with column", "Quinn"));
        sheet.ThreadedComments[originalRight].Should().Be(new ThreadedComment("Move left", "Codex"));
        sheet.ThreadedComments.Should().NotContainKey(shiftedRight);
    }

    [Fact]
    public void DeleteColumn_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 1, 7)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 6), new CellAddress(sheet.Id, 2, 7)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Col.Should().Be(4);
        validation.AppliesTo.End.Col.Should().Be(5);
        format.AppliesTo.Start.Col.Should().Be(4);
        format.AppliesTo.End.Col.Should().Be(5);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Col.Should().Be(6);
        validation.AppliesTo.End.Col.Should().Be(7);
        format.AppliesTo.Start.Col.Should().Be(6);
        format.AppliesTo.End.Col.Should().Be(7);
    }

    [Fact]
    public void DeleteColumn_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 1, 7)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(4);
        wb.NamedRanges["Sales"].End.Col.Should().Be(5);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Col.Should().Be(6);
        wb.NamedRanges["Sales"].End.Col.Should().Be(7);
    }

    [Fact]
    public void DeleteColumn_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 3, 7));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(4);
        sheet.PrintArea.Value.End.Col.Should().Be(5);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Col.Should().Be(6);
        sheet.PrintArea.Value.End.Col.Should().Be(7);
    }

    [Fact]
    public void DeleteColumn_ShiftsColumnPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnPageBreaks.Add(2);
        sheet.ColumnPageBreaks.Add(4);
        sheet.ColumnPageBreaks.Add(8);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ColumnPageBreaks.Should().Equal(2u, 6u);

        cmd.Revert(ctx);

        sheet.ColumnPageBreaks.Should().Equal(2u, 4u, 8u);
    }
}
