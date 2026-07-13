using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-39 fresh-lens findings for the "sort by custom-list/color/icon, case-sensitive"
/// bucket. Covers:
/// R39-commands-sort-custom-2-1 — case-sensitive text sort must still be alphabetical, with
///   case only as a same-letter tiebreak (lowercase before uppercase).
/// R39-commands-sort-custom-2-2 — Sort On Cell Color/Font Color must resolve the EFFECTIVE
///   color, including a color contributed by a matching conditional-formatting rule, not just
///   the cell's static stored style.
/// </summary>
public sealed class R39_SortCommandCustom2Tests
{
    [Fact]
    public void CaseSensitiveSort_OrdersAlphabeticallyAcrossMixedCaseWords()
    {
        // R39-commands-sort-custom-2-1: "Zebra", "apple", "Mango", "banana" case-sensitive
        // ascending must come back alphabetically (apple, banana, Mango, Zebra) — NOT clumped by
        // leading-letter case (which the old ordinal-codepoint compare produced: Mango, Zebra,
        // apple, banana, since uppercase code points precede lowercase ones).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Zebra"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Mango"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("banana"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(CaseSensitive: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("apple"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("banana"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Mango"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Zebra"));
    }

    [Fact]
    public void CaseSensitiveSort_UsesCaseOnlyAsTiebreakBetweenIdenticalWords_NoRegression()
    {
        // Sibling no-regression case: when two entries are letter-for-letter identical apart
        // from case ("apple" vs "Apple"), case-sensitive sort still uses case as the tiebreak,
        // with lowercase sorting before uppercase.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("apple"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(CaseSensitive: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("apple"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Apple"));
    }

    [Fact]
    public void CaseInsensitiveSort_StillOrdersAlphabetically_NoRegression()
    {
        // Sibling no-regression case: the default (case-insensitive) path is untouched.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("apple"));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true)], new SortOptions(CaseSensitive: false));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("apple"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Banana"));
    }

    [Fact]
    public void SortByCellColor_ReadsColorFromMatchingConditionalFormatRule()
    {
        // R39-commands-sort-custom-2-2: a CF rule colors cells red when value > 100. Neither cell
        // has a manual/static fill — the fill only exists via the CF rule — so "Sort On: Cell
        // Color" must still be able to group the CF-colored (red, >100) rows apart from the
        // uncolored (<=100) rows.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(50));  // no CF match, no fill
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(200)); // CF match -> red fill
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));  // no CF match, no fill

        var red = new CellColor(255, 0, 0);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = red }
        });

        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.CellColor, TargetColor: red)]);

        command.Apply(ctx).Success.Should().BeTrue();

        // The CF-red row (200) must be pulled to the top; the two uncolored rows keep their
        // original relative order (stable sort) after it.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(200));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(50));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void SortByCellColor_StaticFillStillWorks_NoRegression()
    {
        // Sibling no-regression case: a plain static (non-CF) fill color sort is unaffected.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        var green = new CellColor(0, 255, 0);
        var greenStyleId = workbook.RegisterStyle(new CellStyle { FillColor = green });

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("A") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("B"), StyleId = greenStyleId });

        var command = new SortCommand(
            sheet.Id, range,
            [new SortKey(0, true, SortOn.CellColor, TargetColor: green)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
    }
}
