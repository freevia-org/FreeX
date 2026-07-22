using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R74-formula-text-format-4-1: BuiltInFunctions.TextCore.Format.cs's TextFormatValue only routed
/// NumberValue/DateTimeValue through the NumberFormatter engine, so TEXT(value, fmt) discarded the
/// format code entirely for a text input (returning ToText(val) verbatim) -- e.g.
/// TEXT("A1","""SKU ""@") returned "A1" instead of "SKU A1". Excel applies the format's text
/// section ('@' placeholder plus any literal/escaped text) to a text value, exactly like
/// NumberFormatter.FormatTextWithColor already implements (proven by
/// NumberFormatterTests.CustomNumberSubset_AppliesSingleTextSectionWhenItContainsPlaceholder in
/// FreeX.Core.Calc.Tests). Fixed by routing a TextValue through NumberFormatter.Format too.
///
/// R74-formula-text-format-4-2: BuiltInFunctions.TextCore.Format.cs's DollarScalar always
/// prepended CurrencySymbol regardless of locale, so DOLLAR() on a culture whose
/// CurrencyPositivePattern places the symbol AFTER the number (pattern 3, "n $" -- e.g. fr-FR,
/// de-DE) still showed the symbol first. Fixed by placing the symbol per
/// CultureInfo.CurrentCulture.NumberFormat.CurrencyPositivePattern (negative amounts keep the
/// existing accounting-parentheses wrapping around that same locale-aware layout).
///
/// R74-formula-text-format-4-3: BuiltInFunctions.TextPhaseA1.cs's NumbervalueScalar substituted
/// the declared decimal_separator with '.' but never validated that a stray, undeclared '.'
/// wasn't already present in the input -- so NUMBERVALUE("3.5","X","Y") silently parsed via
/// double.TryParse's InvariantCulture '.' handling instead of returning #VALUE!. Fixed by
/// rejecting any '.' that is neither the declared decimal_separator nor group_separator.
/// </summary>
public sealed class R74_FmlTextFormatFixesTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    // ── TEXT() applies the format's text section to a TextValue input ──────────

    [Fact]
    public void Text_QuotedLiteralPrefixThenAtPlaceholder_AppliesToTextValue()
    {
        _eval.Evaluate("=TEXT(\"A1\",\"\"\"SKU \"\"@\")", MakeSheet())
            .Should().Be(new TextValue("SKU A1"));
    }

    [Fact]
    public void Text_AtPlaceholderThenQuotedLiteralSuffix_AppliesToTextValue()
    {
        _eval.Evaluate("=TEXT(\"A1\",\"@ \"\"units\"\"\")", MakeSheet())
            .Should().Be(new TextValue("A1 units"));
    }

    [Fact]
    public void Text_EscapedAtLiteralThenAtPlaceholder_AppliesToTextValue()
    {
        _eval.Evaluate("=TEXT(\"A1\",\"\\@@\")", MakeSheet())
            .Should().Be(new TextValue("@A1"));
    }

    [Fact]
    public void Text_NumericOnlyFormatOnTextValue_ReturnsTextUnchanged()
    {
        // A format with no '@' placeholder does not apply to a text value at all -- Excel
        // returns the text verbatim, matching FormatTextWithColor's sections.Length==1 rule.
        _eval.Evaluate("=TEXT(\"A1\",\"0.00\")", MakeSheet())
            .Should().Be(new TextValue("A1"));
    }

    [Fact]
    public void Text_NumericFormatOnNumberValue_StillFormatsAsBefore()
    {
        _eval.Evaluate("=TEXT(5,\"0.00\")", MakeSheet())
            .Should().Be(new TextValue("5.00"));
    }

    // ── DOLLAR() honors the locale's CurrencyPositivePattern symbol placement ──

    [Fact]
    public void Dollar_FrFrCulture_PlacesSymbolAfterAmountPerLocalePattern()
    {
        using var culture = new TestCultureScope("fr-FR");

        // fr-FR's NumberGroupSeparator is a narrow no-break space (U+202F), not a plain U+0020
        // space -- build the expected grouped digits via the culture itself so this assertion
        // doesn't depend on hardcoding that exact separator character.
        var group = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
        _eval.Evaluate("=DOLLAR(1234.567,2)", MakeSheet())
            .Should().Be(new TextValue($"1{group}234,57 €"));
    }

    [Fact]
    public void Dollar_EnUsCulture_StillPlacesSymbolBeforeAmount()
    {
        using var culture = new TestCultureScope("en-US");

        _eval.Evaluate("=DOLLAR(1234.567,2)", MakeSheet())
            .Should().Be(new TextValue("$1,234.57"));
    }

    [Fact]
    public void Dollar_FrFrCulture_NegativeValueWrapsLocaleFormattedAmountInParens()
    {
        using var culture = new TestCultureScope("fr-FR");

        var group = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;
        _eval.Evaluate("=DOLLAR(-1234.567,2)", MakeSheet())
            .Should().Be(new TextValue($"(1{group}234,57 €)"));
    }

    // ── NUMBERVALUE() rejects a stray, undeclared decimal point ────────────────

    [Fact]
    public void Numbervalue_StrayDotNotMatchingEitherDeclaredSeparator_ReturnsValueError()
    {
        _eval.Evaluate("=NUMBERVALUE(\"3.5\",\"X\",\"Y\")", MakeSheet())
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Numbervalue_CommaDecimalSpaceGroup_StrayDotStillRejected()
    {
        _eval.Evaluate("=NUMBERVALUE(\"3.5\",\",\",\" \")", MakeSheet())
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Numbervalue_CommaDecimalDotGroup_ParsesCorrectly()
    {
        _eval.Evaluate("=NUMBERVALUE(\"3,5\",\",\",\".\")", MakeSheet())
            .Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Numbervalue_CommaDecimalSpaceGroup_GroupedValueParsesCorrectly()
    {
        _eval.Evaluate("=NUMBERVALUE(\"1 234,5\",\",\",\" \")", MakeSheet())
            .Should().Be(new NumberValue(1234.5));
    }

    [Fact]
    public void Numbervalue_DefaultDotDecimalSeparator_StillParsesUnchanged()
    {
        _eval.Evaluate("=NUMBERVALUE(\"3.5\")", MakeSheet())
            .Should().Be(new NumberValue(3.5));
    }
}
