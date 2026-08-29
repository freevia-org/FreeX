using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// freex-number-format-edge F1: NumberFormatter.SectionHasLeadingLiteralSign (NumberFormatter.cs)
/// skipped leading "[...]" bracket directives and whitespace, then checked only the SINGLE next
/// character for a literal '-' or '(' sign. It did not skip past a leading quoted literal (e.g.
/// "Owed ") or a bare literal character (e.g. a currency symbol like '$') that legitimately
/// precedes the sign/paren in a [condition]-selected section's own text. When such a prefix was
/// present, the helper wrongly returned false, so FormatNumber kept the SIGNED value instead of
/// Math.Abs(value); the signed value then reached .NET's custom-format engine, which -- because
/// the pattern has no ';'-separated negative section at that point -- auto-prepended its OWN '-'
/// on top of the author's literal sign/paren, producing a doubled/garbled result ("Owed --5.00",
/// "Owed (-5.00)", "-$(5.00)").
///
/// Fixed by rewriting SectionHasLeadingLiteralSign to scan forward past quoted text ("..."),
/// escaped literal characters (\X), bracket directives, and any other bare literal character,
/// stopping only at the first '-'/'(' (real sign -- Math.Abs the value) or the first numeric
/// placeholder (0/#/? -- no literal sign, keep the signed value for .NET's auto '-').
/// </summary>
public sealed class NumFmtEdge_F1_ConditionSectionLiteralPrefixSignTests
{
    /// <summary>
    /// r168 remediation. The scan above originally skipped past ANY bare literal character to keep
    /// looking for a sign, which read the hyphen inside a label word as the author's sign, dropped
    /// the real minus, and rendered a negative value identically to a positive one. Losing the sign
    /// silently is worse than the doubled sign this helper exists to prevent, so a bare LETTER now
    /// ends the scan while a bare symbol (a currency mark) is still skipped.
    /// </summary>
    /// <summary>
    /// r169. The r168 rewrite treated a quoted span as wholly inert so a quoted LABEL could be
    /// skipped -- which meant an author who quotes the SIGN itself was never seen to have written
    /// one, and .NET prepended a second minus. Quoting a literal character is ordinary authoring
    /// practice, so this is the same doubled sign the helper exists to prevent, reached by a
    /// different route.
    /// </summary>
    [Fact]
    public void ConditionedNegativeSection_QuotedSignCharacter_IsNotDoubled()
    {
        NumberFormatter.FormatWithColor(new NumberValue(-5.25), "[<0]\"-\"0.00;[>=0]0.00")
            .Text.Should().Be("-5.25");
    }

    [Fact]
    public void ConditionedNegativeSection_HyphenatedLabelWord_KeepsTheRealMinus()
    {
        NumberFormatter.FormatWithColor(new NumberValue(-5), "[<0]Ref-No 0.00;[>=0]Ref-No 0.00")
            .Text.Should().NotBe(
                NumberFormatter.FormatWithColor(new NumberValue(5), "[<0]Ref-No 0.00;[>=0]Ref-No 0.00").Text,
                "a negative value must not render identically to a positive one");
    }

    /// <summary>
    /// Sibling of the case above: a date section whose separator is a hyphen must not have that
    /// separator read as a sign either. Excel shows its invalid-date indicator for a negative
    /// serial; fabricating a calendar date instead would be a silent wrong answer.
    /// </summary>
    [Fact]
    public void ConditionedDateSection_HyphenSeparator_IsNotReadAsASign()
    {
        NumberFormatter.FormatWithColor(new NumberValue(-5), "[<0]yyyy-mm-dd;[>=0]0.00")
            .Text.Should().NotBe("1900-01-05");
    }

    [Fact]
    public void ConditionedNegativeSection_QuotedLiteralPrefixBeforeMinus_NoDoubledSign()
    {
        // Before the fix: "Owed --5.00" (auto '-' glued in front of the author's literal '-',
        // which the helper never reached because it stopped at the quoted prefix's opening '"').
        NumberFormatter.FormatWithColor(new NumberValue(-5), "[<0]\"Owed \"-0.00;[>=0]0.00")
            .Text.Should().Be("Owed -5.00");
    }

    [Fact]
    public void ConditionedNegativeSection_QuotedLiteralPrefixBeforeParens_NoMisplacedSign()
    {
        // Before the fix: "Owed (-5.00)" (auto '-' injected inside the accounting parens).
        NumberFormatter.FormatWithColor(new NumberValue(-5), "[<0]\"Owed \"(0.00);[>=0]0.00")
            .Text.Should().Be("Owed (5.00)");
    }

    [Fact]
    public void ConditionedNegativeSection_BareCurrencySymbolPrefixBeforeParens_NoLeadingMinus()
    {
        // Before the fix: "-$(5.00)" (auto '-' glued in front of the whole result because the
        // helper stopped at the bare '$' and never saw the real ')(' paren sign after it).
        NumberFormatter.FormatWithColor(new NumberValue(-5), "[<0]$(0.00);[>=0]$0.00")
            .Text.Should().Be("$(5.00)");
    }

    [Fact]
    public void ConditionedNegativeSection_NoLiteralSignAfterPrefix_SiblingNoRegression_AutoSignStillShows()
    {
        // Sibling no-regression: a literal prefix ("Owed ") in front of a section that has NO
        // real sign indicator of its own (just digit placeholders) must still keep the signed
        // value so .NET's auto-prepended '-' remains the only sign shown -- the scan must stop
        // at the numeric placeholder and return false, not skip through it looking for a sign.
        NumberFormatter.FormatWithColor(new NumberValue(-5), "[<0]\"Owed \"0.00;[>=0]0.00")
            .Text.Should().Be("Owed -5.00");
    }

    [Fact]
    public void ConditionedNegativeSection_NoPrefixLiteralMinus_SiblingNoRegression_StillCorrect()
    {
        // Sibling no-regression: the original no-prefix case (already pinned by
        // NumFmtSections_F1_ConditionSectionLiteralSignTests) must be unaffected by the rewrite.
        NumberFormatter.Format(new NumberValue(-5.25), "0.00;[<0]-0.00").Should().Be("-5.25");
    }
}
