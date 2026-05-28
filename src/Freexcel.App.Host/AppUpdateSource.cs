namespace Freexcel.App.Host;

public sealed record AppUpdateSource(string ReleasePageUrl, string LatestDownloadUrl)
{
    public static AppUpdateSource CreateDefault() =>
        new(AppInfo.LatestReleaseUrl, AppInfo.LatestTesterDownloadUrl);
}
