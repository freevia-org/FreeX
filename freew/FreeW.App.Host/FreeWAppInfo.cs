using FreeW.App.Presentation;

namespace FreeW.App.Host;

public static class FreeWAppInfo
{
    public static AboutDialogPresentation AboutPresentation { get; } =
        FreeWAboutDialogPresentation.Create(typeof(FreeWAppInfo).Assembly);

    public static string AboutText => AboutPresentation.AboutText;

    public static string FeedbackUrl => FreeWProductInfo.CreateFeedbackUrl(typeof(FreeWAppInfo).Assembly);

    public static string CreateDiagnosticsText(string diagnosticsDirectory, string optionsPath) =>
        FreeWProductInfo.CreateDiagnosticsText(
            typeof(FreeWAppInfo).Assembly,
            diagnosticsDirectory,
            optionsPath);
}
