using FluentAssertions;
using FreeX.App.Services.Updates;
using Xunit;

namespace FreeX.App.Services.Tests.Updates;

public class UpdateServiceDecisionTests
{
    private static VelopackUpdateService Service(Func<CancellationToken, Task<DownloadedUpdate?>> probe) =>
        new(releasesPageUrl: "https://example/releases", downloadProbe: probe);

    [Fact]
    public async Task NoUpdate_ReportsUpToDate()
    {
        var svc = Service(_ => Task.FromResult<DownloadedUpdate?>(null));
        var result = await svc.CheckAndDownloadAsync();
        result.State.Should().Be(UpdateState.UpToDate);
    }

    [Fact]
    public async Task UpdateDownloaded_ReportsReadyToApplyWithVersion()
    {
        var svc = Service(_ => Task.FromResult<DownloadedUpdate?>(new DownloadedUpdate("0.6.0")));
        var result = await svc.CheckAndDownloadAsync();
        result.State.Should().Be(UpdateState.ReadyToApply);
        result.AvailableVersion.Should().Be("0.6.0");
    }

    [Fact]
    public async Task ProbeThrows_ReportsUnavailable_NeverThrows()
    {
        var svc = Service(_ => throw new InvalidOperationException("offline"));
        var result = await svc.CheckAndDownloadAsync();
        result.State.Should().Be(UpdateState.Unavailable);
    }
}
