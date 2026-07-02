using FreeW.App.Presentation.Links;

namespace FreeW.App.Presentation.Tests;

public sealed class HyperlinkTargetTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("  mailto:team@example.com  ", "mailto:team@example.com")]
    public void TryParse_keeps_external_targets_as_urls(string input, string expectedUrl)
    {
        HyperlinkTarget.TryParse(input, out var target).Should().BeTrue();

        target.Url.Should().Be(expectedUrl);
        target.Anchor.Should().BeNull();
        target.DisplayFallback.Should().Be(expectedUrl);
    }

    [Theory]
    [InlineData("#Bookmark1", "Bookmark1")]
    [InlineData("  #Section 2  ", "Section 2")]
    public void TryParse_normalizes_internal_bookmark_targets(string input, string expectedAnchor)
    {
        HyperlinkTarget.TryParse(input, out var target).Should().BeTrue();

        target.Url.Should().BeNull();
        target.Anchor.Should().Be(expectedAnchor);
        target.DisplayFallback.Should().Be(expectedAnchor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData(" #  ")]
    public void TryParse_rejects_blank_targets(string input)
    {
        HyperlinkTarget.TryParse(input, out var target).Should().BeFalse();

        target.HasTarget.Should().BeFalse();
    }
}
