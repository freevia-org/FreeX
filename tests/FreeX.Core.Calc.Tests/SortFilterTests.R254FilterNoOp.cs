using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// r254: the five AutoFilter commands whose third snapshot is the structured table's own
/// filter-column list. Each is re-applicable from the Filter menu, so re-picking the same criterion
/// writes exactly what is already there; reporting that as an edit pushes an undo entry, and
/// UndoRedoStack.Push clears the redo stack.
///
/// <para>Both directions are pinned for each command. The no-op direction is the fix; the
/// changed direction is the guard against the worse failure, where a real edit is swallowed.</para>
/// </summary>
public partial class SortFilterTests
{
    private static (Workbook wb, ICommandContext ctx, Sheet sheet, GridRange range, SheetId sid) MakeFilterSheet()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sid, 5, 1), new NumberValue(40));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        return (wb, ctx, sheet, range, sid);
    }

    [Fact]
    public void TopBottomFilterCommand_ReapplyingTheSameCriterionReportsANoOp()
    {
        var (wb, ctx, sheet, range, sid) = MakeFilterSheet();

        new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: true).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first application hid rows");
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void TopBottomFilterCommand_ADifferentCountIsNotANoOp()
    {
        var (_, ctx, _, range, sid) = MakeFilterSheet();

        new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: true).Apply(ctx);

        new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 3, top: true).Apply(ctx)
            .IsNoOp.Should().BeFalse("Top 3 keeps a row Top 2 hid");
    }

    [Fact]
    public void TopBottomFilterCommand_SwitchingBottomAfterTopIsNotANoOp()
    {
        var (_, ctx, _, range, sid) = MakeFilterSheet();

        new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: true).Apply(ctx);

        new TopBottomFilterCommand(sid, range, filterColOffset: 0, count: 2, top: false).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void CellFillColorFilterCommand_ReapplyingTheSameColourReportsANoOp()
    {
        var (wb, ctx, sheet, range, sid) = MakeFilterSheet();
        var green = new CellColor(0, 200, 0);
        PaintFill(wb, sheet, sid, row: 3, green);

        new CellFillColorFilterCommand(sid, range, filterColOffset: 0, green).Apply(ctx)
            .IsNoOp.Should().BeFalse("rows without the colour were hidden");
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new CellFillColorFilterCommand(sid, range, filterColOffset: 0, green).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void CellFillColorFilterCommand_ADifferentColourIsNotANoOp()
    {
        var (wb, ctx, sheet, range, sid) = MakeFilterSheet();
        var green = new CellColor(0, 200, 0);
        var red = new CellColor(200, 0, 0);
        PaintFill(wb, sheet, sid, row: 3, green);
        PaintFill(wb, sheet, sid, row: 4, red);

        new CellFillColorFilterCommand(sid, range, filterColOffset: 0, green).Apply(ctx);

        new CellFillColorFilterCommand(sid, range, filterColOffset: 0, red).Apply(ctx)
            .IsNoOp.Should().BeFalse("a different colour keeps a different row visible");
    }

    [Fact]
    public void CellNoFillColorFilterCommand_ReapplyingReportsANoOp()
    {
        var (wb, ctx, sheet, range, sid) = MakeFilterSheet();
        PaintFill(wb, sheet, sid, row: 3, new CellColor(0, 200, 0));

        new CellNoFillColorFilterCommand(sid, range, filterColOffset: 0).Apply(ctx)
            .IsNoOp.Should().BeFalse("the filled row was hidden");
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new CellNoFillColorFilterCommand(sid, range, filterColOffset: 0).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void CellFontColorFilterCommand_ReapplyingTheSameColourReportsANoOp()
    {
        var (wb, ctx, sheet, range, sid) = MakeFilterSheet();
        var blue = new CellColor(0, 0, 200);
        PaintFontColor(wb, sheet, sid, row: 3, blue);

        new CellFontColorFilterCommand(sid, range, filterColOffset: 0, blue).Apply(ctx)
            .IsNoOp.Should().BeFalse();
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new CellFontColorFilterCommand(sid, range, filterColOffset: 0, blue).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void FilterConditionCommand_ReapplyingTheSameConditionReportsANoOp()
    {
        var (wb, ctx, sheet, range, sid) = MakeFilterSheet();

        new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(25))
            .Apply(ctx).IsNoOp.Should().BeFalse("rows at or below 25 were hidden");
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(25))
            .Apply(ctx).IsNoOp.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void FilterConditionCommand_ADifferentThresholdIsNotANoOp()
    {
        var (_, ctx, _, range, sid) = MakeFilterSheet();

        new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(25))
            .Apply(ctx);

        new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(35))
            .Apply(ctx).IsNoOp.Should().BeFalse("the threshold change hides one more row");
    }

    private static void PaintFill(Workbook workbook, Sheet sheet, SheetId sid, uint row, CellColor color) =>
        sheet.GetCell(new CellAddress(sid, row, 1))!.StyleId =
            workbook.RegisterStyle(new CellStyle { FillColor = color });

    private static void PaintFontColor(Workbook workbook, Sheet sheet, SheetId sid, uint row, CellColor color) =>
        sheet.GetCell(new CellAddress(sid, row, 1))!.StyleId =
            workbook.RegisterStyle(new CellStyle { FontColor = color });
}
