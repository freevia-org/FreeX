namespace FreeX.App.Host;

public static class AppInfo
{
    public const string VersionText = "Version 0.5 (Tester Release)";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/FreeX/issues/new";
    public const string LatestReleaseUrl = "https://github.com/tony-xmelon/FreeX/releases/latest";
    public const string LatestTesterDownloadUrl = "https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe";

    public static string AboutText { get; } =
        $"FreeX\n{VersionText}\n\nBuilt with .NET 10, WPF, ClosedXML, OxyPlot.";
}
