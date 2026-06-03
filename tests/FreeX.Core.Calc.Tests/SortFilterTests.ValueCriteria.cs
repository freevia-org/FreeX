using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    [Fact]
    public void FilterCondition_TextContains_HidesNonMatchingRowsAndUndoRestores()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Red Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Green Apple"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new TextContainsFilterCriterion("apple"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(4u);

        command.Revert(ctx);

        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void FilterCondition_NumberGreaterThan_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(20));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(10));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_NumberLessThan_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(20));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberLessThanFilterCriterion(15));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_NumberEquals_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(12));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberEqualsFilterCriterion(12));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_NumberNotEquals_HidesMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(12));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberNotEqualsFilterCriterion(12));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_NumberGreaterThanOrEqual_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(20));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberGreaterThanOrEqualFilterCriterion(12));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_NumberLessThanOrEqual_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(20));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberLessThanOrEqualFilterCriterion(12));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_NumberBetween_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(12));
        sheet.SetCell(new CellAddress(sid, 4, 1), new NumberValue(20));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberBetweenFilterCriterion(10, 15));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_TextBeginsWith_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Red Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Green Apple"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Red Pear"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new TextBeginsWithFilterCriterion("red"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_TextEndsWith_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Red Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Green Apple"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Red Pear"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new TextEndsWithFilterCriterion("apple"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_TextDoesNotContain_HidesMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Red Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Green Apple"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new TextDoesNotContainFilterCriterion("apple"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_TextEquals_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Red Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("red apple"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new TextEqualsFilterCriterion("Red Apple"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_TextNotEquals_HidesMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Red Apple"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("red apple"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new TextNotEqualsFilterCriterion("Red Apple"));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_Blank_HidesNonBlankRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Pear"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new BlankFilterCriterion());

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_NonBlank_HidesBlankRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Pear"));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NonBlankFilterCriterion());

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }
}
