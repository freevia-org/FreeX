using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R32-rendering-numberformat-display-2: column-width fill for a currency-symbol-prefixed
// "_)"-only format (no asterisk) wrongly inserted padding BETWEEN the symbol and the digits
// via the genuine asterisk-stretch insertion path (FindAccountingFillInsertionIndex), even
// though the format has no "*" stretch directive at all.
//
// Excel:  built-in Currency id 5 "$#,##0_);($#,##0)" applied to 100 at column width 8 renders
// "$100 " (the value plus the single fixed "_)" reserve space) -- it must NOT stretch/insert a
// gap after the "$" to fill the remaining column width.
public class R32_NumberFormatAccountingFillTests
{
    [Fact]
    public void CurrencyPrefixed_UnderscoreOnlyFill_DoesNotInsertGapAfterSymbol()
    {
        // Pre-fix: NumberFormatter.Format(new NumberValue(100), "$#,##0_)", 8) == "$    100"
        // (mid-string stretch meant only for the genuine "$* " asterisk idiom).
        var result = NumberFormatter.Format(new NumberValue(100), "$#,##0_)", 8);

        result.Should().Be("$100 ");
    }

    [Fact]
    public void NoSymbolPrefix_UnderscoreOnlyFill_StillReservesSingleTrailingSpace()
    {
        // Sibling already-working case: no currency-symbol prefix, so there was never a
        // mid-string insertion point -- must remain unaffected by the fix.
        var result = NumberFormatter.Format(new NumberValue(100), "#,##0_)", 8);

        result.Should().Be("100 ");
    }

    [Fact]
    public void CurrencyPrefixed_GenuineAsteriskStretch_StillFillsBetweenSymbolAndDigits()
    {
        // Sibling genuine accounting idiom (built-in Currency-Accounting id 44 positive
        // section) -- the "$* " asterisk directive DOES mean "stretch to fill the column",
        // and must still insert the fill between the symbol and the digits. (Same
        // format/value/width already pinned by NumberFormatterTests.
        // AccountingSubset_ExpandsFillSpaceToRequestedCharacterWidth.)
        const string format = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";

        var result = NumberFormatter.Format(new NumberValue(1234.5), format, 14);

        result.Should().Be("$     1,234.50");
    }
}
