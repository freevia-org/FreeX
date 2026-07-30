using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-94 regression coverage for the '?' integer-alignment sign-desync defect:
/// ApplyQuestionIntegerSpacing compared the loop's absolute string index against
/// missingCount (a pure digit-count difference) to decide which leading-zero digits
/// become spaces. When the rendered text carries a leading '-' (reached via
/// FormatNumber's hasConditions branch, which never Math.Abs's the value before
/// formatting a matched [condition] section), the sign shifted every digit position
/// right by one and the index check silently never fired, leaving a literal zero
/// digit ("-05.5 ") instead of a blanked space ("- 5.5 "). Fixed by stripping the
/// sign from both the zero- and hash-padded integer strings before the digit walk
/// and reattaching it to the result.
/// </summary>
public sealed class R94_QuestionMarkConditionSignAlignmentTests
{
    [Fact]
    public void NegativeValue_ThroughExplicitConditionSection_BlanksSuppressedLeadingZero()
    {
        // "[<0]??.??" applied to -5.5 goes through FormatNumber's hasConditions/multi-section
        // path (single section but gated by an explicit [<0] condition), which leaves the
        // signed value un-Math.Abs'd before it reaches FormatQuestionPlaceholderNumber.
        var result = NumberFormatter.Format(new NumberValue(-5.5), "[<0]??.??");

        result.Should().Be("- 5.5 ");
    }

    [Fact]
    public void NegativeValue_WithoutCondition_StillBlanksSuppressedLeadingZero()
    {
        // Sibling no-regression case: the SAME pattern applied without a condition already
        // went through the correct sign-prepend fast path (magnitude formatted with no sign,
        // then '-' prepended afterward) and must keep producing the identical alignment.
        var result = NumberFormatter.Format(new NumberValue(-5.5), "??.??");

        result.Should().Be("- 5.5 ");
    }

    [Fact]
    public void PositiveValue_ThroughExplicitConditionSection_Unaffected()
    {
        // Sibling no-regression case: positive values never carry a leading sign character,
        // so the digit walk was never desynced for them -- must remain unaffected by the fix.
        var result = NumberFormatter.Format(new NumberValue(5.5), "[>=0]??.??");

        result.Should().Be(" 5.5 ");
    }

    [Fact]
    public void NegativeValue_ThroughExplicitConditionSection_IntegerOnly_BlanksSuppressedLeadingZero()
    {
        // Integer-only '?' format (no decimal section) isolates ApplyQuestionIntegerSpacing's
        // return path from ApplyQuestionDecimalSpacing, confirming the sign-strip/reattach
        // fix alone (not some decimal-side interaction) accounts for the corrected output.
        var result = NumberFormatter.Format(new NumberValue(-5.0), "[<0]???");

        result.Should().Be("-  5");
    }
}
