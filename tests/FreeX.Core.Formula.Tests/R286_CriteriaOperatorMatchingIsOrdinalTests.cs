using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// r286: criteria operators were matched with a CULTURE-SENSITIVE <c>StartsWith</c> and then sliced
/// ORDINALLY, and the two disagree about where the operator ends.
///
/// <para>.NET's default <c>StartsWith(string)</c> uses the current culture, and ICU treats zero-width
/// joiners, zero-width non-joiners and soft hyphens as IGNORABLE -- on the invariant and en-US
/// cultures too, so this is not a locale-specific defect. A criteria of <c>"&lt;ZWJ&gt;&gt;=5"</c>
/// therefore matched <c>StartsWith("&gt;=")</c>, and <c>criteria[2..]</c> then removed the joiner and
/// the <c>&gt;</c>, leaving the operand <c>"=5"</c>. The operator was read from one interpretation of
/// the string and its operand from another.</para>
///
/// <para>The first draft of these tests summed numeric labels and PASSED against the unfixed code,
/// because a garbage operand matches nothing and "matches nothing" was also the correct answer for
/// that data. The tests below put a cell whose text IS the criteria, so the correct answer is
/// non-zero and the two readings cannot agree by luck.</para>
///
/// <para>The rest of the codebase had already moved off culture-sensitive <c>IndexOf</c> and
/// <c>EndsWith</c> -- zero remaining -- and left this cluster of six behind.</para>
/// </summary>
public class R286_CriteriaOperatorMatchingIsOrdinalTests
{
    private const string Zwj = "‍";

    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static Sheet NumbersWithLabels() => MakeSheet(
        (1, 1, new NumberValue(10)), (1, 2, new NumberValue(1)),
        (2, 1, new NumberValue(20)), (2, 2, new NumberValue(6)),
        (3, 1, new NumberValue(30)), (3, 2, new NumberValue(9)));

    [Theory]
    [InlineData(">=5", 50.0)]
    [InlineData("<=5", 10.0)]
    [InlineData(">5", 50.0)]
    [InlineData("<5", 10.0)]
    [InlineData("<>6", 40.0)]
    public void OrdinaryComparisonCriteriaStillSplit(string criteria, double expected) =>
        _eval.Evaluate($"=SUMIF(B1:B3,\"{criteria}\",A1:A3)", NumbersWithLabels())
            .Should().Be(new NumberValue(expected));

    /// <summary>
    /// The discriminating case. B2 holds the literal text of the criteria, so a correct text match
    /// sums 20. Treating it as "&gt;=" with the operand "=5" matches nothing and sums 0.
    /// </summary>
    [Theory]
    [InlineData("‍")]  // zero-width joiner
    [InlineData("‌")]  // zero-width non-joiner
    [InlineData("­")]  // soft hyphen
    public void AnIgnorableCharacterBeforeTheOperatorIsPartOfTheTextNotAComparison(string ignorable)
    {
        var criteria = ignorable + ">=5";
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("plain")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue(criteria)),
            (3, 1, new NumberValue(30)), (3, 2, new TextValue("other")));

        _eval.Evaluate($"=SUMIF(B1:B3,\"{criteria}\",A1:A3)", sheet)
            .Should().Be(new NumberValue(20),
                "the criteria is text that no operator introduces. Culture-sensitive matching read it "
                + "as '>=' and then sliced ordinally, producing the operand \"=5\" -- an operator from "
                + "one reading of the string and its operand from another, matching nothing");
    }

    /// <summary>
    /// Single-character operators slice one char rather than two, so they carry the same mismatch
    /// with a shorter prefix; pinned separately so a half-applied fix cannot pass.
    /// </summary>
    [Fact]
    public void AnIgnorableCharacterBeforeASingleCharacterOperatorIsAlsoText()
    {
        var criteria = Zwj + ">5";
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new TextValue("plain")),
            (2, 1, new NumberValue(20)), (2, 2, new TextValue(criteria)));

        _eval.Evaluate($"=SUMIF(B1:B3,\"{criteria}\",A1:A3)", sheet)
            .Should().Be(new NumberValue(20));
    }

    /// <summary>
    /// The platform fact the defect rests on, asserted rather than assumed: the two overloads
    /// disagree, which is precisely why matching with one and slicing for the other is unsound.
    /// </summary>
    [Theory]
    [InlineData("‍")]
    [InlineData("‌")]
    [InlineData("­")]
    public void TheCultureSensitiveAndOrdinalOverloadsDisagreeOnIgnorableLeadingCharacters(string ignorable)
    {
        var criteria = ignorable + ">=5";

        criteria.StartsWith(">=", StringComparison.CurrentCulture).Should().BeTrue(
            "ICU treats these as ignorable, so the culture-sensitive overload skips them");
        criteria.StartsWith(">=", StringComparison.Ordinal).Should().BeFalse(
            "the ordinal overload does not -- and the slicing that follows the match is ordinal");
    }
}
