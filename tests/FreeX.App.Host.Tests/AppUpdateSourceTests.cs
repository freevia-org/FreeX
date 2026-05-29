using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

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
