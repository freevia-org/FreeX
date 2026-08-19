using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r152 (BACKLOG B2): PasteCommandFactory's locale-aware grouped-number paste pass
/// (<c>TryParseCultureGroupedNumber</c>) never allowed a currency symbol at all -- no
/// <c>NumberStyles.AllowCurrencySymbol</c> and no symbol token in its own hand-built grouping-shape
/// regex (<c>TryBuildCultureGroupingPattern</c>). The formula-text-coercion path
/// (<see cref="ExcelTextNumberParser"/>) already derives the symbol from
/// <c>culture.NumberFormat.CurrencySymbol</c> (fixed in r151, see
/// R151_TextNumberParserSpaceGroupingAndCurrencyTests), so "1.234,56 €"+0 already evaluates to
/// 1234.56 under de-DE -- but an ordinary Ctrl+V of the exact same text landed as literal text
/// instead of a number, because the paste path's separate en-US-only fallback
/// (<c>TryParseExcelPasteNumber</c>/<c>ValidGroupingRegex</c>) only ever recognizes a literal "$"
/// (by design -- mirrors real Excel's "$" is always the ASCII currency marker regardless of locale,
/// see CellEntryParser.TryParseCurrency's identical rule for typed entry) and never reaches a
/// non-"$" symbol under any culture.
///
/// Fixed by teaching <c>TryParseCultureGroupedNumber</c>/<c>TryBuildCultureGroupingPattern</c> to
/// recognize the culture's own leading/trailing currency symbol, read from the same
/// <c>culture.NumberFormat.CurrencySymbol</c> source of truth the formula engine already uses,
/// rather than growing a third from-scratch currency detector. Indian-numbering support
/// (NumberGroupSizes {3,2}) means this pass can't simply delegate its whole regex to
/// ExcelTextNumberParser.GetValidGroupingRegex (which assumes a fixed group size of 3), so the
/// symbol is threaded directly into this pass's own pattern builder instead.
/// </summary>
public sealed class R152_ClipboardPasteCultureCurrencySymbolTests
{
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    // ── The bug: a Euro-suffixed de-DE amount pasted with Ctrl+V ────────────────────────────

    [Fact]
    public void ParseClipboardValue_DeDe_EuroSuffixWithSpace_CoercesToNumber()
    {
        using var _ = new CurrentCultureScope("de-DE");

        var value = PasteCommandFactory.ParseClipboardValue("1.234,56 €");

        value.Should().BeOfType<NumberValue>(
            "Ctrl+V of a Euro-suffixed de-DE amount must become a number, exactly like the same "
            + "text already does via \"=text+0\" formula coercion");
        ((NumberValue)value).Value.Should().BeApproximately(1234.56, 1e-9);
    }

    [Fact]
    public void ParseClipboardValue_DeDe_EuroSuffixNoSpace_CoercesToNumber()
    {
        using var _ = new CurrentCultureScope("de-DE");

        var value = PasteCommandFactory.ParseClipboardValue("1.234,56€");

        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().BeApproximately(1234.56, 1e-9);
    }

    // ── Paths-agree: paste must land on the same value as formula-text coercion ─────────────

    [Theory]
    [InlineData("de-DE", "1.234,56 €")]
    [InlineData("de-DE", "1.234,56€")]
    public void ParseClipboardValue_AgreesWithFormulaTextCoercion_ForCurrencySuffixedAmounts(
        string cultureName, string text)
    {
        using var _ = new CurrentCultureScope(cultureName);

        var pasted = PasteCommandFactory.ParseClipboardValue(text);

        // The formula engine's own text-to-number coercion (="text"+0, matching
        // R151_TextNumberParserSpaceGroupingAndCurrencyTests.DeDeCulture_EuroCurrencySymbolWithSpace
        // _PlusZero_CoercesToNumber and its no-space sibling) is the ground truth this pass must
        // agree with -- both paths turn the same clipboard text into the same number.
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "S");
        var formulaResult = evaluator.Evaluate($"=\"{text}\"+0", sheet);

        pasted.Should().BeOfType<NumberValue>();
        formulaResult.Should().Be(pasted);
    }

    // ── Sibling no-regression: everything that already worked keeps working ─────────────────

    [Fact]
    public void ParseClipboardValue_DeDe_NoCurrency_StillCoercesToNumber()
    {
        // Sibling: the r151-fixed no-currency de-DE case must be untouched by adding currency
        // support.
        using var _ = new CurrentCultureScope("de-DE");

        var value = PasteCommandFactory.ParseClipboardValue("1.234,56");

        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().BeApproximately(1234.56, 1e-9);
    }

    [Fact]
    public void ParseClipboardValue_EnUs_DollarPrefixed_StillCoercesToNumber()
    {
        // Sibling: en-US's own currency symbol is "$" both for the culture-aware pass (new) and
        // the pre-existing en-US-only fallback pass -- either way this must keep working.
        using var _ = new CurrentCultureScope("en-US");

        var value = PasteCommandFactory.ParseClipboardValue("$1,234.56");

        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().BeApproximately(1234.56, 1e-9);
    }

    [Fact]
    public void ParseClipboardValue_EnIn_IndianGroupingWithRupeeSymbol_CoercesToNumber()
    {
        // Sibling: currency support must not break Indian-numbering grouping ({3,2}) -- the whole
        // reason this pass can't simply delegate to ExcelTextNumberParser's fixed-group-size-3
        // regex.
        using var _ = new CurrentCultureScope("en-IN");
        var symbol = CultureInfo.GetCultureInfo("en-IN").NumberFormat.CurrencySymbol;

        var value = PasteCommandFactory.ParseClipboardValue(symbol + "1,23,456.50");

        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().BeApproximately(123456.50, 1e-9);
    }

    [Fact]
    public void ParseClipboardValue_DeDe_MalformedGroupingWithCurrency_StillRejectedAsText()
    {
        // Sibling: a currency symbol must not relax the existing grouping-shape validation --
        // "1.23,56 €" has a malformed leading group and must stay text, not be silently misparsed.
        using var _ = new CurrentCultureScope("de-DE");

        var value = PasteCommandFactory.ParseClipboardValue("1.23,56 €");

        value.Should().BeOfType<TextValue>();
    }

    [Fact]
    public void ParseClipboardValue_DeDe_WrongCurrencySymbol_StillRejectedAsText()
    {
        // Sibling: only the CURRENT culture's own symbol is recognized -- a dollar-suffixed
        // amount under de-DE (whose own symbol is €, not $) still isn't a currency-shaped match
        // for this pass, and the separate en-US fallback pass requires en-US comma/dot grouping
        // (not de-DE's dot-grouped/comma-decimal shape), so it stays text end to end.
        using var _ = new CurrentCultureScope("de-DE");

        var value = PasteCommandFactory.ParseClipboardValue("1.234,56 $");

        value.Should().BeOfType<TextValue>();
    }
}
