using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class HyperlinkNavigationPlannerTests
{
    [Fact]
    public void IsAllowedScheme_HttpUrl_ReturnsTrue()
        => HyperlinkNavigationPlanner.IsAllowedScheme("http://example.com").Should().BeTrue();

    [Fact]
    public void IsAllowedScheme_HttpsUrl_ReturnsTrue()
        => HyperlinkNavigationPlanner.IsAllowedScheme("https://example.com").Should().BeTrue();

    [Fact]
    public void IsAllowedScheme_MailtoUrl_ReturnsTrue()
        => HyperlinkNavigationPlanner.IsAllowedScheme("mailto:user@example.com").Should().BeTrue();

    [Fact]
    public void IsAllowedScheme_JavascriptUrl_ReturnsFalse()
        => HyperlinkNavigationPlanner.IsAllowedScheme("javascript:alert(1)").Should().BeFalse();

    [Fact]
    public void IsAllowedScheme_DataUrl_ReturnsFalse()
        => HyperlinkNavigationPlanner.IsAllowedScheme("data:text/html,<h1>hi</h1>").Should().BeFalse();

    [Fact]
    public void IsAllowedScheme_VbscriptUrl_ReturnsFalse()
        => HyperlinkNavigationPlanner.IsAllowedScheme("vbscript:MsgBox(1)").Should().BeFalse();

    [Fact]
    public void IsAllowedScheme_RelativeUrl_ReturnsFalse()
        => HyperlinkNavigationPlanner.IsAllowedScheme("relative/path/file.xlsx").Should().BeFalse();

    [Fact]
    public void IsAllowedScheme_EmptyString_ReturnsFalse()
        => HyperlinkNavigationPlanner.IsAllowedScheme("").Should().BeFalse();
}
