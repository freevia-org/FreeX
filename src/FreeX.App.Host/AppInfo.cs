namespace FreeX.App.Host;

public static class AppInfo
{
    public const string VersionText = "Version 0.5 (Tester Release)";
    public const string HelpUrl = "https://github.com/tony-xmelon/FreeX";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/FreeX/issues/new";
    public const string LatestReleaseUrl = "https://github.com/tony-xmelon/FreeX/releases/latest";
    public const string LatestTesterDownloadUrl = "https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe";
    public const string TrademarkNotice = "FreeX is not affiliated with, endorsed by, or sponsored by Microsoft. Microsoft Excel is a trademark of Microsoft Corporation.";

    public static string AboutText { get; } =
        $"FreeX\n{VersionText}\n\nA free spreadsheet app for .xlsx files.\n\nBuilt with .NET 10, WPF, ClosedXML, OxyPlot.\n\n{TrademarkNotice}";
}
