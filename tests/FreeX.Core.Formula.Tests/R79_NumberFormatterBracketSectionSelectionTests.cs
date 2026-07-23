using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R79-io-numfmt-codes-5-1 / 5-2: two custom-format section bugs in NumberFormatter.
///
/// 5-1: a single-section format (no ';') whose first character is a bracket token -- a
/// locale/currency token like [$-409]/[$$-409], a color like [Red], or a DBNum/NatNum
/// directive -- used to skip FormatNumber's sign-extracting fast path entirely (that path
/// only triggered when sections[0][0] != '['). It fell through to ParseSections/
/// SelectPositionalSection, which for a length-1 array returns the raw (still-negative)
/// value unchanged, letting .NET's own numeric formatting auto-insert "-" *inside* the
/// numeric text -- so the already-extracted locale/literal prefix landed BEFORE that
/// auto-inserted minus (e.g. "$-12.30" instead of Excel's "-$12.30", where the sign always
/// sits before any prefix for a single/unsectioned format).
///
/// 5-2: when every section of a custom format carries an explicit [condition] (e.g.
/// "[>=100]\"big\";[<0]\"neg\"") and a value satisfies none of them, there is no
/// unconditioned section to fall back to. FormatNumber's hasConditions branch used to
/// default to `selectedIndex = 0` in that case, applying the FIRST conditioned section's
/// format/color unconditionally. Excel instead renders the value with the plain General
/// format (uncolored) since no section actually applies.
/// </summary>
public class R79_NumberFormatterBracketSectionSelectionTests
{
    [Fact]
    public void SingleSection_LeadingLocaleCurrencyBracket_NegativeValue_SignBeforePrefix()
    {
        // "[$$-409]0.00" is a single-section (no ';') USD locale-currency format. Excel shows
        // the sign before the currency prefix: "-$12.30", not "$-12.30".
        NumberFormatter.Format(new NumberValue(-12.3), "[$$-409]0.00").Should().Be("-$12.30");
    }

    [Fact]
    public void SingleSection_LeadingColorBracket_NegativeValue_SignBeforeLiteralPrefix()
    {
        // Same defect, not locale-specific: any bracket-led single section with a literal
        // prefix must place the sign before that prefix.
        NumberFormatter.Format(new NumberValue(-12.3), "[Red]\"$\"0.00").Should().Be("-$12.30");
    }

    [Fact]
    public void SingleSection_LeadingLocaleCurrencyBracket_PositiveValue_SiblingNoRegression()
    {
        // Positive values are unaffected -- no sign to place either way.
        NumberFormatter.Format(new NumberValue(12.3), "[$$-409]0.00").Should().Be("$12.30");
    }

    [Fact]
    public void SingleSection_NoBracket_NegativeValue_SiblingNoRegression()
    {
        // The pre-existing fast path for a bracket-free single section must keep working
        // exactly as before.
        NumberFormatter.Format(new NumberValue(-12.3), "\"$\"0.00").Should().Be("-$12.30");
    }

    [Fact]
    public void AllSectionsConditioned_NoConditionMatches_FallsBackToGeneral()
    {
        // 50 matches neither ">=100" nor "<0" -- Excel falls back to plain General ("50"),
        // not the first conditioned section's text ("big").
        NumberFormatter.Format(new NumberValue(50), "[>=100]\"big\";[<0]\"neg\"").Should().Be("50");
    }

    [Fact]
    public void AllSectionsConditioned_ConditionMatches_SiblingNoRegression()
    {
        // A value that DOES satisfy one of the explicit conditions still selects that
        // section's format -- unaffected by the General-fallback fix.
        NumberFormatter.Format(new NumberValue(150), "[>=100]\"big\";[<0]\"neg\"").Should().Be("big");
    }
}
