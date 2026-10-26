using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r609: pasting a number must store what TYPING it stores.
///
/// <para>Excel keeps 15 significant decimal digits for a number that enters a cell, and FreeX
/// implements that as <see cref="ExcelNumericPrecision.CapSignificantDigits"/>. Typed entry applies
/// it at all three of its numeric branches (plain, fraction, percent in <c>CellEntryParser</c>), and
/// <c>PasteCommandFactory.ParseClipboardValue</c> applied it at NONE of its four -- while its own
/// comments say each branch mirrors the typed one ("exactly like typing '123 into a cell", "matching
/// the same first pass CellEntryParser uses for typed entry").</para>
///
/// <para>So the same characters produced two different doubles depending on how they arrived, and
/// the pasted one is a value Excel cannot hold: a workbook saved from it carries more precision than
/// Excel would, and <c>=A1=B1</c> over a typed and a pasted copy of one number answered FALSE.</para>
/// </summary>
public sealed class R609_PastedNumbersGetExcelsPrecisionRuleTests
{
    // 17 significant digits: more than a double can distinguish from its 15-digit neighbour, which
    // is exactly the range Excel's rule exists to normalise.
    private const string SeventeenDigits = "1.2345678901234567";

    private static double PastedNumber(string text) =>
        PasteCommandFactory.ParseClipboardValue(text).Should().BeOfType<NumberValue>().Subject.Value;

    [Theory]
    // These expectations are what ExcelNumericPrecision ACTUALLY does, per branch, and the two
    // branches differ: below 1e15 it rounds, at or above it zeroes the excess low-order digits.
    // R75 established the zeroing deliberately -- "Excel truncates -- zeroes -- excess low-order
    // integer digits unconditionally, it does not round them" -- and this test pins the paste path
    // to whatever that shared rule is, which is the invariant it exists for. The branch difference
    // is recorded as an open question in the r609 ledger entry, not settled here.
    [InlineData("1.2345678901234567", 1.23456789012346)]
    [InlineData("-1.2345678901234567", -1.23456789012346)]
    [InlineData("123456789012345678", 123456789012345000d)]
    public void APastedNumberIsCappedToFifteenSignificantDigits(string text, double expected)
    {
        PastedNumber(text).Should().Be(
            expected,
            "a pasted number must carry Excel.s precision, not a value Excel cannot hold");
    }

    [Fact]
    public void PastingAndTypingTheSameCharactersAgree()
    {
        // The invariant the paste factory's own comments claim. Compared against the rule rather
        // than against CellEntryParser directly, because that lives in a shell assembly this one
        // cannot reference -- the rule is the thing both are required to honour.
        var pasted = PastedNumber(SeventeenDigits);
        var typedEquivalent = ExcelNumericPrecision.CapSignificantDigits(
            double.Parse(SeventeenDigits, System.Globalization.CultureInfo.InvariantCulture));

        pasted.Should().Be(typedEquivalent);
    }

    [Theory]
    [InlineData("42", 42.0)]
    [InlineData("-7.5", -7.5)]
    [InlineData("0", 0.0)]
    [InlineData("1.5", 1.5)]
    public void OrdinaryNumbersAreUnchanged(string text, double expected) =>
        PastedNumber(text).Should().Be(expected, "the cap must not disturb values already inside 15 digits");

    [Fact]
    public void TextIsStillText() =>
        PasteCommandFactory.ParseClipboardValue("not a number").Should().BeOfType<TextValue>();
}
