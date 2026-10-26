using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// r556: the AutoFilter checklist builds a sort key by parsing each value's filter text, and
/// <c>NumberStyles.Float</c> accepts the literal "NaN" and "Infinity" while an overflowing
/// literal parses successfully to Infinity. A TEXT cell reading "NaN" therefore landed in the
/// checklist's NUMERIC bucket (Rank 0) and sorted among the numbers.
///
/// <para>Excel does not do that: its number parser rejects those tokens, so typing NaN into a
/// cell produces TEXT, and the filter list sorts it with the other text. This is an alignment
/// fix rather than a crash fix -- the comparison itself is sound, because it uses
/// <c>double.CompareTo</c>, which defines a TOTAL ORDER including NaN, unlike the &lt; and &gt;
/// operators that would have made the comparer inconsistent and thrown out of Array.Sort.</para>
/// </summary>
public sealed class R556_FilterChecklistSortsNonNumericTextAsTextTests
{
    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static IReadOnlyList<string> ChecklistValues(params ScalarValue[] columnValues)
    {
        var sheet = CreateSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        for (var i = 0; i < columnValues.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 2), 1), columnValues[i]);

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)(columnValues.Length + 1), 1));
        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        return AutoFilterChecklistPlanner
            .CreateItems(sheet, plan, blankDisplayText: "(Blanks)")
            .Select(item => item.Value)
            .ToList();
    }

    // Each case carries a TEXT SENTINEL chosen so that the text-bucket order and the
    // numeric-rank order differ. Without one, "Infinity" and "1e400" pass either way: ranked as
    // numbers they are the largest value and land last, which is exactly where correct text
    // placement would put them. My first draft asserted only "after the numbers" and so was
    // VACUOUS for those two -- it went green against the unfixed code.
    [Theory]
    [InlineData("NaN", "AAA", "NaN")]
    [InlineData("Infinity", "AAA", "Infinity")]
    [InlineData("-Infinity", "-Infinity", "AAA")]
    [InlineData("1e400", "0zz", "1e400")]
    public void Text_that_is_not_a_real_number_sorts_with_the_text(
        string hostile,
        string expectedFirstText,
        string expectedSecondText)
    {
        var values = ChecklistValues(
            new NumberValue(10),
            new TextValue(hostile),
            new NumberValue(2),
            new TextValue(expectedFirstText == hostile ? expectedSecondText : expectedFirstText));

        // Numbers first in numeric order, then BOTH text values in text order.
        values.Should().Equal("2", "10", expectedFirstText, expectedSecondText);
    }

    [Fact]
    public void Ordinary_numbers_still_sort_numerically_not_lexically()
    {
        // Non-vacuity: a guard that pushed everything into the text bucket would satisfy the
        // assertions above while breaking real numeric ordering -- lexically "10" precedes "2".
        ChecklistValues(new NumberValue(10), new NumberValue(2), new NumberValue(30))
            .Should().Equal("2", "10", "30");
    }

    [Fact]
    public void Ordinary_text_still_sorts_after_numbers()
    {
        // Pins the pre-existing rank order the fix relies on, so the buckets cannot be reordered
        // without this test noticing.
        ChecklistValues(new TextValue("apple"), new NumberValue(5))
            .Should().Equal("5", "apple");
    }
}
