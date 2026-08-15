using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    [Fact]
    public void DeleteRow_RemovesCellsAndShiftsUp()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(30));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void DeleteRowRevert_RestoresCells()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void DeleteRow_ShiftsCustomRowHeightsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[2] = 22;
        sheet.RowHeights[4] = 44;
        sheet.RowHeights[6] = 66;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowHeights[2].Should().Be(22);
        sheet.RowHeights[4].Should().Be(66);
        sheet.RowHeights.Should().NotContainKey(3);
        sheet.RowHeights.Should().NotContainKey(6);

        cmd.Revert(ctx);

        sheet.RowHeights[2].Should().Be(22);
        sheet.RowHeights[4].Should().Be(44);
        sheet.RowHeights[6].Should().Be(66);
    }

    // R136-io-worksheet-props-col-row-default-style-shift: sheet.RowStyles is the same
    // absolute-row key space as RowHeights and must shift/drop entries the same way on delete,
    // or a whole-row default style lands on the wrong (stale-indexed) row.
    [Fact]
    public void DeleteRow_ShiftsRowStylesAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var aboveStyle = new StyleId(1);
        var deletedStyle = new StyleId(2);
        var belowStyle = new StyleId(3);
        sheet.RowStyles[2] = aboveStyle;
        sheet.RowStyles[4] = deletedStyle;
        sheet.RowStyles[6] = belowStyle;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowStyles[2].Should().Be(aboveStyle);
        sheet.RowStyles[4].Should().Be(belowStyle);
        sheet.RowStyles.Should().NotContainKey(3);
        sheet.RowStyles.Should().NotContainKey(6);

        cmd.Revert(ctx);

        sheet.RowStyles[2].Should().Be(aboveStyle);
        sheet.RowStyles[4].Should().Be(deletedStyle);
        sheet.RowStyles[6].Should().Be(belowStyle);
    }

    // Deleting the styled row itself must remove its default style entirely (not leave it
    // painting whatever row slides into that slot), and undo must bring it back.
    [Fact]
    public void DeleteRow_DeletesStyledRowItself_UndoRestoresStyle()
    {
        var (_, sheet, ctx) = Setup();
        var bannerStyle = new StyleId(1);
        sheet.RowStyles[4] = bannerStyle;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 4, count: 1);
        cmd.Apply(ctx);

        sheet.RowStyles.Should().NotContainKey(3);
        sheet.RowStyles.Should().NotContainKey(4);

        cmd.Revert(ctx);

        sheet.RowStyles[4].Should().Be(bannerStyle);
    }

    // Deleting a row strictly above the styled row must shift the style up with its row, not
    // leave it stranded at the old (now-wrong) index.
    [Fact]
    public void DeleteRow_AboveStyledRow_ShiftsRowStyleUpAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var bannerStyle = new StyleId(1);
        sheet.RowStyles[5] = bannerStyle;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx);

        sheet.RowStyles.Should().NotContainKey(5);
        sheet.RowStyles[4].Should().Be(bannerStyle);

        cmd.Revert(ctx);

        sheet.RowStyles.Should().NotContainKey(4);
        sheet.RowStyles[5].Should().Be(bannerStyle);
    }

    [Fact]
    public void DeleteRow_ShiftsHiddenRowsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenRows.Add(2);
        sheet.HiddenRows.Add(4);
        sheet.HiddenRows.Add(6);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.HiddenRows.Should().BeEquivalentTo(new[] { 2u, 4u });

        cmd.Revert(ctx);

        sheet.HiddenRows.Should().BeEquivalentTo(new[] { 2u, 4u, 6u });
    }

    [Fact]
    public void DeleteRow_ShiftsFilterHiddenRowsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FilterHiddenRows.Add(2);
        sheet.FilterHiddenRows.Add(4);
        sheet.FilterHiddenRows.Add(6);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(new[] { 2u, 4u });

        cmd.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(new[] { 2u, 4u, 6u });
    }

    [Fact]
    public void DeleteRow_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 3, 2);
        var originalBelow = new CellAddress(sheet.Id, 6, 2);
        var shiftedBelow = new CellAddress(sheet.Id, 4, 2);
        sheet.Comments[deleted] = "Remove with row";
        sheet.Comments[originalBelow] = "Move up";

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(deleted);
        sheet.Comments.Should().NotContainKey(originalBelow);
        sheet.Comments[shiftedBelow].Should().Be("Move up");

        cmd.Revert(ctx);

        sheet.Comments[deleted].Should().Be("Remove with row");
        sheet.Comments[originalBelow].Should().Be("Move up");
        sheet.Comments.Should().NotContainKey(shiftedBelow);
    }

    [Fact]
    public void DeleteRow_ShiftsThreadedCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 3, 2);
        var originalBelow = new CellAddress(sheet.Id, 6, 2);
        var shiftedBelow = new CellAddress(sheet.Id, 4, 2);
        sheet.ThreadedComments[deleted] = new ThreadedComment("Remove with row", "Quinn");
        sheet.ThreadedComments[originalBelow] = new ThreadedComment("Move up", "Codex");

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.ThreadedComments.Should().NotContainKey(deleted);
        sheet.ThreadedComments.Should().NotContainKey(originalBelow);
        sheet.ThreadedComments[shiftedBelow].Should().Be(new ThreadedComment("Move up", "Codex"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[deleted].Should().Be(new ThreadedComment("Remove with row", "Quinn"));
        sheet.ThreadedComments[originalBelow].Should().Be(new ThreadedComment("Move up", "Codex"));
        sheet.ThreadedComments.Should().NotContainKey(shiftedBelow);
    }

    [Fact]
    public void DeleteRow_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 7, 1)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 6, 2), new CellAddress(sheet.Id, 7, 2)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Row.Should().Be(4);
        validation.AppliesTo.End.Row.Should().Be(5);
        format.AppliesTo.Start.Row.Should().Be(4);
        format.AppliesTo.End.Row.Should().Be(5);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Row.Should().Be(6);
        validation.AppliesTo.End.Row.Should().Be(7);
        format.AppliesTo.Start.Row.Should().Be(6);
        format.AppliesTo.End.Row.Should().Be(7);
    }

    [Fact]
    public void DeleteRow_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 1)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(4);
        wb.NamedRanges["Sales"].End.Row.Should().Be(5);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(6);
        wb.NamedRanges["Sales"].End.Row.Should().Be(7);
    }

    [Fact]
    public void DeleteRow_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 3));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Row.Should().Be(4);
        sheet.PrintArea.Value.End.Row.Should().Be(5);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Row.Should().Be(6);
        sheet.PrintArea.Value.End.Row.Should().Be(7);
    }

    [Fact]
    public void DeleteRow_ShiftsRowPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowPageBreaks.Add(2);
        sheet.RowPageBreaks.Add(4);
        sheet.RowPageBreaks.Add(8);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowPageBreaks.Should().Equal(2u, 6u);

        cmd.Revert(ctx);

        sheet.RowPageBreaks.Should().Equal(2u, 4u, 8u);
    }

    [Fact]
    public void DeleteRow_NamedRangeOverlapsDeletion_ShrinksToSurvivingRows()
    {
        // Named range A1:A5, delete rows 3–5 → surviving part A1:A2
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1)));

        new DeleteRowsCommand(sheet.Id, startRow: 3, count: 3).Apply(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(1);
        wb.NamedRanges["Sales"].End.Row.Should().Be(2);
    }

    [Fact]
    public void DeleteRow_NamedRangeEntirelyDeleted_RemovesNamedRange()
    {
        // Named range A3:A5, delete rows 3–5 → named range should be removed
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 1)));

        new DeleteRowsCommand(sheet.Id, startRow: 3, count: 3).Apply(ctx);

        wb.NamedRanges.Should().NotContainKey("Sales");
    }

}
