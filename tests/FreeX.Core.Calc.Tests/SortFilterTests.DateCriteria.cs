using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    [Fact]
    public void FilterCondition_DateEquals_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15, 12, 30, 0)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateEqualsFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateNotEquals_HidesMatchingDateRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15, 12, 30, 0)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateNotEqualsFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_DateAfter_HidesEarlierRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateAfterFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_DateBefore_HidesLaterRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateBeforeFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateOnOrAfter_HidesEarlierRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateOnOrAfterFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_DateOnOrBefore_HidesLaterRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateOnOrBeforeFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateBetween_HidesOutsideRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 20)));
        sheet.SetCell(new CellAddress(sid, 5, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 21)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateBetweenFilterCriterion(new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 20)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
        sheet.FilterHiddenRows.Should().Contain(5u);
    }
}
