using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for G5: the left-to-right sort path must keep blank/error cells last
/// regardless of sort direction, exactly like the vertical sort path already guards for.
/// </summary>
public sealed class SortCommandLeftToRightBlankTests
{
    [Fact]
    public void SortCommand_LeftToRight_Descending_KeepsBlanksLast()
    {
        // Regression: before the fix, ApplyLeftToRight negated the comparison for descending
        // sorts with no blank-last guard (unlike the vertical path), causing the blank column
        // to bubble to the FRONT instead of staying last.
        // Row values: 5, <blank>, 3, 8 sorted Left-to-Right Descending on that row.
        // Expected Excel-correct order: 8, 5, 3, <blank>.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(5));
        // column 2 (offset 1) is intentionally left blank
        sheet.SetCell(new CellAddress(sid, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 1, 4), new NumberValue(8));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 1, 4));

        var descCmd = new SortCommand(sid, range, [new SortKey(0, false)], new SortOptions(LeftToRight: true));
        descCmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(8));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3));
        sheet.GetCell(1, 4).Should().BeNull("blank column must stay last after a left-to-right descending sort");

        descCmd.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(5));
        sheet.GetCell(1, 2).Should().BeNull();
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 4).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void SortCommand_LeftToRight_Ascending_KeepsBlanksLast()
    {
        // Sanity companion: ascending already worked before the fix (CompareScalar's
        // blank-goes-last encoding is used directly with no negation), but verify it
        // still holds after the guard was added.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(5));
        // column 2 (offset 1) is intentionally left blank
        sheet.SetCell(new CellAddress(sid, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 1, 4), new NumberValue(8));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 1, 4));

        var ascCmd = new SortCommand(sid, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));
        ascCmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(8));
        sheet.GetCell(1, 4).Should().BeNull("blank column must stay last after a left-to-right ascending sort");
    }
}
