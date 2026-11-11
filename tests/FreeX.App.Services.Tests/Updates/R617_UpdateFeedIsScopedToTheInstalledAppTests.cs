using FluentAssertions;
using Free.Shared.AppServices.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace FreeX.App.Services.Tests.Updates;

/// <summary>
/// r617: FreeX, FreeW and FreeP release from one repository, so the merged GitHub feed holds all
/// three apps' packages. Velopack picks the highest version without comparing package ids, which
/// offered an installed app its sibling's package.
/// </summary>
public sealed class R617_UpdateFeedIsScopedToTheInstalledAppTests
{
    [Fact]
    public async Task UnscopedFeed_OffersASiblingsPackage_SoTheHazardIsReal()
    {
        var target = await CheckForUpdate(SharedRepositoryFeed(), installedAppId: "FreeXApp", installedVersion: "0.8.190");

        target.Should().NotBeNull("this is the control: without scoping Velopack takes the highest version in the feed");
        target!.PackageId.Should().Be("FreeWApp");
    }

    [Fact]
    public async Task ScopedFeed_IgnoresASiblingsNewerRelease()
    {
        var target = await CheckForUpdate(
            new PackageScopedUpdateSource(SharedRepositoryFeed()), installedAppId: "FreeXApp", installedVersion: "0.8.190");

        target.Should().BeNull("FreeW shipping 0.8.191 on its own is not an update for FreeX 0.8.190");
    }

    [Fact]
    public async Task ScopedFeed_StillFindsTheAppsOwnUpdate()
    {
        var target = await CheckForUpdate(
            new PackageScopedUpdateSource(SharedRepositoryFeed()), installedAppId: "FreeXApp", installedVersion: "0.8.189");

        target.Should().NotBeNull();
        target!.PackageId.Should().Be("FreeXApp");
        target.Version.Should().Be(SemanticVersion.Parse("0.8.190"));
    }

    [Fact]
    public async Task ScopedFeed_WithNoAppIdentity_OffersNothing()
    {
        var feed = await new PackageScopedUpdateSource(SharedRepositoryFeed())
            .GetReleaseFeed(new NullVelopackLogger(), appId: null, channel: "win");

        feed.Assets.Should().BeEmpty();
    }

    [Fact]
    public void ProductionOrchestrator_ScopesTheGitHubFeed()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "shared", "Free.Shared.AppServices", "Updates", "VelopackUpdateOrchestrator.cs"));

        source.Should().Contain("new PackageScopedUpdateSource(new GithubSource(");
        source.Should().NotContain("new UpdateManager(new GithubSource(");
    }

    private static async Task<VelopackAsset?> CheckForUpdate(IUpdateSource source, string installedAppId, string installedVersion)
    {
        var packages = Path.Combine(Path.GetTempPath(), "r617-packages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packages);
        try
        {
            var locator = new TestVelopackLocator(installedAppId, installedVersion, packages, new NullVelopackLogger());
            var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" }, locator);
            var info = await manager.CheckForUpdatesAsync();
            return info?.TargetFullRelease;
        }
        finally
        {
            Directory.Delete(packages, recursive: true);
        }
    }

    // Newest release first, as GithubSource orders it: FreeW has shipped 0.8.191 alone.
    private static FakeSource SharedRepositoryFeed() => new(
        Asset("FreeWApp", "0.8.191"),
        Asset("FreeXApp", "0.8.190"),
        Asset("FreeWApp", "0.8.190"),
        Asset("FreePApp", "0.8.190"),
        Asset("FreeXApp", "0.8.189"));

    private static VelopackAsset Asset(string packageId, string version) => new()
    {
        PackageId = packageId,
        Version = SemanticVersion.Parse(version),
        Type = VelopackAssetType.Full,
        FileName = $"{packageId}-{version}-full.nupkg",
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeSource(params VelopackAsset[] assets) : IUpdateSource
    {
        public Task<VelopackAssetFeed> GetReleaseFeed(
            IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) =>
            Task.FromResult(new VelopackAssetFeed { Assets = assets });

        public Task DownloadReleaseEntry(
            IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default) =>
            throw new NotSupportedException("The check must not download.");
    }

    private sealed class NullVelopackLogger : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) { }
    }
}
