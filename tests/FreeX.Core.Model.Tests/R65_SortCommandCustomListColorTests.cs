using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-65 fresh-lens findings for the Core.Commands SortCommand bucket. Covers:
/// R65-commands-sort-6-1 — a custom-list ("First key sort order") sort must rank EVERY
///   list member ahead of any non-member, including numbers — the numbers-before-text type
///   hierarchy previously short-circuited before the custom-list check ran, letting numbers
///   slip ahead of custom-list text members.
/// R65-commands-sort-6-2 — Sort On Cell/Font Color with no target color chosen must be a
///   no-op for ordering (Excel always requires a specific target color per color-sort level);
///   the code previously fell back to an invented raw R/G/B byte-value ordering.
/// </summary>
public sealed class R65_SortCommandCustomListColorTests
{
    [Fact]
    public void CustomListOrder_RanksAllMembersBeforeNumbersAndNonMembers()
    {
        // R65-commands-sort-6-1: with a Mon..Sun custom list active, both custom-list members
        // ("Mon", "Wed") must come first (in list order), THEN numbers (5, 10), THEN the
        // non-member text ("Foo") — numbers must NOT be able to slip in ahead of a custom-list
        // text member just because the old code checked "is this a number?" before "is this a
        // custom-list member?".
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mon"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Wed"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        CustomSortOrder.TryParse("Mon,Tue,Wed,Thu,Fri,Sat,Sun", out var order).Should().BeTrue();
        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true, CustomOrder: order)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Mon"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Wed"));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(5, 1).Should().Be(new TextValue("Foo"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Mon"));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Wed"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Foo"));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void NoCustomListOrder_NumbersStillSortBeforeText_NoRegression()
    {
        // Sibling no-regression case: without any custom list active, the ordinary type
        // hierarchy (numbers/dates before text) is unaffected by the R65-6-1 restructuring.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mon"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Wed"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(10));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // Numbers first (ascending by value), then text alphabetically.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Foo"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Mon"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("Wed"));
    }

    [Fact]
    public void SortByCellColor_NoTargetColor_LeavesDifferentlyColoredCellsInOriginalOrder()
    {
        // R65-commands-sort-6-2: three cells with three different fill colors, sorted "On Cell
        // Color" with NO target color selected, must keep their original relative order (a
        // no-op) — NOT be reordered by an invented R/G/B byte comparison (which would have put
        // Blue before Green before Red here, since Blue's raw bytes sort lowest).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var red = new CellColor(255, 0, 0);
        var green = new CellColor(0, 255, 0);
        var blue = new CellColor(0, 0, 255);
        var redStyleId = workbook.RegisterStyle(new CellStyle { FillColor = red });
        var greenStyleId = workbook.RegisterStyle(new CellStyle { FillColor = green });
        var blueStyleId = workbook.RegisterStyle(new CellStyle { FillColor = blue });

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("A"), StyleId = redStyleId });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("B"), StyleId = greenStyleId });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new TextValue("C"), StyleId = blueStyleId });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));

        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.CellColor, TargetColor: null)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("C"));
    }

    [Fact]
    public void SortByCellColor_WithTargetColor_StillPutsTargetColorFirst_NoRegression()
    {
        // Sibling no-regression case: a color-sort level WITH a target color still pulls
        // matching cells to the front, unaffected by the no-target no-op fix.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var red = new CellColor(255, 0, 0);
        var green = new CellColor(0, 255, 0);
        var redStyleId = workbook.RegisterStyle(new CellStyle { FillColor = red });
        var greenStyleId = workbook.RegisterStyle(new CellStyle { FillColor = green });

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("A"), StyleId = greenStyleId });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("B"), StyleId = redStyleId });
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.CellColor, TargetColor: red)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
    }
}
