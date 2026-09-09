using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r563: r562 disproved the claim that "a cell value cannot be non-finite because the evaluator
/// returns #NUM!" by finding one writer -- an external link's cached sheet data -- that builds a
/// NumberValue without going past the evaluator. This is the sibling sweep of that disproof: the
/// other doors into a cell value are the TYPED entry parser and the PASTE parser, both of which
/// take text from outside the workbook.
///
/// <para>Both came back guarded, each in its own way: CellEntryParser's plain path is literally
/// named TryParseFiniteNumber, its currency path carries an explicit IsFinite, and its fraction
/// path parses integers and rejects a zero denominator so the quotient cannot be non-finite.
/// PasteCommandFactory guards all four of its branches, including inside
/// TryParseCultureGroupedNumber, whose check sits at the end of the helper rather than beside the
/// NumberValue construction -- which is exactly why reading the call site alone was not enough.</para>
///
/// <para>A clean sweep decays the moment someone adds the next parser to either path, so the
/// result is kept as a CENSUS rather than a note: every hostile spelling is driven through the
/// public entry point, and a future branch that admits a non-finite value fails here without
/// anyone remembering to check.</para>
/// </summary>
public sealed class R563_TypedAndPastedValuesAreFiniteTests
{
    public static TheoryData<string> HostileEntries() =>
    [
        "NaN",
        "Infinity",
        "-Infinity",
        "1e400",
        "-1e400",
        new string('9', 400),
        "1e400%",
        "$1e400",
        "1e400 1/2",
        "1 1/0",
        // A long DIGIT RUN rather than an exponent, because NumberStyles.Currency and the percent
        // path do not accept an exponent at all: "$1e400" never parses and falls through to text,
        // so it would exercise nothing. r559's rule -- NumberStyles constrains the SPELLING of a
        // number, never its SIZE -- applies to the test inputs just as much as to the code.
        "$" + new string('9', 400),
        new string('9', 400) + "%",
        new string('9', 400) + " 1/2",
    ];

    [Theory]
    [MemberData(nameof(HostileEntries))]
    public void A_typed_entry_never_becomes_a_non_finite_number(string text)
    {
        var value = CellEntryParser.ParseScalarValue(text);

        // Text is a perfectly good answer; a non-finite NUMBER is not.
        (value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("typing \"" + text + "\" produced " + value);
    }

    [Theory]
    [MemberData(nameof(HostileEntries))]
    public void A_pasted_entry_never_becomes_a_non_finite_number(string text)
    {
        var cell = CellEntryParser.CreateCell(
            text,
            new CellAddress(default, 1, 1),
            useR1C1ReferenceStyle: false,
            workbook: null);

        (cell.Value is not NumberValue number || double.IsFinite(number.Value))
            .Should().BeTrue("committing \"" + text + "\" produced " + cell.Value);
    }

    [Theory]
    [InlineData("42", 42d)]
    [InlineData("-17.5", -17.5d)]
    [InlineData("50%", 0.5d)]
    [InlineData("$5", 5d)]
    [InlineData("1 1/2", 1.5d)]
    public void Ordinary_entries_still_become_the_numbers_they_look_like(string text, double expected)
    {
        // Non-vacuity, and it covers every branch the census drives: plain, percent, currency and
        // fraction. A guard that rejected everything would satisfy the assertions above while
        // turning every typed number into text.
        CellEntryParser.ParseScalarValue(text)
            .Should().BeOfType<NumberValue>()
            .Which.Value.Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void A_zero_denominator_fraction_never_becomes_infinity()
    {
        // The fraction path is the one place a non-finite value could arise from ARITHMETIC rather
        // than from parsing, so it is pinned separately. TryParseMixedFraction rejects a zero
        // denominator outright, so the quotient is never evaluated.
        //
        // My first draft asserted TextValue here and FAILED: "1 1/0" falls past the fraction
        // parser into DateTime.TryParse, which accepts it, so the answer is a DateTimeValue. That
        // is a question about date-parse leniency, not about this class, and it is NOT asserted
        // either way here -- the census checks the property it exists to check.
        var value = CellEntryParser.ParseScalarValue("1 1/0");

        (value is not NumberValue number || double.IsFinite(number.Value)).Should().BeTrue(
            "\"1 1/0\" produced " + value);
    }
}
