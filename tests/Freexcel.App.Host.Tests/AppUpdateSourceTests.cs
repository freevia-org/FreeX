using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class AppUpdateSourceTests
{
    [Fact]
    public void AppUpdateSource_UsesStableLatestReleaseAndDownloadLinks()
    {
        var source = AppUpdateSource.CreateDefault();

        source.ReleasePageUrl.Should().Be(AppInfo.LatestReleaseUrl);
        source.LatestDownloadUrl.Should().Be(AppInfo.LatestTesterDownloadUrl);
    }
}
