using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R143 numfmt-single-section-fraction-spacing-leak: a single-section (no ';') custom
/// fraction format like "_(# ?/?_)" reaches FormatNumber's fast single-section path
/// (NumberFormatter.cs), which used to hand the RAW format straight to
/// IsSimpleFractionFormat/FormatSimpleFraction without first stripping "_x"/"*x"
/// spacing/fill directives. FormatSimpleFraction's own affix extraction (ExtractNumericAffixes)
/// treats '_' and '*' as ordinary literal characters, so the underscore/asterisk and the
/// character they reserve/repeat leaked into the rendered text verbatim (e.g. "_(2 1/2_)"
/// instead of Excel's invisible-padding "2 1/2"). The multi-section path (ApplyNumericFormat)
/// already stripped these directives via PreserveAccountingFillSpace + RemoveSpacingAndFillDirectives
/// before reaching its own IsSimpleFractionFormat/FormatSimpleFraction call -- the fast
/// single-section path now reuses the same two helpers instead of duplicating the logic.
/// </summary>
public class R143_NumberFormatterSingleSectionFractionSpacingDirectiveTests
{
    [Fact]
    public void SingleSection_UnderscoreParenFractionFormat_DoesNotLeakUnderscoreOrParenLiterally()
    {
        // "_(# ?/?_)" is a single-section (no ';') format: "_(" reserves a space the width of
        // "(" and "_)" reserves a space the width of ")" -- both are invisible padding in Excel,
        // never literal underscore/parenthesis characters in the rendered text.
        var result = NumberFormatter.Format(new NumberValue(2.5), "_(# ?/?_)");

        result.Should().NotContain("_");
        result.Should().NotContain("(");
        result.Should().NotContain(")");
        // Matches exactly what the multi-section path (ApplyNumericFormat, which already
        // strips these directives) produces for the identical format placed in both sections
        // of "_(# ?/?_);_(# ?/?_)" -- confirmed directly against that path below.
        result.Should().Be("2 1/2 ");
    }

    [Fact]
    public void SingleSection_UnderscoreParenFractionFormat_MatchesMultiSectionPathForSameFormat()
    {
        // The multi-section path (two sections separated by ';') already routes every section
        // through ApplyNumericFormat, which strips "_x"/"*x" directives before reaching
        // IsSimpleFractionFormat/FormatSimpleFraction -- it was never affected by this bug.
        // The fast single-section path (no ';') must now produce byte-identical output to what
        // the multi-section path produces for the very same format text, proving the fix reuses
        // (rather than diverges from) the already-correct directive handling.
        var singleSection = NumberFormatter.Format(new NumberValue(2.5), "_(# ?/?_)");
        var viaMultiSectionPath = NumberFormatter.Format(new NumberValue(2.5), "_(# ?/?_);_(# ?/?_)");

        singleSection.Should().Be(viaMultiSectionPath);
    }

    [Fact]
    public void SingleSection_AsteriskRepeatFractionFormat_DoesNotLeakAsteriskLiterally()
    {
        // "* # ?/?" : the "* " directive is a fill directive (repeat the following character,
        // here a space, to fill the column) -- Excel never shows the literal '*' character.
        var result = NumberFormatter.Format(new NumberValue(2.5), "* # ?/?");

        result.Should().NotContain("*");
        result.Should().Be(" 2 1/2");

        var viaMultiSectionPath = NumberFormatter.Format(new NumberValue(2.5), "* # ?/?;* # ?/?");
        result.Should().Be(viaMultiSectionPath);
    }

    [Fact]
    public void SingleSection_PlainFractionFormat_WithoutDirectives_StillRendersUnaffected()
    {
        // Sibling/neighbouring-behaviour proof: a single-section fraction format with NO
        // spacing/fill directives at all must render exactly as before the fix -- confirms the
        // new RemoveSpacingAndFillDirectives/PreserveAccountingFillSpace pass is a no-op absent
        // '_'/'*' tokens and did not regress the common case.
        NumberFormatter.Format(new NumberValue(2.5), "# ?/?").Should().Be("2 1/2");
    }

    [Fact]
    public void SingleSection_PlainFractionFormat_NegativeValue_StillRendersUnaffected()
    {
        // Further neighbouring-behaviour proof: the fast path's negative-value sign handling
        // (routed through FormatSimpleFraction with the original signed value, per the existing
        // R74/R79 comment above the fraction branch) must still work after cleaning the format.
        NumberFormatter.Format(new NumberValue(-2.5), "# ?/?").Should().Be("-2 1/2");
    }
}
