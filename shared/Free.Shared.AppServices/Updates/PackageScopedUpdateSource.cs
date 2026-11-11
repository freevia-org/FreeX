using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Free.Shared.AppServices.Updates;

/// <summary>
/// Narrows a release feed to the packages of the app that is asking.
///
/// <para>
/// FreeX, FreeW and FreeP publish from one repository, each under its own release tag, and every
/// one of those releases carries a <c>releases.{channel}.json</c>. <see cref="GithubSource"/>
/// merges the feeds of the newest releases without looking at the package id, and the update
/// manager then takes the highest version in the merged list. Left unscoped, an installed app is
/// offered whichever sibling published the highest version most recently: measured against the
/// live 0.8.190 feed, a FreeW or FreeP 0.8.189 install resolved to <c>FreeXApp-0.8.190-full.nupkg</c>,
/// and FreeX resolved to its own package only because it happened to publish last.
/// </para>
/// </summary>
public sealed class PackageScopedUpdateSource : IUpdateSource
{
    private readonly IUpdateSource _inner;

    public PackageScopedUpdateSource(IUpdateSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null)
    {
        var feed = await _inner.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease)
            .ConfigureAwait(false);

        // No identity to scope by means the caller is not an installed app; an empty feed is the
        // only answer that cannot hand it a sibling's package.
        if (string.IsNullOrEmpty(appId))
            return new VelopackAssetFeed { Assets = [] };

        // The original asset instances are kept: the inner source downloads by the release it
        // attached to each one.
        return new VelopackAssetFeed
        {
            Assets = [.. (feed.Assets ?? []).Where(asset =>
                string.Equals(asset.PackageId, appId, StringComparison.OrdinalIgnoreCase))],
        };
    }

    public Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken = default) =>
        _inner.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
}
