using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-commands-sort-customlist-5-1: a custom list's "member always precedes non-member" rule
/// is a fixed, direction-independent property of the list — Excel's Sort dialog doesn't even
/// let a user combine Descending with a custom list (choosing "Custom List..." replaces the
/// A-to-Z/Z-to-A choice entirely). FreeX exposes the per-key Ascending toggle and the custom
/// list picker as independent controls that both land on the same SortKey, so Ascending=false
/// really can be combined with a CustomOrder. Before the fix, SortCommand's shared
/// ascending/descending negation was applied uniformly to CustomSortOrder.Compare's result,
/// including the -1/+1 it returns purely for list-membership precedence — so Descending flipped
/// "list members first" into "non-members first" and reversed the day names' relative order.
/// </summary>
public sealed class R91_SortCommandCustomListDescendingTests
{
    [Fact]
    public void CustomListOrder_Descending_WalksListInReverse_NonMemberStillLast()
    {
        // A1:A4 = Wed, Foo, Mon, Tue — Sort Descending ("Z to A") with the Sun..Sat custom
        // list active. Excel's behaviour: the custom list is walked in reverse (Wed, Tue, Mon)
        // and "Foo" (not in the list) still goes LAST, exactly as it would in ascending order —
        // descending must never let a non-member jump ahead of every list member.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Wed"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Mon"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Tue"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        CustomSortOrder.TryParse("Sun,Mon,Tue,Wed,Thu,Fri,Sat", out var order).Should().BeTrue();
        var command = new SortCommand(sheet.Id, range, [new SortKey(0, Ascending: false, CustomOrder: order)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Wed"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Tue"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Mon"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Foo")); // non-member still last, not first

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Wed"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Foo"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Mon"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Tue"));
    }

    [Fact]
    public void CustomListOrder_Descending_LeftToRight_WalksListInReverse_NonMemberStillLast_NoRegression()
    {
        // No-regression sibling exercising the row-of-columns (ApplyLeftToRight) comparator,
        // which has the identical ascending/descending negation pattern as the row-sort
        // comparator above — confirms the fix was applied at both call sites.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Wed"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Mon"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Tue"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 4));

        CustomSortOrder.TryParse("Sun,Mon,Tue,Wed,Thu,Fri,Sat", out var order).Should().BeTrue();
        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, Ascending: false, CustomOrder: order)],
            new SortOptions(LeftToRight: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("Wed"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Tue"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("Mon"));
        sheet.GetValue(1, 4).Should().Be(new TextValue("Foo")); // non-member still last, not first
    }
}
