namespace FreeX.App.Host;

public sealed record AppUpdateSource(string ReleasePageUrl)
{
    public static AppUpdateSource CreateDefault() =>
        new(AppInfo.LatestReleaseUrl);
}
