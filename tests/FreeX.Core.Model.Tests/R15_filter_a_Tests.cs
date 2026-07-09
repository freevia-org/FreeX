using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 15 fixes:
/// - R15-autofilter-advanced-deep-1: NumberNotEquals/DateNotEquals must keep non-matching-type
///   values (text/blank/bool) visible instead of hiding everything that isn't a number/date.
/// - R15-autofilter-advanced-deep-3: '?' and '*' are Excel wildcards (any-one-char / any-run)
///   in text filter criteria and in Advanced Filter plain-text criteria, with '~' escaping them.
/// </summary>
public sealed class R15_filter_a_Tests
{
    [Fact]
    public void NumberNotEquals_KeepsNonNumericValueVisible()
    {
        var (_, sheet, ctx) = Setup();
        var sid = sheet.Id;
        Set(sheet, 1, 1, "Value");
        Set(sheet, 2, 1, 5);
        Set(sheet, 3, 1, 10);
        Set(sheet, 4, 1, "N/A");
        Set(sheet, 5, 1, 20);
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new NumberNotEqualsFilterCriterion(5));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u); // 5 == 5, hidden
        sheet.FilterHiddenRows.Should().NotContain(3u); // 10 != 5, visible
        sheet.FilterHiddenRows.Should().NotContain(4u); // "N/A" is a different type, must stay visible
        sheet.FilterHiddenRows.Should().NotContain(5u); // 20 != 5, visible
    }

    [Fact]
    public void DateNotEquals_KeepsNonDateValueVisible()
    {
        var (_, sheet, ctx) = Setup();
        var sid = sheet.Id;
        var target = new DateOnly(2026, 5, 15);
        Set(sheet, 1, 1, "Value");
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(target.ToDateTime(TimeOnly.MinValue)));
        Set(sheet, 3, 1, "N/A");
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(target.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateNotEqualsFilterCriterion(target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u); // matching date, hidden
        sheet.FilterHiddenRows.Should().NotContain(3u); // "N/A" is a different type, must stay visible
        sheet.FilterHiddenRows.Should().NotContain(4u); // different date, visible
    }

    [Fact]
    public void TextContains_WildcardQuestionMark_MatchesSingleCharacterVariants()
    {
        var criterion = new TextContainsFilterCriterion("c?t");

        criterion.Matches(new TextValue("cat")).Should().BeTrue();
        criterion.Matches(new TextValue("cot")).Should().BeTrue();
        criterion.Matches(new TextValue("dog")).Should().BeFalse();
    }

    [Fact]
    public void AdvancedFilter_PlainWildcardCriterion_MatchesPatternAgainstWholeValue()
    {
        var (_, sheet, ctx) = Setup();
        var sid = sheet.Id;
        Set(sheet, 1, 1, "Name");
        Set(sheet, 2, 1, "Smith");
        Set(sheet, 3, 1, "Smyth");
        Set(sheet, 4, 1, "Smithsonian");
        Set(sheet, 1, 3, "Name");
        Set(sheet, 2, 3, "Sm?th");
        var listRange = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));
        var criteriaRange = new GridRange(new CellAddress(sid, 1, 3), new CellAddress(sid, 2, 3));

        var command = new AdvancedFilterCommand(listRange, criteriaRange, CopyTo: null, UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u); // "Smith" matches Sm?th
        sheet.FilterHiddenRows.Should().NotContain(3u); // "Smyth" matches Sm?th
        sheet.FilterHiddenRows.Should().Contain(4u); // "Smithsonian" is longer than the pattern, hidden
    }

    [Fact]
    public void TextEquals_TildeEscapedAsterisk_MatchesOnlyLiteralAsterisk()
    {
        var criterion = new TextEqualsFilterCriterion("~*");

        criterion.Matches(new TextValue("*")).Should().BeTrue();
        criterion.Matches(new TextValue("abc")).Should().BeFalse();
        criterion.Matches(new TextValue("")).Should().BeFalse();
    }

    private static (Workbook Wb, Sheet Sheet, ICommandContext Ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(text));

    private static void Set(Sheet sheet, uint row, uint col, double number) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(number));
}
