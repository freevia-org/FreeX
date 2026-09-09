namespace FreeW.Core.Model.Tests;

/// <summary>
/// r557: <see cref="ParagraphSort"/>'s numeric sort states its contract in its own comment --
/// "unparseable keys sort after parseable ones" -- and decides which is which with
/// <c>double.TryParse</c>. That call accepts the literal "NaN" and "Infinity" whatever
/// NumberStyles it is given, and a long run of digits overflows to Infinity even though
/// <c>NumberStyles.Number</c> forbids an exponent (the route r550 found in ZoomPercentPolicy).
/// A paragraph reading "NaN" was therefore treated as a NUMBER and, since <c>NaN.CompareTo</c>
/// puts it below everything, sorted ahead of every real one.
///
/// <para>Word extracts a leading NUMBER when sorting numerically; these tokens are not numbers to
/// it. This is the FreeW half of r556's identical finding in FreeX's AutoFilter checklist -- an
/// alignment fix rather than a crash fix, because the comparison uses <c>double.CompareTo</c> and
/// stays a consistent total order either way.</para>
/// </summary>
public class R557_NumericSortTreatsNonNumbersAsUnparseableTests
{
    private static IReadOnlyList<string> SortNumerically(params string[] lines) =>
        ParagraphSort
            .Sort(
                lines.Select(line => new Paragraph(line)).ToArray(),
                SortKind.Number,
                ascending: true,
                caseSensitive: false,
                hasHeaderRow: false)
            .Select(p => p.PlainText)
            .ToList();

    // Each case carries an UNPARSEABLE SENTINEL chosen so the two behaviours order differently.
    // Without one, "Infinity" and a long digit run pass either way: treated as numbers they are
    // the largest value and land last, exactly where correct unparseable placement puts them.
    // My first draft asserted only "after the numbers" and so was vacuous for those two -- it went
    // green against the unfixed code, which is the same trap r556 recorded one round earlier.
    [Theory]
    [InlineData("NaN", "AAA", "AAA", "NaN")]
    [InlineData("Infinity", "AAA", "AAA", "Infinity")]
    [InlineData("-Infinity", "zzz", "-Infinity", "zzz")]
    public void A_token_that_is_not_a_number_sorts_with_the_unparseable_keys(
        string hostile,
        string sentinel,
        string expectedThird,
        string expectedFourth)
    {
        // Parseable keys first in numeric order, then BOTH unparseable keys in text order.
        SortNumerically("10", hostile, "2", sentinel)
            .Should().Equal("2", "10", expectedThird, expectedFourth);
    }

    [Fact]
    public void A_run_of_digits_too_long_to_represent_sorts_with_the_unparseable_keys()
    {
        // No exponent required: NumberStyles.Number forbids one and 400 digits overflow anyway.
        // "0abc" is unparseable and sorts before the digit run in text order, so the numeric and
        // unparseable placements differ.
        var huge = new string('9', 400);

        SortNumerically("10", huge, "2", "0abc").Should().Equal("2", "10", "0abc", huge);
    }

    [Fact]
    public void Ordinary_numbers_still_sort_numerically_not_lexically()
    {
        // Non-vacuity: a guard that made every key unparseable would satisfy the assertions above
        // while destroying numeric sorting -- lexically "10" precedes "2".
        SortNumerically("10", "2", "30").Should().Equal("2", "10", "30");
    }

    [Fact]
    public void Ordinary_unparseable_text_still_sorts_after_the_numbers()
    {
        // Pins the pre-existing contract the fix reuses.
        SortNumerically("apple", "5").Should().Equal("5", "apple");
    }
}
