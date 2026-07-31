using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R102: a trailing-comma-EMPTY argument (e.g. <c>=LEFT("abc",)</c>) is a slot that is PRESENT but
/// blank, which Excel treats as a normal blank-cell numeric coercion (to 0) -- NOT the same as the
/// argument being genuinely OMITTED (e.g. <c>=LEFT("abc")</c>), which uses that function's own
/// smart default. <c>FormulaEvaluator.Functions.cs</c>'s argument-expansion loop used to conflate
/// the two for FIND/SEARCH's start_num, LEFT/RIGHT's num_chars, SUBSTITUTE's instance_num, and
/// their FINDB/SEARCHB/LEFTB/RIGHTB byte-count variants (via the now-removed, overly-broad
/// <c>IsOmittedOptionalOrdinalArgument</c> branch), forcing a trailing-comma-empty argument to
/// behave exactly like the argument being omitted. This mirrors the mechanism this same round's
/// VLOOKUP/HLOOKUP range_lookup fix addresses (trailing-comma-empty must NOT collapse to the
/// omitted default), just in the opposite numeric direction: these six function families all
/// reject or reinterpret a coerced 0, where VLOOKUP's boolean range_lookup coerces to FALSE.
///
/// TEXTBEFORE/TEXTAFTER's instance_num is DELIBERATELY NOT part of this fix (see class doc on the
/// NOT_A_BUG tests below and the long comment in FormulaEvaluator.Functions.cs): unlike the six
/// functions above, instance_num is a MIDDLE optional argument (match_mode/match_end/if_not_found
/// follow it), so Excel treats a blank instance_num used to skip past it and reach one of those
/// later arguments as equivalent to omitted, not as a coerced 0 -- confirmed by this codebase's own
/// pre-existing ExcelParityModernTextTests (a real, documented Microsoft example using
/// TEXTBEFORE("Socrates"," ",,,1) with a blank instance_num, plus a case-insensitive-search test
/// using TEXTBEFORE/TEXTAFTER(...,,1) the same way), which this fix leaves passing unchanged.
/// </summary>
public partial class FunctionLibraryTests
{
    // ── LEFT / RIGHT (+ B-variants): num_chars omitted -> 1; empty -> coerces to 0 -> "" ──────────

    [Fact]
    public void R102_Left_GenuinelyOmittedNumChars_DefaultsToOneChar()
    {
        _eval.Evaluate("=LEFT(\"abc\")", MakeSheet()).Should().Be(new TextValue("a"));
    }

    [Fact]
    public void R102_Left_TrailingCommaEmptyNumChars_CoercesToZero_ReturnsEmptyString()
    {
        _eval.Evaluate("=LEFT(\"abc\",)", MakeSheet()).Should().Be(new TextValue(""));
    }

    [Fact]
    public void R102_Right_GenuinelyOmittedNumChars_DefaultsToOneChar()
    {
        _eval.Evaluate("=RIGHT(\"abc\")", MakeSheet()).Should().Be(new TextValue("c"));
    }

    [Fact]
    public void R102_Right_TrailingCommaEmptyNumChars_CoercesToZero_ReturnsEmptyString()
    {
        _eval.Evaluate("=RIGHT(\"abc\",)", MakeSheet()).Should().Be(new TextValue(""));
    }

    [Fact]
    public void R102_LeftB_TrailingCommaEmptyNumBytes_CoercesToZero_ReturnsEmptyString()
    {
        _eval.Evaluate("=LEFTB(\"abc\",)", MakeSheet()).Should().Be(new TextValue(""));
    }

    [Fact]
    public void R102_RightB_TrailingCommaEmptyNumBytes_CoercesToZero_ReturnsEmptyString()
    {
        _eval.Evaluate("=RIGHTB(\"abc\",)", MakeSheet()).Should().Be(new TextValue(""));
    }

    // A genuine blank-cell reference (already-working case, unaffected by this fix) must behave
    // identically to the trailing-comma-empty case -- confirms the fix makes trailing-comma-empty
    // consistent with the reference case, not the other way around.
    [Fact]
    public void R102_Left_BlankCellReferenceNumChars_CoercesToZero_ReturnsEmptyString()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LEFT(\"abc\",Z9)", sheet).Should().Be(new TextValue(""));
    }

    // ── FIND / SEARCH (+ B-variants): start_num omitted -> 1; empty -> coerces to 0 -> #VALUE! ────

    [Fact]
    public void R102_Find_GenuinelyOmittedStartNum_DefaultsToOne_Succeeds()
    {
        _eval.Evaluate("=FIND(\"b\",\"abc\")", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void R102_Find_TrailingCommaEmptyStartNum_CoercesToZero_ReturnsValueError()
    {
        _eval.Evaluate("=FIND(\"b\",\"abc\",)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void R102_Search_GenuinelyOmittedStartNum_DefaultsToOne_Succeeds()
    {
        _eval.Evaluate("=SEARCH(\"B\",\"abc\")", MakeSheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void R102_Search_TrailingCommaEmptyStartNum_CoercesToZero_ReturnsValueError()
    {
        _eval.Evaluate("=SEARCH(\"B\",\"abc\",)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void R102_FindB_TrailingCommaEmptyStartNum_CoercesToZero_ReturnsValueError()
    {
        _eval.Evaluate("=FINDB(\"b\",\"abc\",)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void R102_SearchB_TrailingCommaEmptyStartNum_CoercesToZero_ReturnsValueError()
    {
        _eval.Evaluate("=SEARCHB(\"B\",\"abc\",)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── SUBSTITUTE: instance_num omitted -> replace all; empty -> coerces to 0 -> #VALUE! ─────────

    [Fact]
    public void R102_Substitute_GenuinelyOmittedInstanceNum_ReplacesAllOccurrences()
    {
        _eval.Evaluate("=SUBSTITUTE(\"a-a-a\",\"a\",\"b\")", MakeSheet()).Should().Be(new TextValue("b-b-b"));
    }

    [Fact]
    public void R102_Substitute_TrailingCommaEmptyInstanceNum_CoercesToZero_ReturnsValueError()
    {
        _eval.Evaluate("=SUBSTITUTE(\"a-a-a\",\"a\",\"b\",)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    // ── TEXTBEFORE / TEXTAFTER: NOT_A_BUG -- instance_num is a MIDDLE optional argument, so a
    // blank slot used to skip past it and reach match_mode/match_end/if_not_found must still mean
    // "use the default" (1), not coerce to 0 and error. See the class doc comment for the evidence.

    [Fact]
    public void R102_Textbefore_GenuinelyOmittedInstanceNum_DefaultsToFirstOccurrence()
    {
        _eval.Evaluate("=TEXTBEFORE(\"a-b-c\",\"-\")", MakeSheet()).Should().Be(new TextValue("a"));
    }

    [Fact]
    public void R102_Textbefore_TrailingCommaEmptyInstanceNum_NotABug_StillDefaultsToFirstOccurrence()
    {
        // NOT_A_BUG: a blank instance_num here is being used to skip ahead to match_mode (arg 4),
        // not to force an error -- must behave identically to the genuinely-omitted case above.
        _eval.Evaluate("=TEXTBEFORE(\"a-b-c\",\"-\",,0)", MakeSheet()).Should().Be(new TextValue("a"));
    }

    [Fact]
    public void R102_Textafter_GenuinelyOmittedInstanceNum_DefaultsToFirstOccurrence()
    {
        _eval.Evaluate("=TEXTAFTER(\"a-b-c\",\"-\")", MakeSheet()).Should().Be(new TextValue("b-c"));
    }

    [Fact]
    public void R102_Textafter_TrailingCommaEmptyInstanceNum_NotABug_StillDefaultsToFirstOccurrence()
    {
        // NOT_A_BUG: same as TEXTBEFORE above -- blank instance_num skipping to match_mode.
        _eval.Evaluate("=TEXTAFTER(\"a-b-c\",\"-\",,0)", MakeSheet()).Should().Be(new TextValue("b-c"));
    }
}
