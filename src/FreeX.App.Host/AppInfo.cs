using FreeX.App.Services;

namespace FreeX.App.Host;

public static class AppInfo
{
    public static string VersionText { get; } = AppHelpInfo.GetVersionText(typeof(AppInfo).Assembly);

    public static string ExactVersionText { get; } = AppHelpInfo.GetBuildVersionText(typeof(AppInfo).Assembly);

    /// <summary>
    /// Release channel for self-update. The tester channel pulls GitHub pre-releases
    /// (see <c>UpdateFeed.AllowPrereleases</c>); mirrors <c>release/progress.json</c>'s "channel".
    /// </summary>
    public const string ReleaseChannel = AppHelpInfo.ReleaseChannel;

    public const string HelpUrl = AppHelpInfo.HelpUrl;
    public const string FeedbackUrl = AppHelpInfo.FeedbackUrl;
    public const string LatestReleaseUrl = AppHelpInfo.LatestReleaseUrl;
    public const string LatestTesterDownloadUrl = "https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe";
    public const string TrademarkNotice = AppHelpInfo.TrademarkNotice;
    public const string ProjectLicenseNotice = AppHelpInfo.ProjectLicenseNotice;
    public const string PrivacyNotice = AppHelpInfo.PrivacyNotice;
    public const string CompatibilityNotice = AppHelpInfo.CompatibilityNotice;
    public const string ThirdPartyRuntimeNotice =
        "Third-party runtime notices: Runtime dependencies remain governed by their own licenses, including permissive and LGPL-licensed components. Package identities, versions, notices, complete bundled license texts, and distribution requirements are available in Help > Legal Notices. Release packaging must preserve those materials.";
    public const string SourceNotice = AppHelpInfo.SourceNotice;

    public static string AboutText { get; } =
        FreeXAboutDialogPresentation.Create(
            typeof(AppInfo).Assembly,
            "WPF",
            thirdPartyRuntimeNotice: ThirdPartyRuntimeNotice).AboutText;

    internal static string FormatVersionText(string? informationalVersion) =>
        AppHelpInfo.FormatVersionText(informationalVersion);
}
