namespace FreeW.Core.Model.Tests;

/// <summary>
/// r561: a mail-merge <c>{ IF }</c> rule compares a data-source field against a rule value, and
/// merge data comes from an EXTERNAL source (a spreadsheet, CSV or database), so both sides are
/// outside the document's control. <c>CompareValues</c> tries a numeric comparison first with
/// <c>NumberStyles.Any</c>, which accepts the literal "NaN" and "Infinity".
///
/// <para>That makes an equality test on IDENTICAL TEXT answer "no match", because NaN == NaN is
/// false by IEEE rule, and it makes every ordering test false while NotEqual becomes vacuously
/// true. Word compares numerically only when both sides really are numbers and otherwise
/// compares as text -- which this method already does in its own fallback, three lines below the
/// numeric branch.</para>
///
/// <para>Same alignment class as r556 (FreeX's filter checklist) and r557 (FreeW's numeric sort),
/// now on the merge surface: a literal that .NET parses and the reference product does not.</para>
/// </summary>
public class R561_MergeConditionComparesNonNumbersAsTextTests
{
    private static string? Branch(string fieldValue, MergeConditionOperator op, string ruleValue)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Field"] = fieldValue,
        };
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Field", op, ruleValue, "yes", "no");

        return MergeRuleEvaluator
            .Evaluate(instruction, row, new MergeState(), recordIndex: 0)
            ?.Text;
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Identical_non_numeric_text_compares_equal(string value)
    {
        // The plainest statement of the bug: the same string on both sides must match.
        Branch(value, MergeConditionOperator.Equal, value).Should().Be("yes");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Identical_non_numeric_text_is_not_reported_as_different(string value)
    {
        // The mirror case, which the numeric path got vacuously right for Infinity and wrong for
        // NaN -- so both spellings are pinned rather than assuming one covers the other.
        Branch(value, MergeConditionOperator.NotEqual, value).Should().Be("no");
    }

    [Fact]
    public void A_run_of_digits_too_long_to_represent_still_compares_equal_to_itself()
    {
        // No exponent needed: 400 digits overflow to Infinity, so two DIFFERENT long numbers would
        // also compare equal numerically. Text comparison keeps them distinct.
        var huge = new string('9', 400);
        var otherHuge = new string('8', 400);

        Branch(huge, MergeConditionOperator.Equal, huge).Should().Be("yes");
        Branch(huge, MergeConditionOperator.Equal, otherHuge).Should().Be("no");
    }

    [Fact]
    public void Ordinary_numbers_still_compare_numerically_not_lexically()
    {
        // Non-vacuity: a guard that pushed everything to text would break real numeric merges --
        // lexically "9" is greater than "10", numerically it is not.
        Branch("9", MergeConditionOperator.GreaterThan, "10").Should().Be("no");
        Branch("10", MergeConditionOperator.GreaterThan, "9").Should().Be("yes");
        Branch("1,5", MergeConditionOperator.Equal, "1.5").Should().Be("no");
    }

    [Fact]
    public void Ordinary_text_still_compares_as_text()
    {
        // Pins the pre-existing fallback the fix routes into.
        Branch("VIP", MergeConditionOperator.Equal, "VIP").Should().Be("yes");
        Branch("VIP", MergeConditionOperator.Equal, "Regular").Should().Be("no");
    }
}
