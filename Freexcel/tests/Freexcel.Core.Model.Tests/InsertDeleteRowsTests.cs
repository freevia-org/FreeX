using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class InsertDeleteRowsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void InsertRow_ShiftsCellsDown()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1).Apply(ctx);

        sheet.GetValue(4, 1).Should().Be(new NumberValue(100));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void InsertRowRevert_RestoresOriginalState()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(3, 1).Should().Be(new NumberValue(100));
        sheet.GetCell(4, 1).Should().BeNull();
    }

    [Fact]
    public void InsertRow_ShiftsCustomRowHeightsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[3] = 30;
        sheet.RowHeights[5] = 45;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowHeights.Should().NotContainKey(3);
        sheet.RowHeights.Should().NotContainKey(4);
        sheet.RowHeights[5].Should().Be(30);
        sheet.RowHeights[7].Should().Be(45);

        cmd.Revert(ctx);

        sheet.RowHeights[3].Should().Be(30);
        sheet.RowHeights[5].Should().Be(45);
        sheet.RowHeights.Should().NotContainKey(7);
    }

    [Fact]
    public void InsertRow_ShiftsCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 3, 2);
        var shifted = new CellAddress(sheet.Id, 5, 2);
        sheet.Comments[original] = "Check this";

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.Comments.Should().NotContainKey(original);
        sheet.Comments[shifted].Should().Be("Check this");

        cmd.Revert(ctx);

        sheet.Comments[original].Should().Be("Check this");
        sheet.Comments.Should().NotContainKey(shifted);
    }

    [Fact]
    public void InsertRow_ShiftsRuleRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1)),
            Type = DvType.List,
            Formula1 = "A,B"
        };
        var format = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 6, 2)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0"
        };
        sheet.DataValidations.Add(validation);
        sheet.ConditionalFormats.Add(format);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        validation.AppliesTo.Start.Row.Should().Be(7);
        validation.AppliesTo.End.Row.Should().Be(8);
        format.AppliesTo.Start.Row.Should().Be(7);
        format.AppliesTo.End.Row.Should().Be(8);

        cmd.Revert(ctx);

        validation.AppliesTo.Start.Row.Should().Be(5);
        validation.AppliesTo.End.Row.Should().Be(6);
        format.AppliesTo.Start.Row.Should().Be(5);
        format.AppliesTo.End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertRow_ShiftsNamedRangesAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        wb.DefineNamedRange("Sales", new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1)));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(7);
        wb.NamedRanges["Sales"].End.Row.Should().Be(8);

        cmd.Revert(ctx);

        wb.NamedRanges["Sales"].Start.Row.Should().Be(5);
        wb.NamedRanges["Sales"].End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertRow_ShiftsPrintAreaAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 3));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.PrintArea!.Value.Start.Row.Should().Be(7);
        sheet.PrintArea.Value.End.Row.Should().Be(8);

        cmd.Revert(ctx);

        sheet.PrintArea!.Value.Start.Row.Should().Be(5);
        sheet.PrintArea.Value.End.Row.Should().Be(6);
    }

    [Fact]
    public void InsertRow_ShiftsRowPageBreaksAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowPageBreaks.Add(3);
        sheet.RowPageBreaks.Add(8);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.RowPageBreaks.Should().Equal(5u, 10u);

        cmd.Revert(ctx);

        sheet.RowPageBreaks.Should().Equal(3u, 8u);
    }

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
    public void InsertRow_ShiftsFilterHiddenRowsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FilterHiddenRows.Add(3);
        sheet.FilterHiddenRows.Add(5);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(new[] { 5u, 7u });

        cmd.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEquivalentTo(new[] { 3u, 5u });
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
    public void InsertRow_ShiftsMergedRegions()
    {
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.AddMergedRegion(mergeRange);

        new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1).Apply(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(4);
        sheet.MergedRegions[0].End.Row.Should().Be(5);
    }

    [Fact]
    public void InsertRow_InsideMergedRegionExpandsRegion()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 2)));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(3);
        sheet.MergedRegions[0].End.Row.Should().Be(7);

        cmd.Revert(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(3);
        sheet.MergedRegions[0].End.Row.Should().Be(5);
    }

    [Fact]
    public void DeleteRow_ShiftsMergedRegionsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 2)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 2)));

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 2)));
    }

    [Fact]
    public void DeleteRows_PartiallyOverlappingMerge_ShrinksInsteadOfDropping()
    {
        // Merge spans rows 2-6; delete rows 4-6 → merge should shrink to rows 2-3
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 6, 2)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 4, count: 3);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)));

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 6, 2)));
    }

    [Fact]
    public void DeleteRows_EntirelyEnclosedMerge_DropsIt()
    {
        // Merge entirely within deleted rows → should be dropped
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 2)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 5);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void InsertRows_WhenDataWouldBePushedPastMaxRow_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow, 1), new NumberValue(1));

        var result = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pushed past the last row");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
