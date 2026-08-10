using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R34-meta-2: R33 fixed the 15-significant-digit truncation for values with more than 15 integer
/// digits (scale &lt; 0) only in DelimitedTextWorkbookReader (CSV import). CellEntryParser's own
/// copy of RoundToSignificantDigits (used for direct cell entry) still clamped scale to
/// [0, 15] and then no-op rounded, leaving an 18-digit typed integer un-truncated -- unlike real
/// Excel, which zeroes the low-order digits beyond its 15-significant-digit storage cap.
/// </summary>
public sealed class CellEntryParserLargeIntegerSignificantDigitTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_TruncatesAnEighteenDigitTypedIntegerToFifteenSignificantDigits()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("123456789012345678", Anchor, useR1C1ReferenceStyle: false);

        var value = cell.Value.Should().BeOfType<NumberValue>().Which.Value;

        // Excel zeroes the low-order 3 digits beyond its 15-significant-digit storage cap rather
        // than leaving the value un-truncated.
        value.Should().Be(123456789012345000d);
    }

    [Fact]
    public void CreateCell_LeavesANormalIntegerUnaffectedByTheCap()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("123456789", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().Be(123456789d);
    }

    // R35-meta-1: the R34 significant-digit rewrite dropped the UPPER clamp on `scale`, so a
    // small-magnitude value (|value| < 0.1 => scale > 15) fed Math.Round(double, scale>15) which
    // throws ArgumentOutOfRangeException -- a crash for one of the most common numeric shapes.
    [Theory]
    [InlineData("0.05", 0.05)]
    [InlineData("0.001", 0.001)]
    [InlineData("0.099", 0.099)]
    [InlineData("0.1", 0.1)]
    [InlineData("-0.0005", -0.0005)]
    public void CreateCell_SmallMagnitudeDecimal_DoesNotThrowAndKeepsValue(string typed, double expected)
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell(typed, Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-15);
    }

    [Fact]
    public void CreateCell_TinyFiniteNumber_RemainsNonzero()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");

        var cell = CellEntryParser.CreateCell("5E-200", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().Be(new NumberValue(5e-200));
    }
}
