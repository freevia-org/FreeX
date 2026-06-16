using FluentAssertions;
using FreeX.App.Services.Updates;
using Xunit;

namespace FreeX.App.Services.Tests.Updates;

public class UpdateFeedTests
{
    [Fact]
    public void GitHubFeedUrl_IsRepoReleasesRoot()
    {
        UpdateFeed.GitHubRepoUrl.Should().Be("https://github.com/tony-xmelon/FreeX");
    }

    [Theory]
    [InlineData("test", true)]
    [InlineData("stable", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PrereleaseChannel_OnlyTesterPullsPrereleases(string? channel, bool expectedPrerelease)
    {
        UpdateFeed.AllowPrereleases(channel).Should().Be(expectedPrerelease);
    }
}
