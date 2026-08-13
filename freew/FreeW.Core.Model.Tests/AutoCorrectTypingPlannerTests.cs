namespace FreeW.Core.Model.Tests;

public sealed class AutoCorrectTypingPlannerTests
{
    [Fact]
    public void Disabled_planner_never_applies_either_rule_family()
    {
        AutoCorrectTypingPlanner.Build(
                "teh",
                ' ',
                enabled: false,
                AutoCorrectOptions.Default,
                AutoFormatOptions.Default)
            .Should().Be(AutoCorrectTypingPlan.None);

        AutoCorrectTypingPlanner.Build(
                "-",
                '-',
                enabled: false,
                AutoCorrectOptions.Default,
                AutoFormatOptions.Default)
            .Should().Be(AutoCorrectTypingPlan.None);
    }

    [Fact]
    public void User_AutoCorrect_table_has_precedence_over_AutoFormat()
    {
        var autoCorrect = AutoCorrectOptions.AllOff;
        autoCorrect.ReplaceText = true;
        autoCorrect.Replacements = [new AutoCorrectReplacement("www.example.com", "custom")];

        var plan = AutoCorrectTypingPlanner.Build(
            "www.example.com",
            ' ',
            enabled: true,
            autoCorrect,
            AutoFormatOptions.Default);

        plan.Applies.Should().BeTrue();
        plan.Result.Insert.Should().Be("custom ");
        plan.Result.Outcome.Should().Be(AutoFormatOutcomeKind.None);
        plan.ReplacementStartOffset.Should().Be(0);
    }

    [Fact]
    public void AutoFormat_is_used_when_AutoCorrect_has_no_match()
    {
        var plan = AutoCorrectTypingPlanner.Build(
            "-",
            '-',
            enabled: true,
            AutoCorrectOptions.AllOff,
            AutoFormatOptions.Default);

        plan.Applies.Should().BeTrue();
        plan.Result.DeleteBefore.Should().Be(1);
        plan.ReplacementStartOffset.Should().Be(0);
    }

    [Fact]
    public void List_start_context_suppresses_only_AutoFormat_capitalization()
    {
        var ordinary = AutoCorrectTypingPlanner.Build(
            string.Empty,
            'a',
            enabled: true,
            AutoCorrectOptions.AllOff,
            AutoFormatOptions.Default,
            suppressCapitalizationAtListStart: false);
        var listStart = AutoCorrectTypingPlanner.Build(
            string.Empty,
            'a',
            enabled: true,
            AutoCorrectOptions.AllOff,
            AutoFormatOptions.Default,
            suppressCapitalizationAtListStart: true);

        ordinary.Result.Insert.Should().Be("A");
        listStart.Should().Be(AutoCorrectTypingPlan.None);
    }

    [Fact]
    public void Plan_reports_paragraph_relative_replacement_start()
    {
        var plan = AutoCorrectTypingPlanner.Build(
            "I teh",
            ' ',
            enabled: true,
            AutoCorrectOptions.Default,
            AutoFormatOptions.AllOff);

        plan.Applies.Should().BeTrue();
        plan.Result.DeleteBefore.Should().Be(3);
        plan.ReplacementStartOffset.Should().Be(2);
    }
}
