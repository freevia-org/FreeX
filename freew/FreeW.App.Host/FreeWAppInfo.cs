using System.Reflection;
using FreeW.App.Presentation;

namespace FreeW.App.Host;

public static class FreeWAppInfo
{
    public const string ProductName = FreeWProductInfo.ProductName;
    public const string HelpUrl = FreeWProductInfo.HelpUrl;
    public const string FeedbackUrl = FreeWProductInfo.FeedbackUrl;
    public const string LatestReleaseUrl = FreeWProductInfo.LatestReleaseUrl;
    public const string TrademarkNotice = FreeWProductInfo.TrademarkNotice;
    public const string ProjectLicenseNotice = FreeWProductInfo.ProjectLicenseNotice;
    public const string PrivacyNotice = FreeWProductInfo.PrivacyNotice;
    public const string SourceNotice = FreeWProductInfo.SourceNotice;

    public static string VersionText { get; } = FreeWProductInfo.GetVersionText(typeof(FreeWAppInfo).Assembly);

    public static string ExactVersionText { get; } = FreeWProductInfo.GetBuildVersionText(typeof(FreeWAppInfo).Assembly);

    public static AboutDialogPresentation AboutPresentation { get; } =
        FreeWAboutDialogPresentation.Create(typeof(FreeWAppInfo).Assembly, "WPF");

    public static string AboutText => AboutPresentation.AboutText;

    public static string GetVersionText(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return FreeWProductInfo.GetVersionText(assembly);
    }

    public static string GetBuildVersionText(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return FreeWProductInfo.GetBuildVersionText(assembly);
    }

    /// <summary>
    /// Formats <paramref name="informationalVersion"/> as a display version string.
    /// FreeW preserves the full three-part version (e.g. <c>0.5.0</c> stays <c>0.5.0</c>);
    /// delegates to <see cref="AppVersionFormatter.FormatVersionText"/> with the default
    /// <c>dropTrailingZeroPatch: false</c>.
    /// </summary>
    public static string FormatVersionText(string? informationalVersion) =>
        AppVersionFormatter.FormatVersionText(informationalVersion);

    /// <inheritdoc cref="AppVersionFormatter.FormatBuildVersionText"/>
    public static string FormatBuildVersionText(string? informationalVersion, string? assemblyVersion = null) =>
        AppVersionFormatter.FormatBuildVersionText(informationalVersion, assemblyVersion);

    public static string CreateDiagnosticsText(string diagnosticsDirectory, string optionsPath)
    {
        return FreeWProductInfo.CreateDiagnosticsText(
            typeof(FreeWAppInfo).Assembly,
            diagnosticsDirectory,
            optionsPath);
    }

}
