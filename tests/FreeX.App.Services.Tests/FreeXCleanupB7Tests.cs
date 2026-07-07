using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for cleanup batch B7 findings P57 and P58 in
/// Free.Shared.AppServices/NumberFormatDecimalAdjuster.cs.
/// </summary>
public sealed class FreeXCleanupB7Tests
{
    [Theory]
    // P57: Decrease Decimal on a format whose decimal run ends in '#' must trim the run itself
    // (respecting '#'/'?' placeholders), not fall through to the literal-digit-only regex, which
    // used to strip the whole ".0" run and leave a corrupt two-position integer mask "0#" (a value
    // of 7 would then render as "07" instead of the correct single decimal place).
    [InlineData("0.0#", "0.0")]
    // Previously matched nothing at all (dead no-op); must now trim one placeholder from the run.
    [InlineData("0.##", "0.#")]
    [InlineData("#.##", "#.#")]
    [InlineData("0.#", "0")]
    public void RemoveDecimalPlace_TrimsMixedPlaceholderRuns_WithoutCorruptingIntegerMask(string format, string expected)
    {
        var result = NumberFormatDecimalAdjuster.RemoveDecimalPlace(format);

        result.Should().Be(expected);
        // Guard against the specific documented regression: "0.0#" must never collapse to "0#",
        // which NumberFormatter renders as a corrupt two-digit integer mask ("07" for a value of 7).
        result.Should().NotBe("0#");
    }

    [Theory]
    // P58: Increase Decimal must be a no-op on formats with no adjustable numeric placeholder,
    // matching Excel. The old fallback (`format + ".0"`) injected a literal '0' into date/time
    // formats, which flips them out of the date-rendering path and produces garbage display text,
    // and turned the Text format "@" into "@.0" (rendering "hello.0" instead of "hello").
    [InlineData("mm/dd/yyyy")]
    [InlineData("h:mm")]
    [InlineData("m/d/yyyy h:mm")]
    [InlineData("@")]
    public void AddDecimalPlace_IsNoOpOnDateAndTextFormats(string format)
    {
        var result = NumberFormatDecimalAdjuster.AddDecimalPlace(format);

        result.Should().Be(format);
        result.Should().NotContain(".0");
    }

    [Fact]
    public void AddDecimalPlace_StillAdjustsGenuineNumericFormats()
    {
        // Regression guard: the P58 fix must not turn AddDecimalPlace into a no-op for ordinary
        // numeric formats that have no literal digit run to extend but do have a '?' placeholder,
        // nor for formats picked up by the existing digit-run/insert-".0" fallback semantics.
        NumberFormatDecimalAdjuster.AddDecimalPlace("0").Should().Be("0.0");
        NumberFormatDecimalAdjuster.AddDecimalPlace("#,##0").Should().Be("#,##0.0");
    }
}
