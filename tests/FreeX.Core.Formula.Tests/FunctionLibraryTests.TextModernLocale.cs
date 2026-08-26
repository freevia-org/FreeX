using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R36-formula-text-modern-3-2 / R36-formula-text-modern-3-3: DOLLAR/FIXED hardcoded
// CultureInfo.InvariantCulture for their grouping/decimal separators (and DOLLAR's currency
// symbol), and NUMBERVALUE hardcoded "."/"," for its omitted decimal_separator/group_separator
// arguments, instead of reading CultureInfo.CurrentCulture as real Excel does per its documented
// locale-aware Remarks for FIXED/DOLLAR/NUMBERVALUE. These tests pin the de-DE (group=".",
// decimal=",", currency="€") behavior plus an en-US sibling to guard against regressing the
// still-common US-locale defaults.
public partial class FunctionLibraryTests
{
    [Fact]
    public void Fixed_DeDeCulture_UsesCultureGroupAndDecimalSeparators()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=FIXED(1234.5,1)", MakeSheet())
            .Should().Be(new TextValue("1.234,5"));
    }

    [Fact]
    public void Fixed_EnUsCulture_StillUsesUsGroupAndDecimalSeparators()
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=FIXED(1234.5,1)", MakeSheet())
            .Should().Be(new TextValue("1,234.5"));
    }

    [Fact]
    public void Dollar_DeDeCulture_UsesCultureSeparatorsAndCurrencySymbol()
    {
        using var culture = new TestCultureScope("de-DE");

        // de-DE's CurrencyPositivePattern is 3 ("n $": symbol AFTER the amount with a
        // separating space, e.g. real German currency display "1.234,50 €") -- R74-formula-
        // text-format-4-2 fixed DOLLAR() to honor that instead of always prepending the symbol.
        _eval.Evaluate("=DOLLAR(1234.5,2)", MakeSheet())
            .Should().Be(new TextValue("1.234,50 €"));
    }

    [Fact]
    public void Dollar_DeDeCulture_NegativeValueWrapsCultureFormattedAmountInParens()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=DOLLAR(-1234.5,2)", MakeSheet())
            .Should().Be(new TextValue("(1.234,50 €)"));
    }

    [Fact]
    public void Dollar_EnUsCulture_StillUsesDollarSignAndUsSeparators()
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=DOLLAR(1234.5,2)", MakeSheet())
            .Should().Be(new TextValue("$1,234.50"));
    }

    [Fact]
    public void Dollar_InvariantHeadlessCulture_UsesExcelUsBaseline()
    {
        using var culture = TestCultureScope.InvariantCurrentCulture();

        _eval.Evaluate("=DOLLAR(1234.5,2)", MakeSheet())
            .Should().Be(new TextValue("$1,234.50"));
        _eval.Evaluate("=VALUE(\"$1,234.50\")", MakeSheet())
            .Should().Be(new NumberValue(1234.5));
    }

    [Fact]
    public void Numbervalue_DeDeCulture_DefaultsToCultureSeparatorsWhenOmitted()
    {
        using var culture = new TestCultureScope("de-DE");

        _eval.Evaluate("=NUMBERVALUE(\"1.234,56\")", MakeSheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void Numbervalue_EnUsCulture_StillDefaultsToUsSeparatorsWhenOmitted()
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=NUMBERVALUE(\"1,234.56\")", MakeSheet())
            .Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void Numbervalue_DeDeCulture_ExplicitSeparatorArgumentsOverrideCultureDefault()
    {
        using var culture = new TestCultureScope("de-DE");

        // Even under de-DE, explicitly-passed separators must still be honored verbatim
        // (US-style separators passed as explicit args while the current culture is de-DE).
        _eval.Evaluate("=NUMBERVALUE(\"1,234.56\",\".\",\",\")", MakeSheet())
            .Should().Be(new NumberValue(1234.56));
    }
}
