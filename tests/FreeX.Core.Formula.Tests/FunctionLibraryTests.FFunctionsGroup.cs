using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for review-program group F-functions findings:
/// G11 (MAXIFS/MINIFS not implemented), G27 (TEXTJOIN fast-path overflow
/// rule diverging from the slow path), G37 (PERMUT(0,0) should be 1).
/// </summary>
public partial class FunctionLibraryTests
{
    // ── G11: MAXIFS / MINIFS ─────────────────────────────────────────────────

    [Fact]
    public void Maxifs_RangeArg_WorksCorrectly()
    {
        // A: 10,20,30; B: "A","B","A" → MAXIFS(A1:A3,B1:B3,"A") = 30
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("A")),  (2, 2, new TextValue("B")),  (3, 2, new TextValue("A")));

        _eval.Evaluate("=MAXIFS(A1:A3,B1:B3,\"A\")", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Minifs_RangeArg_WorksCorrectly()
    {
        // A: 10,20,30; B: "A","B","A" → MINIFS(A1:A3,B1:B3,"A") = 10
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (1, 2, new TextValue("A")),  (2, 2, new TextValue("B")),  (3, 2, new TextValue("A")));

        _eval.Evaluate("=MINIFS(A1:A3,B1:B3,\"A\")", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Maxifs_MultipleCriteria_AllMustMatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),  (1, 2, new TextValue("East")), (1, 3, new TextValue("Q1")),
            (2, 1, new NumberValue(15)), (2, 2, new TextValue("East")), (2, 3, new TextValue("Q2")),
            (3, 1, new NumberValue(25)), (3, 2, new TextValue("West")), (3, 3, new TextValue("Q1")));

        _eval.Evaluate("=MAXIFS(A1:A3,B1:B3,\"East\",C1:C3,\"Q1\")", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Minifs_NoMatchingRows_ReturnsZero()
    {
        // Excel: MAXIFS/MINIFS return 0 (not an error) when nothing matches.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=MINIFS(A1:A1,B1:B1,\"Z\")", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Maxifs_NoMatchingRows_ReturnsZero()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=MAXIFS(A1:A1,B1:B1,\"Z\")", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Maxifs_CriteriaError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=MAXIFS(A1:A1,B1:B1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Minifs_MaxRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("A")));
        _eval.Evaluate("=MINIFS(NA(),A1:A1,\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Maxifs_CriteriaRangeArgumentError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(10)));
        _eval.Evaluate("=MAXIFS(A1:A1,NA(),\"A\")", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Maxifs_MismatchedCriteriaRangeShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)),
            (1, 2, new TextValue("A")));

        _eval.Evaluate("=MAXIFS(A1:A2,B1:B1,\"A\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Minifs_TextCellsInMinRange_AreIgnored()
    {
        // Non-numeric cells in the min/max range are excluded from the aggregate,
        // matching Excel's MAXIFS/MINIFS (and this codebase's SUMIFS) semantics.
        var sheet = MakeSheet(
            (1, 1, new TextValue("n/a")), (1, 2, new TextValue("A")),
            (2, 1, new NumberValue(7)),   (2, 2, new TextValue("A")));

        _eval.Evaluate("=MINIFS(A1:A2,B1:B2,\"A\")", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Maxifs_NotRegisteredAsNameError_IsNowRecognized()
    {
        // Prior to the fix this returned #NAME? because MAXIFS/MINIFS had no
        // entry in the built-in function table.
        _eval.Evaluate("=MAXIFS(A1:A1,A1:A1,1)", MakeSheet((1, 1, new NumberValue(1))))
            .Should().NotBe(ErrorValue.Name);
        _eval.Evaluate("=MINIFS(A1:A1,A1:A1,1)", MakeSheet((1, 1, new NumberValue(1))))
            .Should().NotBe(ErrorValue.Name);
    }

    // ── G27: TEXTJOIN fast-path / slow-path overflow-rule parity ────────────

    [Fact]
    public void Textjoin_SurrogatePairHeavyString_FastPathAndSlowPathAgree_AtLimit()
    {
        // 16383 supplementary-plane characters = 32766 UTF-16 code units: at the
        // limit, both paths must accept it (Excel counts raw UTF-16 code units,
        // not "text elements"/collapsed surrogate pairs).
        var text = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 16383));
        text.Length.Should().Be(32766);

        var sheet = MakeSheet((1, 1, new TextValue(text)));

        // Fast path: TEXTJOIN's only variadic argument is a direct single-cell range.
        _eval.Evaluate("=TEXTJOIN(\"\",FALSE,A1)", sheet).Should().Be(new TextValue(text));

        // Slow path: force the fast path to bail out by using a non-range expression.
        _eval.Evaluate("=TEXTJOIN(\"\",FALSE,A1&\"\")", sheet).Should().Be(new TextValue(text));
    }

    [Fact]
    public void Textjoin_SurrogatePairHeavyString_FastPathAndSlowPathAgree_OverLimit()
    {
        // 16384 supplementary-plane characters = 32768 UTF-16 code units: over
        // Excel's 32767-code-unit cell text limit, so both paths must return
        // #VALUE!. Before the fix, the fast path collapsed each surrogate pair
        // into a single "element" (16384 <= 32767) and wrongly accepted this,
        // while the slow path correctly rejected it.
        var text = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 16384));
        text.Length.Should().Be(32768);

        var sheet = MakeSheet((1, 1, new TextValue(text)));

        _eval.Evaluate("=TEXTJOIN(\"\",FALSE,A1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TEXTJOIN(\"\",FALSE,A1&\"\")", sheet).Should().Be(ErrorValue.Value);
    }

    // ── G37: PERMUT(0,0) boundary case ───────────────────────────────────────

    [Fact]
    public void Permut_ZeroAndZero_ReturnsOne()
    {
        // 0!/(0-0)! = 1, matching COMBIN(0,0) and Excel's real PERMUT(0,0) result.
        _eval.Evaluate("=PERMUT(0,0)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Permut_ZeroPointNineTruncatedToZeroAndZero_ReturnsOne()
    {
        _eval.Evaluate("=PERMUT(0.9,0)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Permut_ZeroWithPositiveChosen_StillReturnsNumError()
    {
        // n=0 with k>0 must still be #NUM! (k > n).
        _eval.Evaluate("=PERMUT(0,1)", MakeSheet()).Should().Be(ErrorValue.Num);
    }
}
