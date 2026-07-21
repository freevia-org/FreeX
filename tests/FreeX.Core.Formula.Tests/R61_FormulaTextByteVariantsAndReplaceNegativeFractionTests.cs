using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R61-formula-text-substitute-6-1: LEFTB/RIGHTB/FINDB/SEARCHB never got the R51 omitted-vs-blank-
/// reference fix applied to their non-B siblings (LEFT/RIGHT/FIND/SEARCH). An explicit blank-cell
/// reference for num_bytes/start_num must coerce to numeric 0 (matching Excel and the already-fixed
/// non-B functions), not fall back to the omitted-argument default of 1.
///
/// R61-formula-text-substitute-6-2: REPLACE/REPLACEB validated num_chars/num_bytes AFTER truncating
/// to int, so a negative fraction in (-1, 0) (e.g. -0.5) silently truncated to 0 and passed the
/// domain check instead of being rejected with #VALUE!, unlike LEFT/RIGHT/MID which check the raw
/// double before casting.
/// </summary>
public sealed class R61_FormulaTextByteVariantsAndReplaceNegativeFractionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet SheetWithBlankB1()
    {
        // B1 is never set, so referencing it evaluates to BlankValue -- an explicit blank-cell
        // reference, distinct from the argument slot being omitted entirely.
        return new Sheet(SheetId.New(), "S");
    }

    [Fact]
    public void LeftB_NumBytesAsBlankCellReference_ReturnsEmptyString()
    {
        var sheet = SheetWithBlankB1();

        // Blank-cell reference coerces to 0, not the omitted-argument default of 1.
        _eval.Evaluate("=LEFTB(\"hello\",B1)", sheet).Should().Be(new TextValue(""));
    }

    [Fact]
    public void LeftB_NumBytesOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=LEFTB(\"hello\")", sheet).Should().Be(new TextValue("h"));
    }

    [Fact]
    public void RightB_NumBytesAsBlankCellReference_ReturnsEmptyString()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=RIGHTB(\"hello\",B1)", sheet).Should().Be(new TextValue(""));
    }

    [Fact]
    public void RightB_NumBytesOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=RIGHTB(\"hello\")", sheet).Should().Be(new TextValue("o"));
    }

    [Fact]
    public void FindB_StartNumAsBlankCellReference_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=FINDB(\"l\",\"hello\",B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void FindB_StartNumOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=FINDB(\"l\",\"hello\")", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void SearchB_StartNumAsBlankCellReference_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=SEARCHB(\"l\",\"hello\",B1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void SearchB_StartNumOmitted_StillDefaultsToOne_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=SEARCHB(\"l\",\"hello\")", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Replace_NegativeFractionalNumChars_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        // -0.5 truncates to 0 via (int) cast; Excel rejects any negative num_chars with #VALUE!,
        // regardless of its fractional part -- exactly like LEFT/MID reject a negative count.
        _eval.Evaluate("=REPLACE(\"abcdef\",2,-0.5,\"XY\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Replace_NonNegativeNumChars_StillReplacesNormally_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=REPLACE(\"abcdef\",2,3,\"XY\")", sheet).Should().Be(new TextValue("aXYef"));
    }

    [Fact]
    public void ReplaceB_NegativeFractionalNumBytes_ReturnsValueError()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=REPLACEB(\"abcdef\",2,-0.5,\"XY\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void ReplaceB_NonNegativeNumBytes_StillReplacesNormally_SiblingNoRegression()
    {
        var sheet = SheetWithBlankB1();

        _eval.Evaluate("=REPLACEB(\"abcdef\",2,3,\"XY\")", sheet).Should().Be(new TextValue("aXYef"));
    }
}
