using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.DocumentView;

public sealed class AutomaticHyphenationDisplayPlannerTests
{
    [Fact]
    public void Enabled_returns_model_relative_break_offsets_without_mutating_text()
    {
        const string text = "hyphenation rabbit";
        var page = new PageSettings { AutoHyphenation = true };

        var offsets = AutomaticHyphenationDisplayPlanner.BuildBreakOffsets(
            text,
            page,
            ParagraphFormatting.Default);

        offsets.Should().Contain(2);
        offsets.Should().OnlyContain(offset => offset > 0 && offset < text.Length);
        text.Should().Be("hyphenation rabbit");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Disabled_or_suppressed_returns_no_breaks(bool enabled, bool suppressed)
    {
        var page = new PageSettings { AutoHyphenation = enabled };
        var formatting = ParagraphFormatting.Default with { SuppressAutoHyphens = suppressed };

        AutomaticHyphenationDisplayPlanner.BuildBreakOffsets("hyphenation", page, formatting)
            .Should().BeEmpty();
    }

    [Fact]
    public void Do_not_hyphenate_caps_skips_caps_but_keeps_lowercase_words()
    {
        const string text = "HYPHENATION hyphenation";
        var page = new PageSettings
        {
            AutoHyphenation = true,
            DoNotHyphenateCaps = true,
        };

        var offsets = AutomaticHyphenationDisplayPlanner.BuildBreakOffsets(
            text,
            page,
            ParagraphFormatting.Default);

        offsets.Should().OnlyContain(offset => offset > "HYPHENATION ".Length);
        offsets.Should().NotBeEmpty();
    }

    [Fact]
    public void Existing_soft_hyphen_is_not_reported_as_generated_and_does_not_shift_later_offsets()
    {
        var text = $"hy{Hyphenator.SoftHyphen}phenation rabbit";
        var page = new PageSettings { AutoHyphenation = true };
        var rabbitStart = text.IndexOf("rabbit", StringComparison.Ordinal);
        var expectedRabbitBreak = rabbitStart + Hyphenator.BreakPoints("rabbit")[0];

        var offsets = AutomaticHyphenationDisplayPlanner.BuildBreakOffsets(
            text,
            page,
            ParagraphFormatting.Default);

        offsets.Should().Contain(expectedRabbitBreak);
        offsets.Should().OnlyContain(offset => offset > rabbitStart);
    }

    [Theory]
    [InlineData(0, 18, false)]
    [InlineData(0, 18.01, true)]
    [InlineData(36, 36, false)]
    [InlineData(36, 36.01, true)]
    public void Line_decision_applies_default_or_authored_zone_at_the_exact_boundary(
        double zonePt,
        double trailingWhitespacePt,
        bool expected)
    {
        var page = new PageSettings
        {
            AutoHyphenation = true,
            HyphenationZonePt = zonePt,
        };

        AutomaticHyphenationDisplayPlanner.AllowsAutomaticLineBreak(
                page,
                consecutiveHyphenatedLines: 0,
                hasOrdinaryWordBreak: true,
                trailingWhitespacePt)
            .Should().Be(expected);
    }

    [Fact]
    public void Line_decision_does_not_apply_zone_when_no_whole_word_break_exists()
    {
        var page = new PageSettings
        {
            AutoHyphenation = true,
            HyphenationZonePt = 72,
        };

        AutomaticHyphenationDisplayPlanner.AllowsAutomaticLineBreak(
                page,
                consecutiveHyphenatedLines: 0,
                hasOrdinaryWordBreak: false,
                ordinaryTrailingWhitespacePt: 0)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 20, true)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    public void Line_decision_honors_zero_as_unlimited_and_positive_consecutive_limit(
        int limit,
        int currentConsecutiveLines,
        bool expected)
    {
        var page = new PageSettings
        {
            AutoHyphenation = true,
            ConsecutiveHyphenLimit = limit,
        };

        AutomaticHyphenationDisplayPlanner.AllowsAutomaticLineBreak(
                page,
                currentConsecutiveLines,
                hasOrdinaryWordBreak: false,
                ordinaryTrailingWhitespacePt: 0)
            .Should().Be(expected);
    }
}
