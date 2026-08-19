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

    // F1: ApplyAndRestart used to swallow a failed apply/restart with only a log line and no way
    // for the caller to know the app is still on the old version. It must now report failure back
    // via its return value instead of silently doing nothing observable.
    [Fact]
    public void ApplyAndRestart_WhenApplyThrows_ReturnsFalse_AndDoesNotThrow()
    {
        var svc = new VelopackUpdateService(
            releasesPageUrl: "https://example/releases",
            downloadProbe: _ => Task.FromResult<DownloadedUpdate?>(null),
            applyAndRestart: () => throw new InvalidOperationException("staged package missing"));

        var applied = svc.ApplyAndRestart();

        applied.Should().BeFalse();
    }

    // Sibling/no-regression: the ordinary path (apply delegate runs without throwing -- the
    // production case where Velopack either restarts the process or there was nothing staged)
    // must still report success and must not start throwing now that the method has a return
    // value.
    [Fact]
    public void ApplyAndRestart_WhenApplyDoesNotThrow_ReturnsTrue()
    {
        var invoked = false;
        var svc = new VelopackUpdateService(
            releasesPageUrl: "https://example/releases",
            downloadProbe: _ => Task.FromResult<DownloadedUpdate?>(null),
            applyAndRestart: () => invoked = true);

        var applied = svc.ApplyAndRestart();

        applied.Should().BeTrue();
        invoked.Should().BeTrue();
    }
}
