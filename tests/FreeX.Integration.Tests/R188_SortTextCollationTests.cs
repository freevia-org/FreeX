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

    // The same word in NFC (precomposed U+00E9) and NFD (e + U+0301) form. A user who pastes from
    // macOS gets NFD; typing on Windows gets NFC. Culture collation reports these EQUAL, which is
    // exactly the case the ordinal tie-break exists for.
    //
    // r189: this test used to use ("co-op", "coop") on the stated premise that the culture calls
    // them equal. Measured on .NET 10 under de-DE, string.Compare with CompareOptions.IgnoreCase
    // returns -1 for that pair, so the primary comparison decided it and the tie-break was never
    // reached -- the test passed without exercising the thing it named. These two do compare 0.
    private const string PrecomposedAccent = "\u00E9lan";
    private const string DecomposedAccent = "e\u0301lan";

    [Fact]
    public void CompareIgnoreCase_ForKeysTheCultureCallsEqual_OrdersThemDeterministically()
    {
        // The ordinal fallback keeps the comparison a total order: a culture-aware compare reports
        // 0 for strings a user can tell apart, and a comparer that calls distinct values equal
        // leaves the sorted result dependent on the order the rows happened to be in.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            // Both input orders must produce the SAME output order. That is what "total" means, and
            // it is the assertion the old version of this test was missing: it only checked that
            // both rows survived, which a stable sort guarantees whatever the comparer returns.
            SortedPair(PrecomposedAccent, DecomposedAccent)
                .Should().Equal(SortedPair(DecomposedAccent, PrecomposedAccent));

            // Ordinal decides: the NFD form starts with 'e' (U+0065), below the precomposed U+00E9.
            SortedPair(PrecomposedAccent, DecomposedAccent)
                .Should().Equal(DecomposedAccent, PrecomposedAccent);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        static string[] SortedPair(string first, string second)
        {
            var workbook = new Workbook("test");
            var sheet = workbook.AddSheet("Sheet1");
            var ctx = new TestCommandContext(workbook);
            var sid = sheet.Id;

            sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue(first));
            sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue(second));

            var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1));
            new SortCommand(sid, range, sortByColOffset: 0, ascending: true)
                .Apply(ctx).Success.Should().BeTrue();

            return
            [
                ((TextValue)sheet.GetValue(1, 1)!).Value,
                ((TextValue)sheet.GetValue(2, 1)!).Value,
            ];
        }
    }
}
