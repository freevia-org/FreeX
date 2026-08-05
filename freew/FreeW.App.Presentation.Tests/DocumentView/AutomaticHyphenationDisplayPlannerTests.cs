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
}
