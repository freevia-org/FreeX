using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R29-localization-resx-culture-3: <see cref="PdfTableReader"/>'s cell-coercion helper
/// (<c>TryParseFiniteNumber</c>, exercised via <see cref="PdfTableReader.CoerceValue"/>) must not
/// silently misparse a dot-decimal number under a comma-decimal current culture.
///
/// Under de-DE (group separator '.', decimal separator ','), <c>double.TryParse("12.34",
/// NumberStyles.Any, ...)</c> happily returns 1234 -- .NET's grouping validation does not check that
/// group separators fall on 3-digit boundaries, so the '.' is treated as a thousands separator and the
/// fractional part is silently dropped (a 100x magnitude corruption). The fix ports
/// DelimitedTextWorkbookReader's HasValidGroupingShape guard so a malformed grouping shape is rejected
/// under CurrentCulture and the parse falls through to the InvariantCulture attempt instead.
/// </summary>
public sealed class R29_PdfTableReaderLocaleNumberParsingTests
{
    [Fact]
    public void CoerceValue_DotDecimalNumberUnderCommaDecimalCulture_ParsesAsInvariantDecimal()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        // Under de-DE this would otherwise misparse to 1234 (dot treated as group separator).
        var value = PdfTableReader.CoerceValue("12.34");

        value.Should().Be(new NumberValue(12.34));
    }

    [Fact]
    public void CoerceValue_DotDecimalNumberWithThousandsGroupUnderCommaDecimalCulture_ParsesAsInvariantDecimal()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        // "1,234.5" (en-US-style grouped decimal) must not be misread via de-DE grouping rules either.
        var value = PdfTableReader.CoerceValue("1,234.5");

        value.Should().Be(new NumberValue(1234.5));
    }

    [Fact]
    public void CoerceValue_CommaDecimalNumberUnderCommaDecimalCulture_StillParsesCorrectly()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        // Sibling already-working case: a genuine de-DE-authored PDF ("1.234,56") must keep parsing
        // via CurrentCulture, i.e. the fix must not break the legitimate comma-decimal path.
        var value = PdfTableReader.CoerceValue("1.234,56");

        value.Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void CoerceValue_DotDecimalNumberUnderInvariantCulture_StillParsesCorrectly()
    {
        using var cultureScope = TestCultureScope.InvariantCurrentCulture();

        // Sibling already-working case: plain dot-decimal numbers under a dot-decimal culture must
        // keep working unaffected by the new grouping-shape guard.
        var value = PdfTableReader.CoerceValue("12.34");

        value.Should().Be(new NumberValue(12.34));
    }
}
