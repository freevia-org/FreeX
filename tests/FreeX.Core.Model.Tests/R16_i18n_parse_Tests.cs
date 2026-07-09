using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 16 i18n parsing fixes: user-entered Data Validation numeric bounds must honor the
/// current UI culture (not just invariant), while the resulting stored/compared value stays a
/// culture-neutral double so file persistence (which is always invariant) is unaffected.
/// Covers finding R16-rtl-i18n-parsing-1.
/// </summary>
public sealed class R16_i18n_parse_Tests
{
    [Fact]
    public void TryParseNumberBound_DeDECommaDecimalBound_ParsesAsOneAndAHalf_NotFifteen()
    {
        using var _ = new CultureScope("de-DE");

        // Before the fix, "1,5" was parsed invariant-only: the comma was read as a thousands
        // separator and stripped, silently yielding 15 instead of the user's intended 1.5.
        DataValidationBoundsParser.TryParseNumberBound("1,5", out var value).Should().BeTrue();
        value.Should().Be(1.5);

        // The parsed value is a plain double - inherently culture-neutral - so persisting it
        // (e.g. re-serializing to the XLSX/model layer, which is always invariant) round-trips
        // correctly regardless of the UI culture used to type it.
        value.ToString(CultureInfo.InvariantCulture).Should().Be("1.5");
    }

    [Fact]
    public void TryParseNumberBound_DeDEGroupedIntegerBound_ParsesCorrectly()
    {
        using var _ = new CultureScope("de-DE");

        DataValidationBoundsParser.TryParseNumberBound("1.234", out var value).Should().BeTrue();
        value.Should().Be(1234);
    }

    [Fact]
    public void TryParseNumberBound_EnUSBounds_StillParseCorrectly()
    {
        using var _ = new CultureScope("en-US");

        DataValidationBoundsParser.TryParseNumberBound("1,234", out var grouped).Should().BeTrue();
        grouped.Should().Be(1234);

        DataValidationBoundsParser.TryParseNumberBound("1.5", out var decimalValue).Should().BeTrue();
        decimalValue.Should().Be(1.5);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string cultureName) =>
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
