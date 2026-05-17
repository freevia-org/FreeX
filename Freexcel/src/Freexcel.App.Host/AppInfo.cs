namespace Freexcel.App.Host;

public static class AppInfo
{
    public const string VersionText = "Version 0.5 (Phase 5)";
    public const string HelpUrl = "https://github.com/tony-xmelon/Freexcel";
    public const string FeedbackUrl = "https://github.com/tony-xmelon/Freexcel/issues/new";

    public static string AboutText { get; } =
        $"Freexcel\n{VersionText}\n\nBuilt with .NET 10, WPF, ClosedXML, OxyPlot.";
}
