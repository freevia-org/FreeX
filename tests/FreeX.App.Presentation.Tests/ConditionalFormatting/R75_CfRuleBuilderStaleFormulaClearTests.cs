using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// Regression tests for R75-io-cf-classic-4-3: editing an existing text (ContainsText/
/// NotContainsText/BeginsWith/EndsWith) or Date Occurring rule updates
/// <see cref="ConditionalFormat.TextRuleText"/>/<see cref="ConditionalFormat.DateOccurringPeriod"/>
/// but must also clear the STALE <see cref="ConditionalFormat.FormulaText"/> cloned from the loaded
/// rule -- otherwise <see cref="XlsxAdvancedConditionalFormatWriter"/> keeps writing the OLD
/// condition (e.g. still searching for "foo" after the user changed the text to "bar"), silently
/// discarding the edit on save/reload.
/// </summary>
public sealed class R75_CfRuleBuilderStaleFormulaClearTests
{
    [Fact]
    public void Build_EditingContainsTextRuleText_ClearsStaleFormulaText()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.ContainsText,
            AppliesTo = Range(),
            TextRuleText = "foo",
            FormulaText = "NOT(ISERROR(SEARCH(\"foo\",A1)))",
        };
        var input = new CfRuleInput { RuleType = CfRuleType.ContainsText, Text = "bar" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.TextRuleText.Should().Be("bar");
        rule.FormulaText.Should().BeNull(
            "the stale formula (still searching for the OLD text \"foo\") must be cleared so the " +
            "writer's synthesis fallback regenerates it for the new text on save");
    }

    [Fact]
    public void Build_EditingDateOccurringPeriod_ClearsStaleFormulaText()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.DateOccurring,
            AppliesTo = Range(),
            DateOccurringPeriod = "today",
            FormulaText = "FLOOR(A1,1)=TODAY()",
        };
        var input = new CfRuleInput { RuleType = CfRuleType.DateOccurring, DatePeriod = "yesterday" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.DateOccurringPeriod.Should().Be("yesterday");
        rule.FormulaText.Should().BeNull(
            "the stale formula (still evaluating the OLD period \"today\") must be cleared so the " +
            "writer's synthesis fallback regenerates it for the new period on save");
    }

    /// <summary>
    /// Sibling no-regression case: a rule loaded but NOT edited (the input's text/period matches the
    /// existing rule's value verbatim) must keep its original FormulaText -- no spurious clear.
    /// </summary>
    [Fact]
    public void Build_ReapplyingContainsTextRuleWithUnchangedText_PreservesFormulaText()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.ContainsText,
            AppliesTo = Range(),
            TextRuleText = "foo",
            FormulaText = "NOT(ISERROR(SEARCH(\"foo\",A1)))",
        };
        var input = new CfRuleInput { RuleType = CfRuleType.ContainsText, Text = "foo" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.TextRuleText.Should().Be("foo");
        rule.FormulaText.Should().Be(
            "NOT(ISERROR(SEARCH(\"foo\",A1)))",
            "re-applying the same text must not spuriously clear an already-correct FormulaText");
    }

    /// <summary>Sibling no-regression case: same as above, for an unchanged Date Occurring period.</summary>
    [Fact]
    public void Build_ReapplyingDateOccurringRuleWithUnchangedPeriod_PreservesFormulaText()
    {
        var existing = new ConditionalFormat
        {
            RuleType = CfRuleType.DateOccurring,
            AppliesTo = Range(),
            DateOccurringPeriod = "today",
            FormulaText = "FLOOR(A1,1)=TODAY()",
        };
        var input = new CfRuleInput { RuleType = CfRuleType.DateOccurring, DatePeriod = "today" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), existingRule: existing);

        rule.DateOccurringPeriod.Should().Be("today");
        rule.FormulaText.Should().Be(
            "FLOOR(A1,1)=TODAY()",
            "re-applying the same period must not spuriously clear an already-correct FormulaText");
    }

    private static GridRange Range() => RangeAt(new SheetId(Guid.NewGuid()), 0, 0, 4, 0);

    private static GridRange RangeAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet, r1, c1), new CellAddress(sheet, r2, c2));
}
