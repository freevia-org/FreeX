using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r254: the same-criterion no-op decision on a STRUCTURED TABLE range, which is where the table
/// half of the decision -- <c>StructuredTableFilterColumnSync.Unchanged</c>, and with it
/// <c>SameAs</c> for <see cref="StructuredTableFilterColumnModel"/> -- actually does any work.
///
/// <para>On a plain worksheet range that half sees a null snapshot and returns true without
/// comparing anything, so the sibling tests in SortFilterTests could not tell a working comparison
/// from a missing one. These can: a table's replacement filter column is a freshly built model
/// carrying its own collections, so record equality would report "changed" on every re-application
/// and the no-op assertions below would fail.</para>
/// </summary>
public sealed class R254_TableFilterReapplyNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx, GridRange Range) SetUpNumericTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(50));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 7,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Score") },
        });

        return (sheet, ctx, range);
    }

    [Fact]
    public void TopBottomFilterCommand_OnATableRange_ReapplyingTheSameCriterionReportsANoOp()
    {
        var (sheet, ctx, range) = SetUpNumericTable();

        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true)
            .Apply(ctx).IsNoOp.Should().BeFalse("the first application hid a row and wrote the criterion");
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle(
            "the table half must actually hold a criterion, or this test would prove nothing about it");

        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true)
            .Apply(ctx).IsNoOp.Should().BeTrue(
                "re-picking Top 2 writes the same criterion the table already carries -- comparing "
                + "the freshly built filter column by reference would call this an edit forever");
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
    }

    [Fact]
    public void TopBottomFilterCommand_OnATableRange_ADifferentCountIsNotANoOp()
    {
        var (sheet, ctx, range) = SetUpNumericTable();

        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true).Apply(ctx);

        new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 3, top: true)
            .Apply(ctx).IsNoOp.Should().BeFalse("Top 3 stores a different <top10 val> and keeps a third row");
    }

    [Fact]
    public void FilterConditionCommand_OnATableRange_ReapplyingTheSameConditionReportsANoOp()
    {
        var (sheet, ctx, range) = SetUpNumericTable();

        new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(60))
            .Apply(ctx).IsNoOp.Should().BeFalse();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();

        new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(60))
            .Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void FilterConditionCommand_OnATableRange_ADifferentThresholdIsNotANoOp()
    {
        var (sheet, ctx, range) = SetUpNumericTable();

        new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(60))
            .Apply(ctx);

        new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(95))
            .Apply(ctx).IsNoOp.Should().BeFalse(
                "a different threshold is a different customFilter value on the table's own column");
    }

    /// <summary>
    /// The table half in isolation: a colour filter on a table range stores a
    /// <c>ColorFilter</c> nested model rather than a raw criterion, so this exercises the nested
    /// content comparison rather than the string-list one.
    /// </summary>
    [Fact]
    public void CellFillColorFilterCommand_OnATableRange_ReapplyingTheSameColourReportsANoOp()
    {
        var (sheet, ctx, range) = SetUpNumericTable();
        var wb = ctx.Workbook;
        var green = new CellColor(0, 200, 0);
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId =
            wb.RegisterStyle(new CellStyle { FillColor = green });

        new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, green)
            .Apply(ctx).IsNoOp.Should().BeFalse();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle(
            column => column.ColorFilter != null);

        new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, green)
            .Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void CellFillColorFilterCommand_OnATableRange_ADifferentColourIsNotANoOp()
    {
        var (sheet, ctx, range) = SetUpNumericTable();
        var wb = ctx.Workbook;
        var green = new CellColor(0, 200, 0);
        var red = new CellColor(200, 0, 0);
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId = wb.RegisterStyle(new CellStyle { FillColor = green });
        sheet.GetCell(new CellAddress(sheet.Id, 4, 1))!.StyleId = wb.RegisterStyle(new CellStyle { FillColor = red });

        new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, green).Apply(ctx);

        new CellFillColorFilterCommand(sheet.Id, range, filterColOffset: 0, red)
            .Apply(ctx).IsNoOp.Should().BeFalse(
                "the stored ColorFilter carries a different Color, and a different row stays visible");
    }
}
