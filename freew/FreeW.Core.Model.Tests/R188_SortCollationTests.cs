using System.Globalization;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r188-sort-collation: <see cref="ParagraphSort"/> compared text keys with
/// <c>StringComparer.Ordinal</c>/<c>OrdinalIgnoreCase</c>, which orders by UTF-16 code point and so
/// sorts every accented word after the whole ASCII alphabet. A user sorting a list or a table
/// column in a German or French locale saw those lines dumped at the end instead of alphabetized.
/// Word collates with the user's locale, and so now does FreeX's worksheet sort
/// (FreeX.Core.Commands.SortTextComparison); this is the FreeW half of the same fix.
/// </summary>
public class R188_SortCollationTests
{
    private static IDisposable UseCulture(string name)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(name);
        return new CultureScope(previous);
    }

    private sealed class CultureScope(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sort_AccentedText_AlphabetizesRatherThanAppending(bool caseSensitive)
    {
        using var _ = UseCulture("de-DE");

        var paragraphs = new[]
        {
            new Paragraph("zebra"),
            new Paragraph("east"),
            new Paragraph("élan"),
            new Paragraph("apple"),
        };

        var sorted = ParagraphSort.Sort(paragraphs, ascending: true, caseSensitive: caseSensitive);

        // Ordinally, the accented word sorts after "zebra" because its code point exceeds 'z'.
        sorted.Select(p => p.PlainText).Should().Equal("apple", "east", "élan", "zebra");
    }

    [Fact]
    public void Sort_KeysTheCultureCallsEqual_AreStillSeparatedAndAllRetained()
    {
        // The ordinal tie-break keeps the comparison a total order: culture collation reports 0 for
        // strings a user can tell apart, and a comparer that calls distinct values equal leaves the
        // result dependent on input order.
        using var _ = UseCulture("de-DE");

        var paragraphs = new[]
        {
            new Paragraph("coop"),
            new Paragraph("co-op"),
        };

        var sorted = ParagraphSort.Sort(paragraphs, ascending: true, caseSensitive: false);

        sorted.Should().HaveCount(2);
        sorted.Select(p => p.PlainText).Should().BeEquivalentTo(["coop", "co-op"]);
    }

    [Fact]
    public void SortRows_AccentedText_AlphabetizesRatherThanAppending()
    {
        using var _ = UseCulture("de-DE");

        var rows = new[]
        {
            RowWith("zebra"),
            RowWith("élan"),
            RowWith("apple"),
        };

        var sorted = ParagraphSort.SortRows(rows, gridColumn: 0, ascending: true, caseSensitive: false);

        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("apple", "élan", "zebra");
    }

    private static TableRow RowWith(string text)
    {
        var row = new TableRow();
        row.Cells.Add(new TableCell(text));
        return row;
    }
}
