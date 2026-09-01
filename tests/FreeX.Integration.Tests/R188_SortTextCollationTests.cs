using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r188-sort-collation: the text branch of the sort comparison used
/// <c>StringComparison.OrdinalIgnoreCase</c>, which orders by UTF-16 code point. Every accented
/// word therefore sorted after the entire ASCII alphabet: sorting {zebra, east, elan, apple} with
/// an acute e on "elan" produced apple, east, zebra, elan. Excel collates with the user's culture,
/// where "elan" belongs next to "east". The case-sensitive path shared the same ordinal primary
/// key, so the Sort dialog's "case sensitive" option did not avoid it either.
/// </summary>
public sealed class R188_SortTextCollationTests
{
    private static readonly string[] Words = ["zebra", "east", "élan", "apple"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SortingAccentedText_AlphabetizesItRatherThanAppendingIt(bool caseSensitive)
    {
        // de-DE is the locale the finding was described against; the assertion below holds for any
        // culture whose collation treats an accented letter as its base letter, which is every
        // Latin-script culture .NET ships. Restored in the finally so the ambient culture of the
        // rest of the run is untouched.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            var ctx = new TestCommandContext(workbook);
            var sid = sheet.Id;

            for (uint row = 1; row <= (uint)Words.Length; row++)
                sheet.SetCell(new CellAddress(sid, row, 1), new TextValue(Words[row - 1]));

            var range = new GridRange(
                new CellAddress(sid, 1, 1),
                new CellAddress(sid, (uint)Words.Length, 1));
            var cmd = new SortCommand(
                sid,
                range,
                [new FreeX.Core.Commands.SortKey(0, true)],
                new SortOptions { CaseSensitive = caseSensitive });

            cmd.Apply(ctx).Success.Should().BeTrue();

            var sorted = new List<string>();
            for (uint row = 1; row <= (uint)Words.Length; row++)
                sorted.Add(((TextValue)sheet.GetValue(row, 1)!).Value);

            // The accented word sits between "apple" and "zebra", not after both of them.
            sorted.Should().Equal("apple", "east", "élan", "zebra");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CompareIgnoreCase_ForKeysTheCultureCallsEqual_StillSeparatesThem()
    {
        // The ordinal fallback exists so the comparison stays a total order: a culture-aware
        // compare reports 0 for strings a user can tell apart, and a comparer that calls distinct
        // values equal makes the sorted result depend on the input order.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            var ctx = new TestCommandContext(workbook);
            var sid = sheet.Id;

            sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("co-op"));
            sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("coop"));

            var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1));
            new SortCommand(sid, range, sortByColOffset: 0, ascending: true)
                .Apply(ctx).Success.Should().BeTrue();

            var first = ((TextValue)sheet.GetValue(1, 1)!).Value;
            var second = ((TextValue)sheet.GetValue(2, 1)!).Value;
            first.Should().NotBe(second, "both rows must survive the sort");
            new[] { first, second }.Should().BeEquivalentTo(["co-op", "coop"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
