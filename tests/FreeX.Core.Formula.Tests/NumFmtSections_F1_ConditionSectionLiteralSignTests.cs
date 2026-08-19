using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// numfmt-sections F1: FormatNumber's hasConditions branch (NumberFormatter.cs) set
/// `double displayValue = value;` and never overwrote it for a [condition]-selected section --
/// unlike SelectPositionalSection, which explicitly Math.Abs's the value for the positional
/// negative section. When the SELECTED section's own text already carries a literal sign
/// indicator (a leading '-' or accounting parens), that literal sign combined with .NET's
/// custom-format engine auto-prepending its OWN '-' for a negative input (since the format has
/// no ';'-separated sections at that point), producing a doubled/misplaced sign: "--5.25"
/// instead of "-5.25", "-(5.25)" instead of "(5.25)", "--5" instead of "-5" for a decorated-
/// General section.
///
/// Fixed by adding SectionHasLeadingLiteralSign: when the [condition]-selected section's
/// (directive-stripped) format text starts with a literal '-' or '(', the hasConditions branch
/// now feeds ApplyNumericFormat the magnitude (matching SelectPositionalSection's convention),
/// so the author's own literal sign is the only one that appears.
/// </summary>
public sealed class NumFmtSections_F1_ConditionSectionLiteralSignTests
{
    [Fact]
    public void ConditionedNegativeSection_LiteralLeadingMinus_NoDoubledSign()
    {
        // "0.00;[<0]-0.00" on -5.25 -- before the fix, the still-signed -5.25 reached .NET's
        // ToString("-0.00", ...), which auto-prepended its own '-' in front of the author's
        // literal '-', producing "--5.25".
        NumberFormatter.Format(new NumberValue(-5.25), "0.00;[<0]-0.00").Should().Be("-5.25");
    }

    [Fact]
    public void ConditionedNegativeSection_AccountingParens_NoMisplacedSign()
    {
        // "[>0]0.00;[<0](0.00)" on -5.25 -- before the fix this rendered "-(5.25)" (auto '-'
        // ahead of the author's parens) instead of the correct accounting "(5.25)".
        NumberFormatter.Format(new NumberValue(-5.25), "[>0]0.00;[<0](0.00)").Should().Be("(5.25)");
    }

    [Fact]
    public void ConditionedDecoratedGeneral_LiteralLeadingMinus_NoDoubledSign()
    {
        // "General;[Red][<0]-General" on -5 -- before the fix this rendered "--5" (color still
        // correctly resolved to red, but text doubled the sign).
        var result = NumberFormatter.FormatWithColor(new NumberValue(-5), "General;[Red][<0]-General");
        result.Text.Should().Be("-5");
        result.ColorHex.Should().Be("#FF0000");
    }

    [Fact]
    public void ConditionedNegativeSection_NoLiteralSign_SiblingNoRegression_AutoSignStillShows()
    {
        // Sibling no-regression case pinned by tests/FreeX.Core.Calc.Tests/NumberFormatterTests.cs
        // ("[Red][<0]0.00;[Blue]0.00" on -2.5 => "-2.50"): a conditioned section with NO literal
        // sign of its own must keep receiving the SIGNED value, so .NET's auto-prepended '-'
        // remains the only sign shown. Applying Math.Abs unconditionally here would silently
        // drop the sign instead of doubling it -- a worse regression than the original bug.
        NumberFormatter.Format(new NumberValue(-2.5), "[Red][<0]0.00;[Blue]0.00").Should().Be("-2.50");
    }

    [Fact]
    public void ConditionedQuestionPlaceholderSection_NoLiteralSign_SiblingNoRegression()
    {
        // Sibling no-regression case pinned by R94_QuestionMarkConditionSignAlignmentTests:
        // "[<0]??.??" on -5.5 must still render "- 5.5 " (auto sign + blanked leading zero),
        // which depends on FormatQuestionPlaceholderNumber still receiving the SIGNED value
        // because "??.??" carries no literal sign of its own.
        NumberFormatter.Format(new NumberValue(-5.5), "[<0]??.??").Should().Be("- 5.5 ");
    }

    [Fact]
    public void ConditionedPositiveSection_LiteralParens_Unaffected()
    {
        // Sibling no-regression case: a positive value never triggers the Math.Abs branch
        // (value < 0 guard), so a conditioned section with its own literal parens must still
        // render normally for a matching non-negative value.
        NumberFormatter.Format(new NumberValue(5.25), "[>0](0.00);[<0]0.00").Should().Be("(5.25)");
    }
}
